using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;

namespace SharpTimer;

internal static class ZoneToolMenu
{
    public static ChatMenu Create(Action<CCSPlayerController> start, Action<CCSPlayerController> end,
        Action<CCSPlayerController> respawn, Action<CCSPlayerController> save)
    {
        var menu = new ChatMenu("[ZONE TOOL]")
        {
            PostSelectAction = PostSelectAction.Nothing,
            ExitButton = true
        };
        menu.AddMenuOption("startzone", (player, _) => start(player));
        menu.AddMenuOption("endzone", (player, _) => end(player));
        menu.AddMenuOption("respawnpos", (player, _) => respawn(player));
        menu.AddMenuOption("save", (player, _) => save(player));
        return menu;
    }
}
