using System.Text.Json;
using System.Numerics;
using SharpTimer;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    passed++;
}

var normal = HudAppearance.Load(Path.Join(AppContext.BaseDirectory, "example.hud.json"));
var replay = HudAppearance.Load(Path.Join(AppContext.BaseDirectory, "example.replay_hud.json"));
Check(normal.Colors.Secondary == null, "Legacy color fields remain optional");
Check(replay.Colors.ReplayHeader == "#FF3030", "Replay preset has independent colors");
Check(normal.FontClasses.Sync == "fontSize-l horizontal-center", "Normal preset exposes an independent sync font class");
Check(normal.Layout.Contains("{speed} {preSpeed}"), "Example displays speed and pre-speed on one row");
Check(normal.Layout.Contains("{keys}"), "Normal HUD example keeps movement keys visible");
Check(replay.Layout.Contains("{keys}"), "Replay HUD example keeps movement keys visible");
Check(normal.Layout.Contains("{serverRecord}"), "Server record is available as a HUD row");
Check(normal.Layout.Contains("{serverRecordTime}"), "Server record time has an independent HUD row");
normal.Layout = ["{keys}", "{serverRecordTime}", "{serverRecord}", "{replay}", "{speed} {preSpeed}"];
var elements = new Dictionary<string, string>
{
    ["keys"] = normal.Element("keys", "<font>W _</font>"),
    ["serverRecord"] = normal.StyledText("serverRecord", normal.Text("serverRecord", new Dictionary<string, string> { ["serverRecord"] = "Player | 12.34s" }), "serverRecord", "serverRecord", ""),
    ["serverRecordTime"] = normal.StyledText("serverRecordTime", normal.Text("serverRecordTime", new Dictionary<string, string> { ["serverRecordTime"] = "12.34s" }), "serverRecordTime", "serverRecordTime", ""),
    ["replay"] = normal.Element("replay", "Replay 52.4s"),
    ["speed"] = normal.Element("speed", "500"),
    ["preSpeed"] = normal.Element("preSpeed", "(258)")
};
Check(normal.Render(elements).Contains("SR: Player | 12.34s"), "Server record text is substituted into the HUD layout");
Check(normal.Render(elements).Contains("SR time: 12.34s"), "Server record time is independently formatted in the HUD");
string rendered = normal.Render(elements);
Check(rendered.IndexOf("<font>W _</font>", StringComparison.Ordinal) < rendered.IndexOf("SR: Player | 12.34s", StringComparison.Ordinal) &&
      rendered.IndexOf("SR: Player | 12.34s", StringComparison.Ordinal) < rendered.IndexOf("Replay 52.4s", StringComparison.Ordinal) &&
      rendered.Contains("500 (258)"), "Layout reorders, combines and spaces individual elements");
Check(!rendered.EndsWith("<br>", StringComparison.Ordinal), "The layout does not add a trailing blank row");
normal.Layout = ["{speed}", "{keys}"];
string keysLast = normal.Render(elements);
Check(keysLast.Contains("W _") && keysLast.Contains("</font><br><font style='font-size:0px;line-height:0px'>&#8203;</font>", StringComparison.Ordinal),
    "The final row is explicitly terminated, followed by a zero-height sentinel");
normal.Position.Replay = "position:relative; top:-12px; left:8px";
Check(normal.Element("replay", "Replay").Contains("top:-12px"), "Each display element has independent position CSS");
Check(normal.StyledText("speed", "500", "speed", "speed", "") .Contains("#FFA500"), "Each display element has an independent color");
normal.FontSizes.Speed = "18px";
Check(normal.FontAttributes("speed", "") == " style='font-size:18px'", "CSS font sizes are independently configurable");
normal.FontClasses.Sync = "fontSize-m horizontal-center";
Check(normal.FontClasses.For("Sync", normal.FontClasses.Value) == "fontSize-m horizontal-center" &&
      normal.FontAttributes("sync", "") == "class='fontSize-m horizontal-center'",
    "Sync rendering uses its independent font class");
normal.Texts.Speed = "Velocity {speed} u/s";
Check(normal.Text("speed", new Dictionary<string, string> { ["speed"] = "520" }) == "Velocity 520 u/s", "Text templates substitute value placeholders");
Check(normal.Text("speed", new Dictionary<string, string> { ["speed"] = "<img>" }).Contains("&lt;img&gt;"), "Template values are encoded as plain text");
Check(replay.Layout.Contains("{progress}"), "Replay progress can be shown in its own HUD");
Check(HudAppearance.Attribute("red' onclick='x") == "red&#39; onclick=&#39;x", "Style attributes cannot break out of quotes");
Check(HudAppearance.FormatRecordSeparator("|| PB: 12.34s", show: true, "#FFFFFF") == "<font color='#FFFFFF'>||</font> PB: 12.34s",
    "The SR/PB separator is white when both records are present");
