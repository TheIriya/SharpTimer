using System.Text.Json;
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
Check(rendered.EndsWith("<br>", StringComparison.Ordinal), "The final layout row is terminated for the CS2 center HTML renderer");
normal.Position.Replay = "position:relative; top:-12px; left:8px";
Check(normal.Element("replay", "Replay").Contains("top:-12px"), "Each display element has independent position CSS");
Check(normal.StyledText("speed", "500", "speed", "speed", "") .Contains("#FFA500"), "Each display element has an independent color");
normal.FontSizes.Speed = "18px";
Check(normal.FontAttributes("speed", "") == " style='font-size:18px'", "CSS font sizes are independently configurable");
normal.Texts.Speed = "Velocity {speed} u/s";
Check(normal.Text("speed", new Dictionary<string, string> { ["speed"] = "520" }) == "Velocity 520 u/s", "Text templates substitute value placeholders");
Check(normal.Text("speed", new Dictionary<string, string> { ["speed"] = "<img>" }).Contains("&lt;img&gt;"), "Template values are encoded as plain text");
Check(replay.Layout.Contains("{progress}"), "Replay progress can be shown in its own HUD");
Check(HudAppearance.Attribute("red' onclick='x") == "red&#39; onclick=&#39;x", "Style attributes cannot break out of quotes");

string tempFile = Path.GetTempFileName();
try
{
    File.WriteAllText(tempFile, """
        { // Comments and trailing commas are accepted.
          "colors": { "SpeedValue": "#123456", },
          "fontSizes": { "Speed": "21px", },
          "position": { "Replay": "top:-10px; left:6px" },
          "texts": { "ReplayTime": "RUN {time}" },
          "layout": ["{speed}", "{replay}",],
        }
        """);
    var config = HudAppearance.Load(tempFile);
    Check(config.Colors.SpeedValue == "#123456", "Per-item colors load");
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
    Check(emptyLayout.Texts.NoRecord == "无记录", "Unranked HUD text defaults to the requested no-record label");
}
finally
{
    File.Delete(tempFile);
}

Console.WriteLine($"Passed {passed} HUD appearance checks.");
