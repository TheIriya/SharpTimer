using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace SharpTimer;

public partial class SharpTimer
{
    private HudAppearance _timerHud = new();
    private HudAppearance _replayHud = new();
    private readonly Dictionary<string, (string Value, DateTime ExpiresAt)> _hudServerRecordCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _hudServerRecordPending = new(StringComparer.Ordinal);

    private string GetHudServerRecord(int bonus, int style, string mode)
    {
        string map = currentMapName ?? "";
        string key = $"{map}|{bonus}|{style}|{mode}";
        if (_hudServerRecordCache.TryGetValue(key, out var cached))
        {
            if (cached.ExpiresAt > DateTime.UtcNow) return cached.Value;
            _hudServerRecordCache.Remove(key);
        }
        if (!_hudServerRecordPending.Add(key)) return "";

        _ = RunEnabled(async () =>
        {
            try
            {
                var result = enableDb
                    ? await AwaitEnabled(GetMapRecordSteamIDFromDatabase(bonus, 0, style, mode))
                    : await AwaitEnabled(GetMapRecordSteamID(bonus));
                string formatted = result.Item3 > 0 && result.Item2 != "null"
                    ? $"{result.Item2} | {Utils.FormatTime(result.Item3)}"
                    : "";
                NextFrameEnabled(() =>
                {
                    _hudServerRecordPending.Remove(key);
                    if (currentMapName == map) _hudServerRecordCache[key] = (formatted, DateTime.UtcNow.AddSeconds(30));
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

        return "";
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
