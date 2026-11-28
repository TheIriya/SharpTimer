using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using System.Drawing;
using TagsApi;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace SharpTimer;

public partial class SharpTimer
{
    private readonly RuntimeGate _runtime = new();
    private bool _runtimeReady;
    private bool _mapRuntimeStarted;
    private bool _damageHookRegistered;
    private readonly Dictionary<string, string> _serverSettings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, PlayerSettings> _playerSettings = new();
    private readonly Dictionary<nint, Action> _weaponSettings = new();
    private bool _recordingDemo;
    internal readonly List<CBeam> ZoneBeams = new();

    // Work from an earlier enable period must stay inactive after re-enabling.
    public bool IsEnabled => _runtime.IsActive;
    internal CancellationToken RuntimeToken => _runtime.CurrentToken;

    [ConsoleCommand("sharptimer_enabled", "Enable/disable all SharpTimer features (0/1 or false/true). Default: 1")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void SharpTimerEnabled(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null) return;
        string value = command.ArgString.Trim();
        if (value.Length == 0)
        {
            command.ReplyToCommand($"sharptimer_enabled = {(_runtime.Enabled ? 1 : 0)}");
            return;
        }

        bool enabled;
        if (value == "1") enabled = true;
        else if (value == "0") enabled = false;
        else if (!bool.TryParse(value, out enabled))
        {
            command.ReplyToCommand("Usage: sharptimer_enabled <0|1|false|true>");
            return;
        }

        SetEnabled(enabled);
        command.ReplyToCommand($"SharpTimer {(_runtime.Enabled ? "enabled" : "disabled")}");
    }

    private void SetEnabled(bool enabled)
    {
        if (!_runtime.SetEnabled(enabled)) return;
        if (!_runtimeReady) return;

        if (!enabled)
        {
            StopRuntime();
            return;
        }

        StartRuntime();
    }

