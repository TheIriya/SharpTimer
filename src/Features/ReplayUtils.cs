/*
Copyright (C) 2024 Dea Brcka

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.
This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.
You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Memory;
using System.Text.Json;
using FixVectorLeak;

namespace SharpTimer
{
    public partial class SharpTimer
    {
        private void ReplayUpdate(CCSPlayerController player, int timerTicks)
        {
            try
            {
                if (!IsAllowedPlayer(player)) return;

                // Get the player's current position and rotation
                ReplayVector currentPosition =
                    ReplayVector.GetVectorish(player.Pawn.Value!.CBodyComponent?.SceneNode?.AbsOrigin ?? new(0, 0, 0));
                ReplayVector currentSpeed = ReplayVector.GetVectorish(player.PlayerPawn.Value!.AbsVelocity);
                ReplayQAngle currentRotation = ReplayQAngle.GetQAngleish(player.PlayerPawn.Value.EyeAngles);

                var buttons = player.Buttons;
                var flags = player.Pawn.Value.Flags;
                var moveType = player.Pawn.Value.MoveType;

                var ReplayFrame = new PlayerReplays.ReplayFrames
                {
                    Position = currentPosition,
                    Rotation = currentRotation,
                    Speed = currentSpeed,
                    Buttons = buttons,
                    Flags = flags,
                    MoveType = moveType
                };

                playerReplays[player.Slot].replayFrames.Add(ReplayFrame);
            }
            catch (Exception ex)
            {
                Utils.LogError($"Error in ReplayUpdate: {ex.Message}");
            }
        }

        private void ReplayPlayback(CCSPlayerController player, int plackbackTick)
        {
            try
            {
                if (!IsAllowedPlayer(player)) return;

                if (playerTimers.TryGetValue(player.Slot, out PlayerTimerInfo? value))
                {
                    var replayFrame = playerReplays[player.Slot].replayFrames[plackbackTick];
                    var pawn = player.PlayerPawn.Value;
                    if (pawn == null || pawn.MovementServices == null) return;

                    // Recorded positions drive playback. Observer movement changes the
                    // camera rules, and walking lets physics move the pawn between frames.
                    if (pawn.MoveType != MoveType_t.MOVETYPE_NONE || pawn.ActualMoveType != MoveType_t.MOVETYPE_NONE)
                        SetMoveType(player, MoveType_t.MOVETYPE_NONE);
                    value.MovementService = new CCSPlayer_MovementServices(pawn.MovementServices.Handle);
                    bool ducked = ((PlayerFlags)replayFrame.Flags & PlayerFlags.FL_DUCKING) != 0;
                    SetReplayPosture(pawn, value.MovementService, ducked);

                    bool resetCamera = playerReplays[player.Slot].CameraContinuity.Advance(pawn.Handle, plackbackTick,
                        new(replayFrame.Position!.X, replayFrame.Position.Y, replayFrame.Position.Z),
                        new(replayFrame.Speed!.X, replayFrame.Speed.Y, replayFrame.Speed.Z));

                    pawn.Teleport(ReplayVector.ToVector(replayFrame.Position!),
                        ReplayQAngle.ToQAngle(replayFrame.Rotation!), ReplayVector.ToVector(replayFrame.Speed!));
                    if (resetCamera)
                    {
                        // Snap across map teleports and replay loops instead of moving
                        // the spectator camera through the space between both endpoints.
                        pawn.InterpolationFrame = unchecked((byte)(pawn.InterpolationFrame + 1));
                        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_ubInterpolationFrame");
                        SnapBaseAngles.Snap(pawn, new QAngle_t(replayFrame.Rotation!.Pitch,
                            replayFrame.Rotation.Yaw, replayFrame.Rotation.Roll));
                    }
                    if (pawn.CameraServices != null)
                    {
                        pawn.CameraServices.OldPlayerZ = replayFrame.Position!.Z;
                        pawn.CameraServices.OldPlayerViewOffsetZ = pawn.ViewOffset.Z;
                    }

                    // Observers read the buttons from the frame actually applied to the pawn.
                    playerReplays[player.Slot].LastPlaybackButtons = replayFrame.Buttons;
                    // GetHudContent renders playback keys using replay_hud.json for
                    // both the replaying player and spectators.
                }
            }
            catch (Exception ex)
            {
                Utils.LogError($"Error in ReplayPlayback: {ex.Message}");
            }
        }

        private static void SetReplayPosture(CCSPlayerPawn pawn, CCSPlayer_MovementServices movement, bool ducked)
        {
            movement.DuckAmount = ducked ? 1f : 0f;
            movement.Ducked = ducked;
            movement.Ducking = false;
            movement.DesiresDuck = ducked;
            movement.DuckUntilOnGround = false;
            movement.DuckRootOffset = 0f;
            movement.DuckViewOffset = 0f;
            movement.UsingGroundTopologyOffset = false;
            movement.UsingGroundTopologyOffsetTransitionSmoothing = 0f;
            uint flags = ducked ? pawn.Flags | (uint)PlayerFlags.FL_DUCKING : pawn.Flags & ~(uint)PlayerFlags.FL_DUCKING;
            if (pawn.Flags != flags)
            {
                pawn.Flags = flags;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_fFlags");
            }
            float viewHeight = ducked ? 46f : 64f;
            // m_vecViewOffset is an embedded network object, not a network leaf.
            // Mark the changed component at its schema offset, never its parent.
            if (pawn.ViewOffset.X != 0f)
            {
                pawn.ViewOffset.X = 0f;
                MarkReplayViewComponentChanged(pawn, "m_vecX");
            }
            if (pawn.ViewOffset.Y != 0f)
            {
                pawn.ViewOffset.Y = 0f;
                MarkReplayViewComponentChanged(pawn, "m_vecY");
            }
            if (pawn.ViewOffset.Z != viewHeight)
            {
                pawn.ViewOffset.Z = viewHeight;
                MarkReplayViewComponentChanged(pawn, "m_vecZ");
            }
        }

        private static void MarkReplayViewComponentChanged(CCSPlayerPawn pawn, string component) =>
            Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_vecViewOffset",
                Schema.GetSchemaOffset("CNetworkViewOffsetVector", component));

        private void ResetReplayPosture(CCSPlayerController player)
        {
            var pawn = player.PlayerPawn.Value;
            if (pawn == null || !pawn.IsValid || pawn.MovementServices == null) return;
            SetReplayPosture(pawn, new CCSPlayer_MovementServices(pawn.MovementServices.Handle), false);
            if (pawn.CameraServices != null)
                pawn.CameraServices.OldPlayerViewOffsetZ = pawn.ViewOffset.Z;
        }

        private void ReplayPlay(CCSPlayerController player)
        {
            try
            {
                int totalFrames = playerReplays[player.Slot].replayFrames.Count;

                if (totalFrames == 0) return;

                if (playerReplays[player.Slot].CurrentPlaybackFrame < 0 ||
                    playerReplays[player.Slot].CurrentPlaybackFrame >= totalFrames)
                {
                    playerReplays[player.Slot].CurrentPlaybackFrame = 0;
                    Action<CCSPlayerController?, float, bool> adjustVelocity =
                        use2DSpeed ? AdjustPlayerVelocity2D : AdjustPlayerVelocity;
                    adjustVelocity(player, 0, false);
                }

                ReplayPlayback(player, playerReplays[player.Slot].CurrentPlaybackFrame);

                playerReplays[player.Slot].CurrentPlaybackFrame++;
            }
            catch (Exception ex)
            {
                Utils.LogError($"Error in ReplayPlay: {ex.Message}");
            }
        }

        private void OnRecordingStart(CCSPlayerController player, int bonusX = 0, int style = 0)
        {
            try
            {
                playerReplays.Remove(player.Slot);
                playerReplays[player.Slot] = new PlayerReplays
                {
                    BonusX = bonusX,
                    Style = style
                };
                playerTimers[player.Slot].IsRecordingReplay = true;
            }
            catch (Exception ex)
            {
                Utils.LogError($"Error in OnRecordingStart: {ex.Message}");
            }
        }

        private void OnRecordingStop(CCSPlayerController player)
        {
            try
            {
                playerTimers[player.Slot].IsRecordingReplay = false;
                SetMoveType(player, MoveType_t.MOVETYPE_WALK);
            }
            catch (Exception ex)
            {
                Utils.LogError($"Error in OnRecordingStop: {ex.Message}");
            }
        }

        public async Task DumpReplayToJson(string steamID, int slot, int bonusX = 0,
            int style = 0, string mode = "")
        {
            await AwaitEnabled(RunEnabled(() =>
            {
                string fileName = $"{steamID}_replay.json";
                string playerReplaysDirectory;
                playerReplaysDirectory = Path.Join(gameDir, "csgo", "cfg", "SharpTimer", "PlayerReplayData",
                    bonusX == 0 ? $"{currentMapName}" : $"{currentMapName}_bonus{bonusX}", GetNamedStyle(style), mode);
                string playerReplaysPath = Path.Join(playerReplaysDirectory, fileName);

                try
                {
                    if (!Directory.Exists(playerReplaysDirectory))
                    {
                        Directory.CreateDirectory(playerReplaysDirectory);
                    }

                    if (playerReplays[slot].replayFrames.Count >= maxReplayFrames) return;

                    var indexedReplayFrames = playerReplays[slot].replayFrames
                        .Select((frame, index) => new IndexedReplayFrames { Index = index, Frame = frame })
                        .ToList();

                    RequireEnabled();
                    using (Stream stream = new FileStream(playerReplaysPath, FileMode.Create))
                    {
                        JsonSerializer.Serialize(stream, indexedReplayFrames);
                    }
                }
                catch (Exception ex)
                {
                    Utils.LogError($"Error during serialization: {ex.Message}");
                }
            }));
        }

        public async Task DumpReplayToBinary(string steamID, int playerSlot, int bonusX = 0,
            int style = 0, string mode = "")
        {
            await AwaitEnabled(RunEnabled(() =>
            {
                string fileName = $"{steamID}_replay.dat";
                string playerReplaysDirectory;
                playerReplaysDirectory = Path.Join(gameDir, "csgo", "cfg", "SharpTimer", "PlayerReplayData",
                    bonusX == 0 ? $"{currentMapName}" : $"{currentMapName}_bonus{bonusX}", GetNamedStyle(style), mode);
                string playerReplaysPath = Path.Join(playerReplaysDirectory, fileName);

                try
                {
                    if (!Directory.Exists(playerReplaysDirectory))
                    {
                        Directory.CreateDirectory(playerReplaysDirectory);
                    }

                    if (playerReplays[playerSlot].replayFrames.Count >= maxReplayFrames) return;

                    var indexedReplayFrames = playerReplays[playerSlot].replayFrames
                        .Select((frame, index) => new IndexedReplayFrames { Index = index, Frame = frame })
                        .ToList();

                    RequireEnabled();
                    using Stream stream = new FileStream(playerReplaysPath, FileMode.Create);
                    BinaryWriter writer = new BinaryWriter(stream);

                    writer.Write(REPLAY_VERSION);

                    foreach (var frame in indexedReplayFrames)
                    {
                        writer.Write(frame.Frame!.Position!.X);
                        writer.Write(frame.Frame.Position!.Y);
                        writer.Write(frame.Frame.Position!.Z);
                        writer.Write(frame.Frame.Rotation!.Pitch);
                        writer.Write(frame.Frame.Rotation!.Yaw);
                        writer.Write(frame.Frame.Rotation!.Roll);
                        writer.Write(frame.Frame.Speed!.X);
                        writer.Write(frame.Frame.Speed!.Y);
                        writer.Write(frame.Frame.Speed!.Z);
                        writer.Write((int)frame.Frame!.Buttons!);
                        writer.Write((int)frame.Frame.Flags);
                        writer.Write((int)frame.Frame.MoveType);
                    }
                }
                catch (Exception ex)
                {
                    Utils.LogError($"Error during serialization: {ex.Message}");
                }
            }));
        }

        public string SerializeFrameToBinaryString(List<IndexedReplayFrames> frames)
        {
            using Stream stream = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(stream);

            writer.Write(REPLAY_VERSION);

            foreach (var frame in frames)
            {
                writer.Write(frame.Frame!.Position!.X);
                writer.Write(frame.Frame.Position!.Y);
                writer.Write(frame.Frame.Position!.Z);
                writer.Write(frame.Frame.Rotation!.Pitch);
                writer.Write(frame.Frame.Rotation!.Yaw);
                writer.Write(frame.Frame.Rotation!.Roll);
                writer.Write(frame.Frame.Speed!.X);
                writer.Write(frame.Frame.Speed!.Y);
                writer.Write(frame.Frame.Speed!.Z);
                writer.Write((int)frame.Frame!.Buttons!);
                writer.Write((int)frame.Frame.Flags);
                writer.Write((int)frame.Frame.MoveType);
            }

            var memoryStream = (MemoryStream)stream;
            return Convert.ToBase64String(memoryStream.ToArray());
        }

        public string GetReplayBinary(CCSPlayerController player, int slot)
        {
            if (!IsAllowedPlayer(player))
            {
                Utils.LogError($"Error in GetReplayJson: Player not allowed or not on server anymore");
                return "";
            }

            try
            {
                if (playerReplays[slot].replayFrames.Count >= maxReplayFrames) return "";

                var indexedReplayFrames = playerReplays[slot].replayFrames
                    .Select((frame, index) => new IndexedReplayFrames { Index = index, Frame = frame })
                    .ToList();

                return SerializeFrameToBinaryString(indexedReplayFrames);
            }
            catch (Exception ex)
            {
                Utils.LogError($"Error during serialization: {ex.Message}");
                return "";
            }
        }

        private async Task ReadReplayFromJson(CCSPlayerController player, string steamId, int slot, int bonusX = 0,
            int style = 0, string mode = "")
        {
            string fileName = $"{steamId}_replay.json";
            string playerReplaysPath;
            playerReplaysPath = Path.Join(gameDir, "csgo", "cfg", "SharpTimer", "PlayerReplayData",
                bonusX == 0 ? currentMapName : $"{currentMapName}_bonus{bonusX}", GetNamedStyle(style), mode, fileName);
            try
            {
                if (File.Exists(playerReplaysPath))
                {
                    var jsonString = await AwaitEnabled(File.ReadAllTextAsync(playerReplaysPath));
                    if (!jsonString.Contains("PositionString"))
                    {
                        var indexedReplayFrames = JsonSerializer.Deserialize<List<IndexedReplayFrames>>(jsonString);

                        if (indexedReplayFrames != null)
                        {
                            var replayFrames = indexedReplayFrames
                                .OrderBy(frame => frame.Index)
                                .Select(frame => frame.Frame)
                                .ToList();

                            if (!playerReplays.TryGetValue(slot, out PlayerReplays? value))
                            {
                                value = new PlayerReplays();
                                playerReplays[slot] = value;
                            }

                            value.replayFrames = replayFrames!;
                        }
                    }
                    else
                    {
                        NextFrameEnabled(() => { Utils.PrintToChat(player, $"Unsupported replay format"); });
                    }
                }
                else
                {
                    Utils.LogError($"File does not exist: {playerReplaysPath}");
                    NextFrameEnabled(() => Utils.PrintToChat(player, Localizer["replay_dont_exist"]));
                }
            }
            catch (Exception ex)
            {
                Utils.LogError($"Error during deserialization: {ex.Message}");
            }
        }

        private async Task ReadReplayFromBinary(CCSPlayerController player, string steamId, int playerSlot,
            int bonusX = 0, int style = 0, string mode = "")
        {
            string fileName = $"{steamId}_replay.dat";
            string playerReplaysPath;
            playerReplaysPath = Path.Join(gameDir, "csgo", "cfg", "SharpTimer", "PlayerReplayData",
                bonusX == 0 ? currentMapName : $"{currentMapName}_bonus{bonusX}", GetNamedStyle(style), mode, fileName);

            try
            {
                if (!File.Exists(playerReplaysPath))
                {
                    Utils.LogError($"File does not exist: {playerReplaysPath}");
                    NextFrameEnabled(() => Utils.PrintToChat(player, Localizer["replay_dont_exist"]));
                    return;
                }

                using Stream stream = new FileStream(playerReplaysPath, FileMode.Open);
                BinaryReader reader = new BinaryReader(stream);

                var version = reader.ReadInt32();
                if (version != REPLAY_VERSION)
                {
                    Utils.LogError($"Unsupported replay version: {version}");
                    NextFrameEnabled(() => Utils.PrintToChat(player, $"Unsupported replay version: {version}"));
                    return;
                }

                var replayFrames = new List<PlayerReplays.ReplayFrames>();
                await AwaitEnabled(NextFrameEnabledAsync(() =>
                {
                    while (reader.BaseStream.Position != reader.BaseStream.Length)
                    {
                        var position = new Vector(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        var rotation = new QAngle(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        var speed = new Vector(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        var buttons = (PlayerButtons)reader.ReadInt32();
                        var flags = (uint)reader.ReadInt32();
                        var moveType = (MoveType_t)reader.ReadInt32();

                        replayFrames.Add(new PlayerReplays.ReplayFrames
                        {
                            Position = ReplayVector.GetVectorish(position),
                            Rotation = ReplayQAngle.GetQAngleish(rotation),
                            Speed = ReplayVector.GetVectorish(speed),
                            Buttons = buttons,
                            Flags = flags,
                            MoveType = moveType
                        });
                    }
                }));

                if (!playerReplays.TryGetValue(playerSlot, out PlayerReplays? value))
                {
                    value = new PlayerReplays();
                    playerReplays[playerSlot] = value;
                }

                value.replayFrames = replayFrames;
            }
            catch (Exception ex)
            {
                Utils.LogError($"Error during deserialization: {ex.Message}");
            }
        }

        private async Task ReadReplayFromGlobal(CCSPlayerController player, int recordId, string mode, int bonusX = 0)
        {
            var payload = new
            {
                record_id = recordId,
                map_id = mapCache.MapID,
                mode,
                bonus = bonusX
            };

            try
            {
                byte[] replayData = Convert.FromBase64String(await AwaitEnabled(GetReplayFromGlobal(payload)));
                using Stream stream = new MemoryStream(replayData);
                using BinaryReader reader = new BinaryReader(stream);

                var version = reader.ReadInt32();
                if (version != REPLAY_VERSION)
                {
                    Utils.LogError($"Unsupported replay version: {version}");
                    NextFrameEnabled(() => Utils.PrintToChat(player, $"Unsupported replay version: {version}"));
                    return;
                }

                var replayFrames = new List<PlayerReplays.ReplayFrames>();
                await AwaitEnabled(NextFrameEnabledAsync(() =>
                {
                    while (reader.BaseStream.Position != reader.BaseStream.Length)
                    {
                        var position = new Vector(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        var rotation = new QAngle(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        var speed = new Vector(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        var buttons = (PlayerButtons)reader.ReadInt32();
                        var flags = (uint)reader.ReadInt32();
                        var moveType = (MoveType_t)reader.ReadInt32();

                        replayFrames.Add(new PlayerReplays.ReplayFrames
                        {
                            Position = ReplayVector.GetVectorish(position),
                            Rotation = ReplayQAngle.GetQAngleish(rotation),
                            Speed = ReplayVector.GetVectorish(speed),
                            Buttons = buttons,
                            Flags = flags,
                            MoveType = moveType
                        });
                    }
                }));

                if (!playerReplays.TryGetValue(player.Slot, out PlayerReplays? value))
                {
                    value = new PlayerReplays();
                    playerReplays[player.Slot] = value;
                }

                value.replayFrames = replayFrames!;
            }
            catch (Exception ex)
            {
                Utils.LogError($"Error during deserialization: {ex.Message}");
            }
        }

        private readonly ReplayBotStartup _replayBotStartup = new();
        private nint _replayBotPawnHandle;

        private async Task SpawnReplayBot()
        {
            CCSPlayerController? replayBot = null;
            bool botRequested = false;
            bool triedTTeam = false;
            int readinessAttempts = 0;
            string botMode = "";
            try
            {
                bool started = await _replayBotStartup.Run(async () =>
                {
                    if (!enableReplays || !enableSRreplayBot) return false;
                    await AwaitEnabled(NextFrameEnabledAsync(() => botMode = GetModeName(defaultMode)));
                    string replayFailure = "";
                    if (!await AwaitEnabled(CheckSRReplay("x", 0, 0, botMode, reason => replayFailure = reason)))
                    {
                        Utils.LogError($"Replay bot: no usable first-place replay for Normal/{botMode} on {currentMapName}. {replayFailure}");
                        return false;
                    }

                    await AwaitEnabled(NextFrameEnabledAsync(() =>
                    {
                        ExecuteEnabledCommand("bot_quota_mode normal");
                        ExecuteEnabledCommand("bot_quota 0");
                        ExecuteEnabledCommand("bot_chatter off");
                        ExecuteEnabledCommand("bot_controllable 0");
                        ExecuteEnabledCommand("bot_kick");
                        replayBotSlot = -1;
                        _replayBotPawnHandle = 0;
                    }));
                    await AwaitEnabled(Task.Delay(3000, RuntimeToken));
                    await AwaitEnabled(NextFrameEnabledAsync(() =>
                    {
                        ExecuteEnabledCommand("bot_quota 1");
                        ExecuteEnabledCommand("bot_add_ct");
                        ExecuteEnabledCommand("bot_quota 1");
                        botRequested = true;
                        replayBot = Utilities.GetPlayers().FirstOrDefault(b => b.IsValid && b.IsBot && !b.IsHLTV);
                        if (replayBot != null) replayBotSlot = replayBot.Slot;
                    }));
                    return true;
                }, async () =>
                {
                    bool ready = false;
                    int botSlot = -1;
                    PlayerTimerInfo? botTimer = null;
                    readinessAttempts++;
                    await AwaitEnabled(NextFrameEnabledAsync(() =>
                    {
                        if (!enableReplays || !enableSRreplayBot)
                            throw new InvalidOperationException("Replay bot was disabled during startup.");
                        replayBot = Utilities.GetPlayers().FirstOrDefault(b => b.IsValid && b.IsBot && !b.IsHLTV);
                        bool hasLivePawn = replayBot != null && replayBot.PlayerPawn() is { IsValid: true } && replayBot.PawnIsAlive;
                        bool onCt = replayBot?.TeamCT() == true;
                        if (!triedTTeam && ReplayBotStartup.ShouldFallbackToT(replayBot != null, onCt, hasLivePawn, readinessAttempts))
                        {
                            ExecuteEnabledCommand("bot_quota 0");
                            if (replayBot != null && replayBot.IsValid)
                            {
                                ExecuteEnabledCommand($"kickid {replayBot.UserId}");
                                replayBotSlot = -1;
                            }
                            triedTTeam = true;
                            Utils.LogDebug("Replay bot: CT has no usable spawn; trying the Terrorist team.");
                            ExecuteEnabledCommand("bot_add_t");
                            ExecuteEnabledCommand("bot_quota 1");
                            replayBot = null;
                            return;
                        }
                        if (replayBot == null) return;
                        replayBotSlot = replayBot.Slot;
                        var pawn = replayBot.PlayerPawn();
                        if (pawn == null || !pawn.IsValid || !replayBot.PawnIsAlive ||
                            pawn.MovementServices == null || (!replayBot.TeamT() && !replayBot.TeamCT())) return;

                        OnPlayerConnect(replayBot, true);
                        botSlot = replayBot.Slot;
                        if (!playerTimers.TryGetValue(botSlot, out botTimer) || botTimer.MovementService == null) return;
                        botTimer.IsTimerBlocked = true;
                        botTimer.IsRecordingReplay = false;
                        botTimer.Mode = botMode;
                        botTimer.changedStyle = false;
                        ChangePlayerName(replayBot, replayBotName);
                        ready = true;
                    }));
                    if (!ready) return false;

                    await AwaitEnabled(ReplayHandler(replayBot!, botSlot, "1", "69", "unknown", 0, 0, false, botMode));
                    await AwaitEnabled(NextFrameEnabledAsync(() =>
                    {
                        if (!replayBot!.IsValid || !playerTimers.TryGetValue(botSlot, out var timer) ||
                            !ReferenceEquals(timer, botTimer) || !timer.IsReplaying ||
                            !playerReplays.TryGetValue(botSlot, out var replay) || replay.replayFrames.Count == 0)
                            throw new InvalidOperationException("The first-place replay did not load for the bot.");
                        Utils.LogDebug($"Replay bot started: {replayBot.PlayerName}, {replay.replayFrames.Count} frames, Normal/{botMode}.");
                    }));
                    return true;
                }, () => AwaitEnabled(Task.Delay(250, RuntimeToken)), RuntimeToken);

                if (!started && botRequested)
                    Utils.LogError("Replay bot: timed out waiting for a living bot pawn and movement services.");
                if (!started && botRequested) await RemoveFailedReplayBot(replayBot);
            }
            catch (OperationCanceledException) when (!IsEnabled) { }
            catch (Exception ex)
            {
                Utils.LogError($"Replay bot startup failed: {ex.Message}");
                if (botRequested) await RemoveFailedReplayBot(replayBot);
            }
        }

        private Task RemoveFailedReplayBot(CCSPlayerController? bot) => NextFrameEnabledAsync(() =>
        {
            ExecuteEnabledCommand("bot_quota 0");
            bot ??= Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && p.IsBot && !p.IsHLTV);
            if (bot == null || !bot.IsValid || !bot.IsBot) return;
            ExecuteEnabledCommand($"kickid {bot.UserId}");
            OnPlayerDisconnect(bot, true);
            _replayBotPawnHandle = 0;
        });

        private void ReplayBotOnTick(CCSPlayerController bot)
        {
            if (!enableReplays || !enableSRreplayBot || !IsAllowedPlayer(bot)) return;
            var pawn = bot.PlayerPawn();
            if (pawn == null || !pawn.IsValid || pawn.MovementServices == null ||
                !playerTimers.TryGetValue(bot.Slot, out var timer) || !timer.IsReplaying ||
                !playerReplays.TryGetValue(bot.Slot, out var replay) || replay.replayFrames.Count == 0) return;

            // A new pawn after death/round restart needs new movement services.
            // Keep playback out of human AFK, weapons, rank and movement logic.
            if (_replayBotPawnHandle != pawn.Handle)
            {
                timer.MovementService = new CCSPlayer_MovementServices(pawn.MovementServices.Handle);
                foreach (var key in specTargets.Where(pair => pair.Value.Slot == bot.Slot).Select(pair => pair.Key).ToArray())
                    specTargets.Remove(key);
                specTargets[pawn.EntityHandle.Index] = bot;
                bot.RemoveWeapons();
                if (pawn.Bot != null)
                {
                    pawn.Bot.IsStopping = true;
                    pawn.Bot.IsSleeping = true;
                    pawn.Bot.AllowActive = true;
                }
                _replayBotPawnHandle = pawn.Handle;
            }
            timer.IsTimerBlocked = true;
            timer.IsRecordingReplay = false;
            ReplayPlay(bot);
        }

        public async Task<bool> CheckSRReplay(string topSteamID = "x", int bonusX = 0, int style = 0, string mode = "",
            Action<string>? reportFailure = null)
        {
            var (srSteamID, srPlayerName, srTime) = ("null", "null", 0);

            if (topSteamID == "x")
            {
                if (enableDb)
                    (srSteamID, srPlayerName, srTime) = await AwaitEnabled(GetMapRecordSteamIDFromDatabase(bonusX, 1, style, mode));
                else
                    (srSteamID, srPlayerName, srTime) = await AwaitEnabled(GetMapRecordSteamID(bonusX, 1));
                if (srSteamID == "null" || srTime <= 0)
                {
                    reportFailure?.Invoke("No first-place record exists for this map/style/mode.");
                    return false;
                }
            }
            
            string ext = useBinaryReplays ? "dat" : "json";
            string fileName = $"{(topSteamID == "x" ? $"{srSteamID}" : $"{topSteamID}")}_replay.{ext}";
            string playerReplaysPath;
            playerReplaysPath = Path.Join(gameDir, "csgo", "cfg", "SharpTimer", "PlayerReplayData",
                (bonusX == 0 ? currentMapName : $"{currentMapName}_bonus{bonusX}"), GetNamedStyle(style), mode,
                fileName);
            try
            {
                if (File.Exists(playerReplaysPath))
                {
                    if (useBinaryReplays)
                    {
                        using var reader = new BinaryReader(File.Open(playerReplaysPath, FileMode.Open));
                        var version = reader.ReadInt32();
                        if (version != REPLAY_VERSION)
                            reportFailure?.Invoke($"Unsupported replay version {version} at {playerReplaysPath}.");
                        return version == REPLAY_VERSION;
                    }

                    var jsonString = await AwaitEnabled(File.ReadAllTextAsync(playerReplaysPath));
                    if (!jsonString.Contains("PositionString"))
                    {
                        var indexedReplayFrames = JsonSerializer.Deserialize<List<IndexedReplayFrames>>(jsonString);

                        if (indexedReplayFrames != null)
                        {
                            return true;
                        }
                    }
                }
                reportFailure?.Invoke($"Replay file missing or unsupported: {playerReplaysPath}");
                return false;
            }
            catch (Exception ex)
            {
                reportFailure?.Invoke($"Cannot read {playerReplaysPath}: {ex.Message}");
                Console.WriteLine($"Error during deserialization: {ex.Message}");
                return false;
            }
        }
    }
}
