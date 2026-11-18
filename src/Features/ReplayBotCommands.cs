using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;

namespace SharpTimer;

public partial class SharpTimer
{
    [ConsoleCommand("sharptimer_replay_bot_restart", "Reload the default-mode first-place replay bot")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void RestartReplayBotCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null) return;
        if (!IsEnabled || !enableReplays || !enableSRreplayBot)
        {
            command.ReplyToCommand("Enable SharpTimer, sharptimer_replays_enabled and sharptimer_replay_bot_enabled first.");
            return;
        }
        command.ReplyToCommand("Reloading the first-place replay bot. Check the server console for startup results.");
        _ = RunEnabled(SpawnReplayBot);
    }
}
