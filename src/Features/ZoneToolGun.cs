using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using FixVectorLeak;
using System.Numerics;

namespace SharpTimer;

public partial class SharpTimer
{
    [ConsoleCommand("css_s", "Zone tool: select shooting input")]
    [RequiresPermissions("@css/cheats")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void ZoneToolShootMode(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsEnabled) return;
        SelectZoneToolMode(player, ZoneToolSelectionMode.Shoot);
    }

    [ConsoleCommand("css_d", "Zone tool: select default input at your feet")]
    [RequiresPermissions("@css/cheats")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void ZoneToolFeetMode(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsEnabled) return;
        SelectZoneToolMode(player, ZoneToolSelectionMode.Feet);
    }

    [ConsoleCommand("css_c", "Cancel the current zone drawing")]
    [RequiresPermissions("@css/cheats")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void ZoneToolCancel(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsEnabled || !IsAllowedPlayer(player) || !AdminManager.PlayerHasPermissions(player!, "@css/cheats")) return;
        var timer = playerTimers[player!.Slot];
        bool hadPendingRegion = timer.PendingZoneToolRegion != null;
        CancelZoneToolRegion(timer);
        Utils.PrintToChat(player, hadPendingRegion
            ? "[区域工具] 已取消当前区域，原有点位已恢复；可继续选择 !1 / !2。"
            : "[区域工具] 当前没有待取消的区域；已完成点位仍保留。");
        OpenFakeZonesMenu(player);
    }

