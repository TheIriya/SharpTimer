using System.Numerics;
using CounterStrikeSharp.API.Modules.Menu;
using SharpTimer;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}

var feet = new Vector3(1, 2, 3);
var hit = new Vector3(12.25f, -30.5f, 64.125f);
var input = new ZoneToolShotInput();
Check(input.Mode == ZoneToolSelectionMode.Feet && input.TryConfirm(feet, out var defaultPoint) && defaultPoint == feet,
    "The first point uses feet by default without a mode command");
input.Arm(10);
Check(!input.Capture(hit, 10), "Shooting does not change the default feet method");
input.SelectMode(ZoneToolSelectionMode.Feet);
input.Arm(20);
Check(!input.Capture(hit, 20), "Gunfire does not affect feet mode");
Check(input.TryConfirm(feet, out var first) && first == feet, "First confirmation uses current feet");
Check(input.TryConfirm(-feet, out var second) && second == -feet, "Next confirmation uses new feet");

input.SelectMode(ZoneToolSelectionMode.Shoot);
Check(!input.TryConfirm(feet, out _), "Shooting mode requires a shot before confirmation");
Check(!input.Capture(hit, 30), "An impact without a trigger pull is ignored");
input.Arm(30);
Check(input.Capture(hit, 30) && input.Candidate == hit, "A shot selects a candidate before drawing starts");
Check(!input.Capture(-hit, 30) && !input.Capture(-hit, 31) && input.Candidate == hit,
    "Pellets and penetration cannot move the candidate again");
input.Arm(40);
Check(input.Capture(-hit, 40) && input.Candidate == -hit, "New shots replace unconfirmed candidates");
Check(input.TryConfirm(feet, out first) && first == -hit, "Only menu confirmation accepts the first point");
Check(input.Mode == ZoneToolSelectionMode.Shoot && input.Candidate == null, "Confirmation keeps mode and consumes candidate");
Check(!input.TryConfirm(feet, out _), "Next confirmation requires a new shot");
input.Arm(50);
Check(input.Capture(hit, 50) && first == -hit, "Shooting the next candidate leaves the first corner intact");
Check(input.TryConfirm(feet, out second) && second == hit, "Another confirmation accepts the second point");
foreach (var step in new[] { "end first corner", "end second corner", "respawn" })
{
    Check(input.Mode == ZoneToolSelectionMode.Shoot && !input.TryConfirm(feet, out _), $"{step}: the method stays shooting and needs a fresh shot");
    input.Arm(55);
    Check(input.Capture(hit, 55) && input.TryConfirm(feet, out var point) && point == hit,
        $"{step}: one switch applies to the remaining map setup points");
}

input.SelectMode(ZoneToolSelectionMode.Feet);
Check(input.TryConfirm(feet, out first), "Feet can begin another drawing");
input.SelectMode(ZoneToolSelectionMode.Shoot);
Check(first == feet && !input.TryConfirm(-feet, out _), "Switching mid-drawing retains the first corner and requires a shot");
input.Arm(60);
Check(input.Capture(hit, 60), "Shooting can select the second corner after starting at feet");
input.SelectMode(ZoneToolSelectionMode.Feet);
Check(input.Candidate == null && input.TryConfirm(-feet, out second) && second == -feet && first == feet,
    "Switching back discards candidate and confirms feet without moving the first corner");
input.SelectMode(ZoneToolSelectionMode.Shoot);
input.Arm(70);
input.SelectMode(ZoneToolSelectionMode.Feet);
input.SelectMode(ZoneToolSelectionMode.Shoot);
Check(!input.Capture(hit, 70) && !input.TryConfirm(feet, out _), "Switching methods cancels pending shots before drawing too");

input.Arm(100);
Check(!input.Capture(new(float.NaN, 0, 0), 100) &&
      !input.Capture(new(0, float.PositiveInfinity, 0), 100) &&
      !input.Capture(new(0, 0, float.NegativeInfinity), 100), "Invalid impact positions are rejected");
Check(!input.Capture(hit, 99) && !input.Capture(hit, 117), "Impacts outside the shot window are rejected");
Check(input.Capture(Vector3.Zero, 116) && input.TryConfirm(feet, out var origin) && origin == Vector3.Zero,
    "Window boundary and map origin are valid");
