using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace SharpTimer;

public partial class SharpTimer
{
    private HudAppearance _timerHud = new();
    private HudAppearance _replayHud = new();
    private readonly Dictionary<string, (string Holder, string Time, int TotalPlayers)> _hudServerRecordCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hudServerRecordPending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _hudServerRecordGenerations = new(StringComparer.Ordinal);

    private (string Holder, string Time, int TotalPlayers) GetHudServerRecord(int bonus, int style, string mode)
    {
        string map = currentMapName ?? "";
        string key = $"{map}|{bonus}|{style}|{mode}";
        if (_hudServerRecordCache.TryGetValue(key, out var cached)) return cached;
        if (!_hudServerRecordPending.Add(key)) return ("", "", 0);
        int generation = _hudServerRecordGenerations.GetValueOrDefault(key);

        _ = RunEnabled(async () =>
        {
            try
            {
                var result = enableDb
                    ? await AwaitEnabled(GetMapRecordSteamIDFromDatabase(bonus, 0, style, mode))
                    : await AwaitEnabled(GetMapRecordSteamID(bonus));
                int totalPlayers = enableDb
                    ? (await AwaitEnabled(GetSortedRecordsFromDatabase(0, bonus, "", style, mode))).Count
                    : (await AwaitEnabled(Utils.GetSortedRecords(bonus))).Count;
                string holder = result.Item3 > 0 && result.Item2 != "null" ? result.Item2 : "";
                string time = result.Item3 > 0 && result.Item2 != "null" ? Utils.FormatTime(result.Item3) : "";
                NextFrameEnabled(() =>
                {
                    _hudServerRecordPending.Remove(key);
                    if (currentMapName == map && _hudServerRecordGenerations.GetValueOrDefault(key) == generation)
                        _hudServerRecordCache[key] = (holder, time, totalPlayers);
                });
            }
            catch (Exception ex)
            {
                NextFrameEnabled(() =>
                {
                    _hudServerRecordPending.Remove(key);
                    Utils.LogError($"Failed to load HUD server record for {map}: {ex.Message}");
                });
            }
        });

        return ("", "", 0);
    }

    private void RefreshHudRecordsAfterFinish(int bonus, int style, string mode)
    {
        string map = currentMapName ?? "";
        string key = $"{map}|{bonus}|{style}|{mode}";
        _hudServerRecordGenerations[key] = _hudServerRecordGenerations.GetValueOrDefault(key) + 1;
        _hudServerRecordCache.Remove(key);

        var players = connectedPlayers.Values
            .Where(player => player.IsValid && !player.IsBot && playerTimers.TryGetValue(player.Slot, out var timer) &&
                timer.Mode == mode && timer.currentStyle == style)
            .Select(player =>
            {
                var timer = playerTimers[player.Slot];
                return (Player: player, Slot: player.Slot, SteamId: player.SteamID.ToString(), Name: player.PlayerName, Timer: timer);
            })
            .ToArray();

        _ = RunEnabled(async () =>
        {
            foreach (var target in players)
            {
                int pbTicks = enableDb
                    ? await AwaitEnabled(GetPreviousPlayerRecordFromDatabase(target.SteamId, map, target.Name, bonus, style, mode))
                    : await AwaitEnabled(GetPreviousPlayerRecord(target.SteamId, bonus));
                string placement = await AwaitEnabled(GetPlayerMapPlacementWithTotal(
                    target.SteamId, target.Name, false, true, bonus, style, false, mode));
                NextFrameEnabled(() =>
                {
                    if (!IsPlayerOrSpectator(target.Player) ||
                        !playerTimers.TryGetValue(target.Slot, out var current) || !ReferenceEquals(current, target.Timer) ||
                        current.Mode != mode || current.currentStyle != style) return;
                    if (bonus == 0)
                    {
                        current.CachedPB = pbTicks > 0 ? $" {Utils.FormatTime(pbTicks)}" : "";
                        current.CachedMapPlacement = placement;
                    }
                    else if (pbTicks > 0)
                    {
                        current.CachedBonusInfo[bonus] = new PlayerBonusPlacementInfo
                        {
                            PbTicks = pbTicks,
                            Placement = placement
                        };
                    }
                    else
                        current.CachedBonusInfo.Remove(bonus);
                });
            }
        });
    }

    private void LoadHudConfigurations()
    {
        _timerHud = LoadHudConfiguration("hud.json", _timerHud);
        _replayHud = LoadHudConfiguration("replay_hud.json", _replayHud);
    }

    private HudAppearance LoadHudConfiguration(string fileName, HudAppearance previous)
    {
        string path = Path.Join(gameDir, "csgo", "cfg", "SharpTimer", fileName);
        try
        {
            HudAppearance config = HudAppearance.Load(path);
            if (!config.HasLayoutToken("keys"))
                Utils.LogError($"{path} does not include {{keys}} in Layout; movement keys will be hidden until that placeholder is added.");
            Utils.LogDebug($"Loaded HUD configuration from {path}");
            return config;
        }
        catch (Exception ex)
        {
            Utils.LogError($"Cannot load {path}: {ex.Message} Keeping the previous HUD configuration (built-in defaults on first load).");
            return previous;
        }
    }

    [ConsoleCommand("sharptimer_hud_reload", "Reload normal and replay HUD appearance files.")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void SharpTimerHudReload(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null) return;
        LoadHudConfigurations();
        command.ReplyToCommand("HUD reload completed. Check the server log for configuration errors.");
    }
}