    [ConsoleCommand("css_9", "Clear unsaved zone points and exit the zone tool")]
    [RequiresPermissions("@css/cheats")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void ZoneToolCloseMenu(CCSPlayerController? player, CommandInfo command)
    {
        if (!IsEnabled || !IsAllowedPlayer(player) || !AdminManager.PlayerHasPermissions(player!, "@css/cheats")) return;
        ClearAllZoneToolPoints(playerTimers[player!.Slot]);
        Utils.PrintToChat(player, "[区域工具] 已清除本次未保存的所有点位并退出；地图文件未修改。");
        MenuManager.CloseActiveMenu(player!);
    }

    private static void ClearAllZoneToolPoints(PlayerTimerInfo timer)
    {
        timer.StartZoneC1 = timer.StartZoneC2 = null;
        timer.EndZoneC1 = timer.EndZoneC2 = null;
        timer.BonusStartZoneC1 = timer.BonusStartZoneC2 = null;
        timer.BonusEndZoneC1 = timer.BonusEndZoneC2 = null;
        timer.RespawnPos = timer.BonusRespawnPos = null;
        FinishZoneToolRegion(timer);
        timer.ZoneToolGun.Reset();
        ClearZoneToolPreview(timer);
    }

    private void SelectZoneToolMode(CCSPlayerController? player, ZoneToolSelectionMode mode)
    {
        if (!IsAllowedPlayer(player) || !AdminManager.PlayerHasPermissions(player!, "@css/cheats")) return;
        var timer = playerTimers[player!.Slot];
        timer.ZoneToolGun.SelectMode(mode);
        ClearZoneToolPreview(timer);
        Utils.PrintToChat(player, mode == ZoneToolSelectionMode.Shoot
            ? "[区域工具] 已切换开枪选角点：起点、终点沿用该模式直到保存；重生点始终使用脚下。先开枪定位，再用原菜单确认；!d 切回脚下。"
            : "[区域工具] 已切换脚下选角点：起点、终点沿用该模式直到保存；重生点始终使用脚下。用原菜单确认；!s 切换开枪。");
        OpenFakeZonesMenu(player);
    }

    private static void BeginZoneToolInput(PlayerTimerInfo timer)
    {
        CancelZoneToolRegion(timer);
        FinishZoneToolRegion(timer);
        timer.ZoneToolWire ??= new();
    }

    private static void BeginZoneToolRegion(PlayerTimerInfo timer, ZoneToolRegionKind region,
        string? previousFirstCorner, string? previousSecondCorner)
    {
        BeginZoneToolInput(timer);
        timer.PendingZoneToolRegion = new(region, previousFirstCorner, previousSecondCorner);
    }

    private static void ClearZoneToolPreview(PlayerTimerInfo timer)
    {
        if (timer.ZoneToolWire == null) return;
        foreach (var wire in timer.ZoneToolWire.Values)
            if (wire.IsValid) wire.Remove();
        timer.ZoneToolWire.Clear();
    }

    private static void ResetZoneToolInput(PlayerTimerInfo timer)
    {
        CancelZoneToolRegion(timer);
        timer.ZoneToolGun.Reset();
        FinishZoneToolRegion(timer);
    }

    private static void CancelZoneToolRegion(PlayerTimerInfo timer)
    {
        if (timer.PendingZoneToolRegion is { } pending &&
            pending.TryRestorePrevious(out var previousFirst, out var previousSecond))
        {
            switch (pending.Region)
            {
                case ZoneToolRegionKind.Start:
                    timer.StartZoneC1 = previousFirst;
                    timer.StartZoneC2 = previousSecond;
                    break;
                case ZoneToolRegionKind.End:
                    timer.EndZoneC1 = previousFirst;
                    timer.EndZoneC2 = previousSecond;
                    break;
                case ZoneToolRegionKind.BonusStart:
                    timer.BonusStartZoneC1 = previousFirst;
                    timer.BonusStartZoneC2 = previousSecond;
                    break;
                case ZoneToolRegionKind.BonusEnd:
                    timer.BonusEndZoneC1 = previousFirst;
                    timer.BonusEndZoneC2 = previousSecond;
                    break;
            }
        }

        FinishZoneToolRegion(timer);
        timer.ZoneToolGun.ClearCandidate();
        ClearZoneToolPreview(timer);
    }

    // Completing one region keeps the selected method for the rest of the map setup.
    private static void FinishZoneToolRegion(PlayerTimerInfo timer)
    {
        timer.PendingZoneToolRegion?.Complete();
        timer.PendingZoneToolRegion = null;
        timer.IsAddingStartZone = timer.IsAddingEndZone = false;
        timer.IsAddingBonusStartZone = timer.IsAddingBonusEndZone = false;
    }

    private bool TryGetZoneToolPoint(CCSPlayerController player, out string position)
    {
        position = "";
        var input = playerTimers[player.Slot].ZoneToolGun;
        var feet = player.Pawn.Value?.CBodyComponent?.SceneNode?.AbsOrigin;
        if (feet == null || !input.TryConfirm(new Vector3(feet.X, feet.Y, feet.Z), out var point))
        {
            Utils.PrintToChat(player, "[区域工具] 请先开枪选择该点，再按原菜单确认。开枪不会自动进入下一点。");
            return false;
        }
        position = FormattableString.Invariant($"{point.X} {point.Y} {point.Z}");
        return true;
    }

    private bool TryGetZoneToolFeetPoint(CCSPlayerController player, out string position)
    {
        position = "";
        var feet = player.Pawn.Value?.CBodyComponent?.SceneNode?.AbsOrigin;
        if (feet == null || !playerTimers[player.Slot].ZoneToolGun.TryConfirmFeet(
                new Vector3(feet.X, feet.Y, feet.Z), out var point))
        {
            Utils.PrintToChat(player, "[区域工具] 无法读取脚下位置，请重试。");
            return false;
        }
        position = FormattableString.Invariant($"{point.X} {point.Y} {point.Z}");
        return true;
    }

    private HookResult OnZoneToolBulletImpact(EventBulletImpact @event, GameEventInfo info)
    {
        if (!IsEnabled) return HookResult.Continue;
        var player = @event.Userid;
        if (player == null || !player.IsValid || !playerTimers.TryGetValue(player.Slot, out var timer) ||
            timer.ZoneToolGun.Mode != ZoneToolSelectionMode.Shoot || !IsAllowedPlayer(player) ||
            !AdminManager.PlayerHasPermissions(player, "@css/cheats")) return HookResult.Continue;
        if (!timer.ZoneToolGun.Capture(new Vector3(@event.X, @event.Y, @event.Z), Server.TickCount))
            return HookResult.Continue;
        Utils.PrintToChat(player, "[区域工具] 候选位置已更新，请按原菜单确认该点；再次开枪可重新选择。");
        return HookResult.Continue;
    }
}