Check(HudAppearance.FormatRecordSeparator("|| PB: 12.34s", show: false, "#FFFFFF") == "PB: 12.34s",
    "The SR/PB separator is omitted when either record is missing");
Check(HudSpeedMetrics.Format(0) == "0" && HudSpeedMetrics.Format(17) == "17" &&
      HudSpeedMetrics.Format(347) == "347" && HudSpeedMetrics.Format(1234) == "1234",
    "Speed values use their natural digit count without zero padding");
Check(HudSpeedMetrics.Measure(new Vector3(300, 400, 120), use2D: true) == 500 &&
      Math.Abs(HudSpeedMetrics.Measure(new Vector3(300, 400, 120), use2D: false) - Math.Sqrt(264400)) < 0.001,
    "Speed measurement follows the configured 2D or 3D mode");
var replayMetrics = new ReplayHudMetrics();
replayMetrics.RecordFrame(new Vector3(340, 0, 0), onGround: true);
replayMetrics.RecordFrame(new Vector3(352, 0, 20), onGround: false);
Check(replayMetrics.LastSpeed == new Vector3(352, 0, 20) && replayMetrics.TakeoffSpeed == HudSpeedMetrics.Measure(new Vector3(352, 0, 20), true),
    "Replay HUD uses recorded frame velocity and records the first airborne speed");
replayMetrics.RecordFrame(new Vector3(360, 0, 0), onGround: false);
Check(replayMetrics.LastSpeed == new Vector3(360, 0, 0) && replayMetrics.TakeoffSpeed == 352,
    "Replay speed continues to follow frames while airborne without input changes");
replayMetrics.Reset();
Check(replayMetrics.LastSpeed == null && replayMetrics.TakeoffSpeed == null,
    "Starting a replay loop clears the previous loop's speed metrics");

string tempFile = Path.GetTempFileName();
try
{
    File.WriteAllText(tempFile, """
        { // Comments and trailing commas are accepted.
          "colors": { "SpeedValue": "#123456", },
          "fontClasses": { "Sync": "fontSize-m horizontal-center", },
          "fontSizes": { "Speed": "21px", },
          "position": { "Replay": "top:-10px; left:6px" },
          "texts": { "ReplayTime": "RUN {time}" },
          "layout": ["{speed}", "{replay}",],
        }
        """);
    var config = HudAppearance.Load(tempFile);
    Check(config.Colors.SpeedValue == "#123456", "Per-item colors load");
    Check(config.FontClasses.Sync == "fontSize-m horizontal-center", "Sync font class loads from HUD configuration");
    Check(config.FontSizes.Speed == "21px", "Per-item font size loads");
    Check(config.Position.Replay == "top:-10px; left:6px", "Per-item position loads");
    Check(config.Texts.ReplayTime == "RUN {time}", "Text can be changed independently");
    Check(config.Texts.ServerRecordTime == "SR time: {serverRecordTime}", "Server record time template loads independently");

    foreach (string invalid in new[]
    {
        "null", "[]", "{", "{\"Colors\":null}", "{\"FontSizes\":null}", "{\"Texts\":null}",
        "{\"Position\":null}", "{\"SectionStyles\":null}", "{\"Layout\":null}",
        "{\"Layout\":[null]}", "{\"Layout\":[\"{typo}\"]}", "{\"Colours\":{}}",
        "{\"Texts\":{\"Speed\":\"{unknown}\"}}", "{\"SectionStyles\":{\"Speed\":null}}"
    })
    {
        File.WriteAllText(tempFile, invalid);
        bool rejected = false;
        try { HudAppearance.Load(tempFile); }
        catch (JsonException) { rejected = true; }
        Check(rejected, $"Invalid configuration rejected: {invalid}");
    }

    File.WriteAllText(tempFile, "{\"Layout\":[]}");
    var emptyLayout = HudAppearance.Load(tempFile);
    Check(emptyLayout.Render(elements) == "", "Empty layout intentionally hides all items");
    Check(emptyLayout.Texts.NoRecord == "无排名({totalPlayers})", "Unranked HUD text includes the completed-player count");
    Check(emptyLayout.NoRecord(0) == "无排名(0)", "An empty map shows zero completed players");
    Check(emptyLayout.NoRecord(12) == "无排名(12)", "Unranked HUD text shows the completed-player count");
    Check(emptyLayout.Text("recordPlacement", new Dictionary<string, string> { ["recordPlacement"] = emptyLayout.NoRecord(12) }) == "[无排名(12)]",
        "The rank placement template adds exactly one pair of brackets");
}
finally
{
    File.Delete(tempFile);
}

Console.WriteLine($"Passed {passed} HUD appearance checks.");