input.SelectMode(ZoneToolSelectionMode.Feet);
Check(!input.TryConfirm(new(float.NaN, 0, 0), out _) && !input.TryConfirm(new(0, float.PositiveInfinity, 0), out _),
    "Invalid feet positions cannot be saved");

input.SelectMode(ZoneToolSelectionMode.Shoot);
input.Arm(200);
input.Capture(hit, 200);
input.Reset();
Check(input.Mode == ZoneToolSelectionMode.Feet && input.Candidate == null && input.TryConfirm(feet, out var resetPoint) && resetPoint == feet,
    "Finishing the setup restores default feet and clears the candidate");
input.SelectMode(ZoneToolSelectionMode.Shoot);
Check(!input.Capture(hit, 200), "Reset cancels pending shots");
var other = new ZoneToolShotInput();
other.SelectMode(ZoneToolSelectionMode.Feet);
input.Arm(210);
input.Capture(hit, 210);
Check(other.Mode == ZoneToolSelectionMode.Feet && other.Candidate == null && other.TryConfirm(-feet, out var otherPoint) && otherPoint == -feet,
    "Players have independent methods and candidates");
Check(input.TryConfirm(feet, out var ownPoint) && ownPoint == hit, "Other players cannot consume this candidate");
var pendingRegion = new ZoneToolPendingRegion(ZoneToolRegionKind.End, "old-end-first", "old-end-second");
Check(pendingRegion.TryRestorePrevious(out var oldFirst, out var oldSecond) &&
      oldFirst == "old-end-first" && oldSecond == "old-end-second",
    "Cancelling a replacement edit restores the previously completed region");
pendingRegion.Complete();
Check(!pendingRegion.TryRestorePrevious(out _, out _), "Completing a region commits it instead of restoring the older points later");
input.SelectMode(ZoneToolSelectionMode.Shoot);
input.Arm(220);
Check(input.Capture(hit, 220), "A candidate exists before cancellation");
input.ClearCandidate();
Check(input.Candidate == null && !input.TryConfirm(feet, out _), "Cancelling clears the pending shot without changing the selected mode");
input.Arm(230);
Check(input.Capture(hit, 230) && input.TryConfirmFeet(feet, out var respawnPoint) && respawnPoint == feet,
    "Respawn points always use feet, even while shooting mode is selected");
Check(input.Mode == ZoneToolSelectionMode.Shoot && input.Candidate == null,
    "Feet-only respawn confirmation clears stale shots without changing zone-corner mode");
// Exercise the actual menu options without accessing native player handles.
var routeInput = new ZoneToolShotInput();
routeInput.SelectMode(ZoneToolSelectionMode.Shoot);
var selected = new List<string>();
void ConfirmMenuPoint(string name)
{
    if (routeInput.TryConfirm(feet, out _)) selected.Add(name);
}
var menu = ZoneToolMenu.Create(_ => ConfirmMenuPoint("start"), _ => ConfirmMenuPoint("end"),
    _ => ConfirmMenuPoint("respawn"), _ => selected.Add("save"));
Check(menu.PostSelectAction == PostSelectAction.Nothing && menu.ExitButton, "Zone menu stays open between point confirmations and can be closed explicitly");
Check(menu.MenuOptions.Select(option => option.Text).SequenceEqual(new[] { "startzone", "endzone", "respawnpos", "save" }),
    "The four original menu numbers retain their original order");
void SelectMenuKey(int key)
{
    var option = menu.MenuOptions[key - 1];
    option.OnSelect(null!, option);
}
int tick = 300;
foreach (int key in new[] { 1, 1, 2, 2, 3 })
{
    routeInput.Arm(tick);
    Check(routeInput.Capture(hit, tick++), "A new shot selects a menu candidate");
    SelectMenuKey(key);
    int count = selected.Count;
    SelectMenuKey(key);
    Check(selected.Count == count, "Repeated number commands without another shot cannot skip a point");
}
SelectMenuKey(4);
Check(selected.SequenceEqual(new[] { "start", "start", "end", "end", "respawn", "save" }),
    "The original !1 !1 !2 !2 !3 !4 sequence routes every step to its handler");
Console.WriteLine($"Passed {checks} zone input and menu mapping checks.");