    private void StartRuntime()
    {
        if (!IsEnabled) return;
        UpdateDamageHook();
        foreach (string name in new[] { "sv_accelerate", "sv_airaccelerate", "sv_air_max_wishspeed", "sv_friction" })
            CaptureServerSetting(name);
        OnMapStartHandler(Server.MapName);
        NextFrameEnabled(() =>
        {
            ClearMapData();
            LoadMapData(Server.MapName);
            foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && !p.IsHLTV))
            {
                if (!connectedPlayers.ContainsKey(player.Slot)) OnPlayerConnect(player);
                if (playerTimers.ContainsKey(player.Slot)) SetPlayerMode(player, defaultMode);
            }
        });
    }

    private void StopRuntime()
    {
        if (_damageHookRegistered)
        {
            RemoveListener<Listeners.OnPlayerTakeDamagePre>(OnPlayerTakeDamagePre);
            _damageHookRegistered = false;
        }
        foreach (var timer in Timers.ToArray()) timer.Kill();
        Timers.Clear();
        foreach (var timer in playerTimers.Values)
            if (timer.ZoneToolWire != null)
                foreach (var beam in timer.ZoneToolWire.Values)
                    if (beam.IsValid) beam.Remove();
        foreach (var beam in ZoneBeams)
            if (beam.IsValid) beam.Remove();
        ZoneBeams.Clear();

        foreach (var settings in _playerSettings.Values)
        {
            try { settings.Restore(this); }
            catch (Exception ex) { Utils.LogError($"Error restoring player settings: {ex.Message}"); }
        }
        _playerSettings.Clear();
        foreach (var restore in _weaponSettings.Values) restore();
        _weaponSettings.Clear();
        if (_recordingDemo)
        {
            Server.ExecuteCommand("tv_stoprecord");
            _recordingDemo = false;
        }
        var bot = Utilities.GetPlayers().FirstOrDefault(p => p.IsValid && p.IsBot && p.Slot == replayBotSlot);
        if (bot != null)
            Server.ExecuteCommand($"kickid {bot.UserId}");

        foreach (var (name, value) in _serverSettings)
        {
            var convar = ConVar.Find(name);
            if (convar != null) convar.StringValue = value;
        }
        _serverSettings.Clear();
        ClearPlayerState();
        ClearMapData();
        _mapRuntimeStarted = false;
        sqlCheck = false;
        isADServerRecordTimerRunning = false;
        isADMessagesTimerRunning = false;
        isRankHUDTimerRunning = false;
    }

    private void OnMapEndHandler()
    {
        _runtime.CancelMapWork();
        StopRuntime();
    }

    private void UpdateDamageHook()
    {
        if (!_runtimeReady) return;
        bool shouldHook = IsEnabled && (disableDamage || (enableReplays && enableSRreplayBot));
        if (shouldHook == _damageHookRegistered) return;
        if (shouldHook) RegisterListener<Listeners.OnPlayerTakeDamagePre>(OnPlayerTakeDamagePre);
        else RemoveListener<Listeners.OnPlayerTakeDamagePre>(OnPlayerTakeDamagePre);
        _damageHookRegistered = shouldHook;
    }

    private void CaptureServerSetting(string name)
    {
        if (_serverSettings.ContainsKey(name)) return;
        var convar = ConVar.Find(name);
        if (convar != null) _serverSettings[name] = convar.StringValue;
    }

    internal void ExecuteEnabledCommand(string command)
    {
        if (!IsEnabled) return;
        CaptureCommandSettings(command, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (command.StartsWith("tv_record ", StringComparison.OrdinalIgnoreCase)) _recordingDemo = true;
        if (command.Equals("tv_stoprecord", StringComparison.OrdinalIgnoreCase)) _recordingDemo = false;
        Server.ExecuteCommand(command);
    }

    // Include settings in custom/map configs so disabling restores their previous values too.
    private void CaptureCommandSettings(string command, HashSet<string> visited)
    {
        foreach (string statement in command.Split(';'))
        {
            string[] parts = statement.Split("//", 2)[0].Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            if (parts[0] is "exec" or "execifexists")
            {
                string path = Path.GetFullPath(Path.Join(gameDir, "csgo", "cfg", parts[1].Trim().Trim('"')));
                if (!Path.HasExtension(path)) path += ".cfg";
                if (visited.Add(path) && File.Exists(path))
                    foreach (string line in File.ReadLines(path)) CaptureCommandSettings(line, visited);
            }
            else CaptureServerSetting(parts[0]);
        }
    }

    internal void NextFrameEnabled(Action callback)
    {
        var token = RuntimeToken;
        if (!IsEnabled) return;
        Server.NextFrame(() => { _runtime.Invoke(token, callback); });
    }

    internal Task NextFrameEnabledAsync(Action callback)
    {
        var token = RuntimeToken;
        if (!IsEnabled) return Task.FromCanceled(new CancellationToken(true));
        return Server.NextFrameAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            _runtime.Invoke(token, callback);
        });
    }

    private void NextWorldUpdateEnabled(Action callback)
    {
        var token = RuntimeToken;
        if (!IsEnabled) return;
        Server.NextWorldUpdate(() => { _runtime.Invoke(token, callback); });
    }

    private Timer AddEnabledTimer(float interval, Action callback, TimerFlags? flags = null)
    {
        var token = RuntimeToken;
        return AddTimer(interval, () => { _runtime.Invoke(token, callback); }, flags);
    }

    internal Task RunEnabled(Action callback) => _runtime.Run(callback);
    internal Task RunEnabled(Func<Task> callback) => _runtime.Run(callback);
    internal Task<T?> RunEnabled<T>(Func<T?> callback) => _runtime.Run(callback);
    internal Task AwaitEnabled(Task task) => _runtime.Await(task);
    internal Task<T> AwaitEnabled<T>(Task<T> task) => _runtime.Await(task);

    private void RequireEnabled()
    {
        if (!IsEnabled) throw new OperationCanceledException("SharpTimer is disabled.");
    }

    private void CapturePlayerSettings(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn == null || !pawn.IsValid) return;
        if (!_playerSettings.TryGetValue(player.Slot, out var settings) || settings.Pawn.Handle != pawn.Handle)
            _playerSettings[player.Slot] = new PlayerSettings(player, pawn, settings);
    }

    private void CaptureWeaponSettings(CBasePlayerWeapon weapon)
    {
        if (_weaponSettings.ContainsKey(weapon.Handle)) return;
        int clip = weapon.Clip1;
        int reserve = weapon.ReserveAmmo[0];
        _weaponSettings[weapon.Handle] = () =>
        {
            if (!weapon.IsValid) return;
            weapon.Clip1 = clip;
            weapon.ReserveAmmo[0] = reserve;
        };
    }

    private sealed class PlayerSettings(CCSPlayerController player, CCSPlayerPawn pawn, PlayerSettings? previous)
    {
        public CCSPlayerPawn Pawn { get; } = pawn;
        private readonly Color _render = pawn.Render;
        private readonly byte _collision = pawn.Collision.CollisionGroup;
        private readonly byte _collisionAttribute = pawn.Collision.CollisionAttribute.CollisionGroup;
        private readonly float _gravity = Schema.GetSchemaValue<float>(pawn.Handle, "CBaseEntity", "m_flActualGravityScale");
        private readonly float _velocity = pawn.VelocityModifier;
        private readonly float? _duckSpeed = pawn.MovementServices?.As<CCSPlayer_MovementServices>().DuckSpeed;
        private readonly MoveType_t _moveType = pawn.MoveType;
        private readonly MoveType_t _actualMoveType = pawn.ActualMoveType;
        private readonly uint _fov = previous?._fov ?? player.DesiredFOV;
        public bool TagsCaptured = previous?.TagsCaptured ?? false;
        public string? ChatTag = previous?.ChatTag;
        public string? ScoreTag = previous?.ScoreTag;

        public void Restore(SharpTimer plugin)
        {
            if (!player.IsValid) return;
            player.PrintToCenterHtml(" ");
            player.DesiredFOV = _fov;
            Utilities.SetStateChanged(player, "CBasePlayerController", "m_iDesiredFOV");
            if (TagsCaptured && plugin.TagApi != null)
            {
                plugin.TagApi.ResetAttribute(player, Tags.TagType.ChatTag);
                plugin.TagApi.ResetAttribute(player, Tags.TagType.ScoreTag);
                if (ChatTag != null) plugin.TagApi.SetAttribute(player, Tags.TagType.ChatTag, ChatTag);
                if (ScoreTag != null) plugin.TagApi.SetAttribute(player, Tags.TagType.ScoreTag, ScoreTag);
            }
            foreach (string name in new[] { "sv_accelerate", "sv_airaccelerate", "sv_air_max_wishspeed", "sv_friction" })
                if (plugin._serverSettings.TryGetValue(name, out string? value)) player.ReplicateConVar(name, value);
            if (!Pawn.IsValid || player.PlayerPawn.Value?.Handle != Pawn.Handle) return;
            if (plugin.playerTimers.TryGetValue(player.Slot, out var timer) && timer.IsReplaying)
                plugin.ResetReplayPosture(player);
            Pawn.Render = _render;
            Pawn.Collision.CollisionGroup = _collision;
            Pawn.Collision.CollisionAttribute.CollisionGroup = _collisionAttribute;
            Pawn.VelocityModifier = _velocity;
            Pawn.MoveType = _moveType;
            Pawn.ActualMoveType = _actualMoveType;
            Schema.SetSchemaValue(Pawn.Handle, "CBaseEntity", "m_flActualGravityScale", _gravity);
            if (_duckSpeed.HasValue && Pawn.MovementServices != null)
                Pawn.MovementServices.As<CCSPlayer_MovementServices>().DuckSpeed = _duckSpeed.Value;
            Utilities.SetStateChanged(Pawn, "CBaseModelEntity", "m_clrRender");
            Utilities.SetStateChanged(Pawn, "CBaseEntity", "m_Collision");
            Utilities.SetStateChanged(Pawn, "CBaseEntity", "m_MoveType");
        }
    }
}
