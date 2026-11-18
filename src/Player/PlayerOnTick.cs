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
using CounterStrikeSharp.API.Modules.Entities.Constants;
using CounterStrikeSharp.API.Modules.Utils;
using FixVectorLeak;

namespace SharpTimer
{
    public partial class SharpTimer
    {
        public void PlayerOnTick()
        {
            if (!IsEnabled) return;
            try
            {
                int currentTick = Server.TickCount;

                UpdateGameRestartFlag();

                foreach (CCSPlayerController player in connectedPlayers.Values)
                {
                    if (player == null || !player.IsValid) continue;

                    if (player.IsBot)
                    {
                        if (player.Slot == replayBotSlot) ReplayBotOnTick(player);
                        continue;
                    }

                    int slot = player.Slot;
                    string playerName = player.PlayerName;
                    string steamID = player.SteamID.ToString();

                    if ((CsTeam)player.TeamNum == CsTeam.Spectator || !player.PawnIsAlive)
                    {
                        if (currentTick % (64 / hudTickrate) != 0)
                            continue;

                        SpectatorOnTick(player);
                        continue;
                    }

                    if (playerTimers.TryGetValue(slot, out PlayerTimerInfo? playerTimer))
                    {
                        if (playerTimer.IsAddingStartZone || playerTimer.IsAddingEndZone || playerTimer.IsAddingBonusStartZone || playerTimer.IsAddingBonusEndZone)
                        {
                            OnTickZoneTool(player);
                            continue;
                        }

                        if (!IsAllowedPlayer(player))
                        {
                            InvalidateTimer(player);
                            continue;
                        }

                        var playerPawn = player.PlayerPawn?.Value;
                        if (playerPawn == null) continue;

                        PlayerButtons? playerButtons = player.Buttons;
                        Vector_t playerSpeed = playerPawn.AbsVelocity.ToVector_t();
                        bool isTimerBlocked = playerTimer.IsTimerBlocked;

                        /* afk */
                        if (connectedAFKPlayers.ContainsKey(player.Slot))
                        {
                            if (!playerSpeed.IsZero())
                            {
                                connectedAFKPlayers.Remove(player.Slot);
                                playerTimer.AFKWarned = false;
                                playerTimer.AFKTicks = 0;
                            }
                            else continue;
                        }

                        if (playerTimer.AFKTicks >= afkSeconds*48 && !playerTimer.AFKWarned && afkWarning)
                        {
                            Utils.PrintToChat(player, $"{Localizer["afk_message"]}");
                            playerTimer.AFKWarned = true;
                        }
                            
                        if (playerTimer.AFKTicks >= afkSeconds*64)
                            connectedAFKPlayers[player.Slot] = connectedPlayers[player.Slot];

                        if (playerSpeed.IsZero())
                            playerTimer.AFKTicks++;
                        else
                            playerTimer.AFKTicks = 0;
                        /* afk */

                        /* timer counting */
                        bool isTimerRunning = playerTimer.IsTimerRunning;
                        bool isBonusTimerRunning = playerTimer.IsBonusTimerRunning;

                        if (isTimerRunning)
                        {
                            playerTimer.TimerTicks++;

                            if (useStageTriggers)
                                playerTimer.StageTicks++;
                        }
                        else if (isBonusTimerRunning)
                        {
                            playerTimer.BonusTimerTicks++;
                        }
                        /* timer counting */

                        // remove jumping in startzone
                        if (!startzoneJumping && playerTimer.inStartzone)
                        {
                            if((playerButtons & PlayerButtons.Jump) != 0 || playerTimer.MovementService!.LegacyJump.OldJumpPressed)
                                playerPawn.AbsVelocity.Z = 0f;
                        }

                        /* single startzone jump */
                        if (startzoneSingleJumpEnabled && !playerTimer.IsTimerBlocked)
                        {
                            bool wasOnGround = playerTimer.WasOnGroundLastTick;
                            playerTimer.WasOnGroundLastTick = playerPawn.GroundEntity.IsValid;
                            
                            if ((playerTimer.inStartzone || playerTimer.CurrentZoneInfo.InBonusStartZone) && playerTimer.StartZoneJumps >= 1 &&
                                playerPawn.AbsVelocity.IsZero())
                                playerTimer.StartZoneJumps = 0;

                            if ((playerTimer.inStartzone || playerTimer.CurrentZoneInfo.InBonusStartZone) && !wasOnGround && playerPawn.GroundEntity.IsValid)
                                playerTimer.StartZoneJumps++;

                            if (playerTimer.StartZoneJumps == 1 && !wasOnGround)
                            {
                                playerPawn.AbsVelocity.X = 0;
                                playerPawn.AbsVelocity.Y = 0;
                            }
                        }
                        /* single startzone jump */

                        /* hide weapons */
                        bool hasWeapons = playerPawn.WeaponServices?.MyWeapons?.Count > 0;
                        if (playerTimer.HideWeapon)
                        {
                            if (hasWeapons)
                            {
                                player.RemoveWeapons();
                                playerTimer.GivenWeapon = false;
                            }
                        }
                        else
                        {
                            if (giveDefaultWeapons && !hasWeapons && !playerTimer.GivenWeapon)
                            {
                                if (player.TeamT())
                                {
                                    player.RemoveWeapons();
                                    player.GiveNamedItem("weapon_knife_t");
                                    player.GiveNamedItem("weapon_glock");
                                }
                                else if (player.TeamCT())
                                {
                                    player.RemoveWeapons();
                                    player.GiveNamedItem("weapon_knife");
                                    player.GiveNamedItem("weapon_usp_silencer");
                                }

                                playerTimer.GivenWeapon = true;
                            }
                        }
                        /* hide weapons */

                        /* styles */
                        if (playerTimer.currentStyle.Equals(4)) //check if 400vel
                            SetVelocity(player, playerPawn.AbsVelocity.ToVector_t(), 400);

                        if (playerTimer.currentStyle.Equals(10) && !playerPawn.GroundEntity.IsValid && currentTick % 2 != 0) //check if ff
                            IncreaseVelocity(player);

                        if (playerTimer.changedStyle)
                        {
                            _ = RunEnabled(async () => await AwaitEnabled(RankCommandHandler(player, steamID, slot, playerName, true, playerTimer.currentStyle, playerTimer.Mode)));
                            playerTimer.changedStyle = false;
                        }
                        /* styles */
                        
                        /* modes */
                        if (playerTimer.ChangedMode)
                        {
                            _ = RunEnabled(async () => await AwaitEnabled(RankCommandHandler(player, steamID, slot, playerName, true, playerTimer.currentStyle, playerTimer.Mode)));
                            playerTimer.ChangedMode = false;
                        }
                        /* modes */

                        // respawn player if on bhop block too long
                        bool isOnBhopBlock = playerTimer.IsOnBhopBlock;
                        if (isOnBhopBlock)
                        {
                            playerTimer.TicksOnBhopBlock++;

                            if (playerTimer.TicksOnBhopBlock > bhopBlockTime)
                                RespawnPlayer(player);
                        }

                        /* checking if player in zones */
                        if (useTriggers == false && isTimerBlocked == false)
                            CheckPlayerCoords(player, playerSpeed);

                        // idk why there is another one but replay breaks with them merged into one :D
                        if (useTriggers == true && isTimerBlocked == false && useTriggersAndFakeZones)
                            CheckPlayerCoords(player, playerSpeed);
                        /* checking if player in zones */

                        /* hud strafe sync % */
                        if (StrafeHudEnabled)
                            OnSyncTick(player, playerButtons, playerPawn.EyeAngles!);

                        // reset in startzone
                        if (StrafeHudEnabled && playerTimer.inStartzone && playerTimer.Rotation.Count > 0) 
                        { 
                            playerTimer.Sync = 100.00f;
                            playerTimer.Rotation.Clear();
                        }
                        /* hud strafe sync % */

                        if (forcePlayerSpeedEnabled)
                        {
                            string designerName = playerPawn.WeaponServices!.ActiveWeapon?.Value?.DesignerName ?? "no_knife";
                            ForcePlayerSpeed(player, designerName);
                        }

                        /* ranks */
                        if (playerTimer.IsRankPbCached == false)
                        {
                            Utils.LogDebug($"{playerName} has rank and pb null... calling handler");
                            _ = RunEnabled(async () => await AwaitEnabled(RankCommandHandler(player, steamID, slot, playerName, true, playerTimer.currentStyle, playerTimer.Mode)));

                            playerTimer.IsRankPbCached = true;
                        }

                        // attempted bugfix on rank not appearing
                        if (playerTimer.CachedMapPlacement == null && !playerTimer.IsRankPbReallyCached)
                        {
                            Utils.LogDebug($"{playerName} CachedMapPlacement is still null, calling rank handler once more");
                            playerTimer.IsRankPbReallyCached = true;
                            AddEnabledTimer(3.0f, () => { _ = RunEnabled(async () => await AwaitEnabled(RankCommandHandler(player, steamID, slot, playerName, true, playerTimer.currentStyle, playerTimer.Mode))); });
                        }
                        /* ranks */

                        if (playerTimer.IsSpecTargetCached == false || specTargets.ContainsKey(playerPawn.EntityHandle.Index) == false)
                        {
                            specTargets[playerPawn.EntityHandle.Index] = new CCSPlayerController(player.Handle);
                            playerTimer.IsSpecTargetCached = true;
                            Utils.LogDebug($"{playerName} was not in specTargets, adding...");
                        }

                        if (removeCollisionEnabled)
                        {
                            if (playerPawn.Collision.CollisionGroup != (byte)CollisionGroup.COLLISION_GROUP_DISSOLVING ||
                                playerPawn.Collision.CollisionAttribute.CollisionGroup != (byte)CollisionGroup.COLLISION_GROUP_DISSOLVING
                                )
                            {
                                Utils.LogDebug($"{playerName} has wrong collision group... RemovePlayerCollision");
                                RemovePlayerCollision(player);
                            }
                        }

                        if (removeCrouchFatigueEnabled)
                        {
                            if (playerTimer.MovementService != null && playerTimer.MovementService.DuckSpeed != 7.0f)
                                playerTimer.MovementService.DuckSpeed = 7.0f;
                        }

                        // update pre speed for hud
                        if (((PlayerFlags)playerPawn.Flags & PlayerFlags.FL_ONGROUND) != PlayerFlags.FL_ONGROUND)
                        {
                            playerTimer.TicksInAir++;

                            if (playerTimer.TicksInAir == 1)
                                playerTimer.PreSpeed = $"{playerSpeed.X} {playerSpeed.Y} {playerSpeed.Z}";
                        }
                        else playerTimer.TicksInAir = 0;

                        // replays
                        if (enableReplays)
                        {
                            int timerTicks = playerTimer.TimerTicks;
                            if (!playerTimer.IsReplaying && (timerTicks > 0 || playerTimer.BonusTimerTicks > 0) && playerTimer.IsRecordingReplay && !isTimerBlocked)
                                ReplayUpdate(player, timerTicks);

                            if (playerTimer.IsReplaying && !playerTimer.IsRecordingReplay && isTimerBlocked)
                                ReplayPlay(player);

                            else if (
                                !isTimerBlocked &&
                                (playerPawn.MoveType.HasFlag(MoveType_t.MOVETYPE_OBSERVER) || playerPawn.ActualMoveType.HasFlag(MoveType_t.MOVETYPE_OBSERVER)) &&
                                !playerPawn.MoveType.HasFlag(MoveType_t.MOVETYPE_LADDER))
                            {
                                SetMoveType(player, MoveType_t.MOVETYPE_WALK);
                            }
                        }

                        // timer hud content
                        if (currentTick % (64 / hudTickrate) != 0)
                            continue;

                        string hudContent = GetHudContent(playerTimer, player);

                        player.PrintToCenterHtml(string.IsNullOrEmpty(hudContent) ? " " : hudContent);
                        
                        // idk what this is for
                        playerTimer.MovementService!.LegacyJump.OldJumpPressed = false;
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.Message != "Invalid game event") Utils.LogError($"Error in TimerOnTick: {ex.StackTrace}");
            }
        }

        private string GetHudContent(PlayerTimerInfo playerTimer, CCSPlayerController player, bool? showKeys = null)
        {
            var appearance = playerTimer.IsReplaying ? _replayHud : _timerHud;
            PlayerButtons? playerButtons = player.Buttons;
            if (playerTimer.IsReplaying)
                playerButtons = playerReplays.TryGetValue(player.Slot, out var replay) ? replay.LastPlaybackButtons : null;
            Vector_t playerSpeed = player.PlayerPawn!.Value!.AbsVelocity.ToVector_t();
            bool keyEnabled = keysOverlayEnabled && (showKeys ?? !playerTimer.HideKeys);
            bool hudEnabled = !playerTimer.HideTimerHud && hudOverlayEnabled;
            string formattedPlayerVel = Math.Round(use2DSpeed
                ? playerSpeed.Length2D()
                : playerSpeed.Length())
                .ToString("0000");
            string formattedPlayerPre = Math.Round(Utils.ParseVector_t(playerTimer.PreSpeed ?? "0 0 0").Length2D()).ToString("000");
            string time = playerTimer.IsBonusTimerRunning
                ? Utils.FormatTime(playerTimer.BonusTimerTicks)
                : playerTimer.IsReplaying
                    ? Utils.FormatTime(playerReplays[player.Slot].CurrentPlaybackFrame)
                    : Utils.FormatTime(playerTimer.TimerTicks);
            string mapInfo = GetMapDataLine();
            string info = playerTimer.IsReplaying
                ? playerTimer.ReplayHUDString ?? ""
                : playerTimer.CurrentZoneInfo.InBonusStartZone
                    ? playerTimer.CachedBonusInfo.FirstOrDefault(x => x.Key == playerTimer.CurrentZoneInfo.CurrentBonusNumber).Value is { } bonusInfo
                        ? Utils.FormatTime(bonusInfo.PbTicks)
                        : appearance.Texts.NoRecord
                    : playerTimer.CachedPB ?? "";
            string mode = playerTimer.IsReplaying ? "" : playerTimer.Mode;
            string currentStyle = playerTimer.IsReplaying || !enableStyles || playerTimer.currentStyle == 0
                ? ""
                : GetNamedStyle(playerTimer.currentStyle);
            string recordPlacement = playerTimer.IsReplaying ? "" : playerTimer.CachedMapPlacement?.ToString() ?? "";
            if (string.Equals(recordPlacement, UnrankedTitle, StringComparison.OrdinalIgnoreCase) ||
                recordPlacement.Contains("Unranked", StringComparison.OrdinalIgnoreCase))
                recordPlacement = appearance.Texts.NoRecord;
            string bonus = playerTimer.CurrentZoneInfo.InBonusStartZone
                ? playerTimer.CurrentZoneInfo.CurrentBonusNumber.ToString()
                : playerTimer.IsBonusTimerRunning ? playerTimer.BonusStage.ToString() : "";
            int serverRecordBonus = playerTimer.IsBonusTimerRunning
                ? playerTimer.BonusStage
                : playerTimer.CurrentZoneInfo.InBonusStartZone ? playerTimer.CurrentZoneInfo.CurrentBonusNumber : 0;
            var serverRecordData = GetHudServerRecord(serverRecordBonus, playerTimer.currentStyle, playerTimer.Mode);
            string serverRecord = serverRecordData.Holder.Length > 0
                ? $"{serverRecordData.Holder} | {serverRecordData.Time}"
                : "";
            string serverRecordTime = serverRecordData.Time;
            string stage = playerTimer.CurrentMapStage != 0 && useStageTriggers
                ? $"{playerTimer.CurrentMapStage}/{stageTriggerCount}"
                : "";
            string placement = playerTimer.IsTimerRunning ? GetPlayerPlacement(player) : "";
            string progress = playerTimer.IsReplaying && playerReplays.TryGetValue(player.Slot, out var playback)
                ? $"{playback.CurrentPlaybackFrame}/{playback.replayFrames?.Count ?? 0}"
                : "";
            var values = new Dictionary<string, string>
            {
                ["time"] = time,
                ["speed"] = formattedPlayerVel,
                ["preSpeed"] = formattedPlayerPre,
                ["sync"] = playerTimer.Sync.ToString("F2"),
                ["placement"] = placement,
                ["recordPlacement"] = recordPlacement,
                ["stage"] = stage,
                ["info"] = info,
                ["mapInfo"] = mapInfo,
                ["mode"] = mode,
                ["style"] = currentStyle,
                ["bonus"] = bonus,
                ["serverRecord"] = serverRecord,
                ["serverRecordTime"] = serverRecordTime,
                ["progress"] = progress,
                ["left"] = (playerButtons & PlayerButtons.Moveleft) != 0 ? appearance.Texts.Left : appearance.Texts.EmptyKey,
                ["forward"] = (playerButtons & PlayerButtons.Forward) != 0 ? appearance.Texts.Forward : appearance.Texts.EmptyKey,
                ["right"] = (playerButtons & PlayerButtons.Moveright) != 0 ? appearance.Texts.Right : appearance.Texts.EmptyKey,
                ["back"] = (playerButtons & PlayerButtons.Back) != 0 ? appearance.Texts.Back : appearance.Texts.EmptyKey,
                ["jump"] = (playerButtons & PlayerButtons.Jump) != 0 || (!playerTimer.IsReplaying && playerTimer.MovementService!.LegacyJump.OldJumpPressed) ? appearance.Texts.Jump : appearance.Texts.EmptyKey,
                ["duck"] = (playerButtons & PlayerButtons.Duck) != 0 ? appearance.Texts.Duck : appearance.Texts.EmptyKey
            };

            var elements = new Dictionary<string, string>
            {
                ["replay"] = hudEnabled && playerTimer.IsReplaying
                    ? appearance.StyledText("replay", appearance.Text("replay", values), "replay", "replay", appearance.FontClasses.Replay)
                    : "",
                ["timer"] = hudEnabled && (playerTimer.IsTimerRunning || playerTimer.IsBonusTimerRunning)
                    ? appearance.StyledText("timer", appearance.Text(playerTimer.IsBonusTimerRunning ? "bonusTimer" : "timer", values), "timer", "timer", appearance.FontClasses.Value)
                    : "",
                ["speed"] = hudEnabled && VelocityHudEnabled
                    ? appearance.StyledText("speed", appearance.Text("speed", values), "speed", "speed", playerTimer.IsReplaying ? appearance.FontClasses.ReplayValue : appearance.FontClasses.Value)
                    : "",
                ["preSpeed"] = hudEnabled && VelocityHudEnabled
                    ? appearance.StyledText("preSpeed", appearance.Text("preSpeed", values), "preSpeed", "preSpeed", appearance.FontClasses.Info)
                    : "",
                ["sync"] = hudEnabled && StrafeHudEnabled && !playerTimer.IsReplaying
                    ? appearance.StyledText("sync", appearance.Text("sync", values), "sync", "sync", appearance.FontClasses.Value)
                    : "",
                ["placement"] = hudEnabled && placement.Length > 0
                    ? appearance.StyledText("placement", appearance.Text("placement", values), "placement", "placement", appearance.FontClasses.Info)
                    : "",
                ["recordPlacement"] = hudEnabled && recordPlacement.Length > 0
                    ? appearance.StyledText("recordPlacement", appearance.Text("recordPlacement", values), "recordPlacement", "recordPlacement", appearance.FontClasses.Info)
                    : "",
                ["stage"] = hudEnabled && stage.Length > 0
                    ? appearance.StyledText("stage", appearance.Text("stage", values), "stage", "stage", appearance.FontClasses.Info)
                    : "",
                ["info"] = hudEnabled && info.Length > 0
                    ? appearance.StyledText("info", appearance.Text("info", values), "info", "info", appearance.FontClasses.Info)
                    : "",
                ["mapInfo"] = hudEnabled && mapInfo.Length > 0
                    ? appearance.StyledText("mapInfo", appearance.Text("mapInfo", values), "mapInfo", "mapInfo", appearance.FontClasses.Info)
                    : "",
                ["mode"] = hudEnabled && mode.Length > 0
                    ? appearance.StyledText("mode", appearance.Text("mode", values), "mode", "mode", appearance.FontClasses.Info)
                    : "",
                ["style"] = hudEnabled && currentStyle.Length > 0
                    ? appearance.StyledText("style", appearance.Text("style", values), "style", "style", appearance.FontClasses.Info)
                    : "",
                ["bonus"] = hudEnabled && bonus.Length > 0
                    ? appearance.StyledText("bonus", appearance.Text("bonus", values), "bonus", "bonus", appearance.FontClasses.Info)
                    : "",
                ["serverRecord"] = hudEnabled && serverRecord.Length > 0
                    ? appearance.StyledText("serverRecord", appearance.Text("serverRecord", values), "serverRecord", "serverRecord", appearance.FontClasses.Info)
                    : "",
                ["serverRecordTime"] = hudEnabled && serverRecordTime.Length > 0
                    ? appearance.StyledText("serverRecordTime", appearance.Text("serverRecordTime", values), "serverRecordTime", "serverRecordTime", appearance.FontClasses.Info)
                    : "",
                ["keys"] = keyEnabled
                    ? appearance.StyledText("keys", appearance.Text("keys", values), "keys", "Keys", appearance.FontClasses.Keys)
                    : "",
                ["progress"] = hudEnabled && progress.Length > 0
                    ? appearance.StyledText("progress", appearance.Text("progress", values), "progress", "progress", appearance.FontClasses.Info)
                    : ""
            };

            return appearance.Render(elements);
        }

        private string GetMapDataLine()
        {
            string mapInfo = "";
    
            if (MapTierHudEnabled && currentMapTier != null)
                mapInfo += $"Tier: {currentMapTier}";
    
            if (MapTypeHudEnabled && currentMapType != null)
                mapInfo += (string.IsNullOrEmpty(mapInfo) ? "" : " | ") + currentMapType;
    
            if (MapNameHudEnabled && currentMapType == null && currentMapTier == null)
                mapInfo += (string.IsNullOrEmpty(mapInfo) ? "" : " | ") + currentMapName;
    
            return mapInfo;
        }

        public void SpectatorOnTick(CCSPlayerController player)
        {
            if (!IsAllowedSpectator(player))
                return;

            try
            {
                var target = specTargets[player.Pawn.Value!.ObserverServices!.ObserverTarget.Index];
                if (playerTimers.TryGetValue(target.Slot, out PlayerTimerInfo? playerTimer) && IsAllowedPlayer(target))
                {
                    bool showKeys = playerTimers.TryGetValue(player.Slot, out var spectatorTimer) && !spectatorTimer.HideKeys;
                    string hudContent = GetHudContent(playerTimer, target, showKeys);

                    player.PrintToCenterHtml(string.IsNullOrEmpty(hudContent) ? " " : hudContent);
                }
            }
            catch (Exception ex)
            {
                if (ex.Message != "Invalid game event") Utils.LogError($"Error in SpectatorOnTick: {ex.Message}");
            }
        }
    }
}
