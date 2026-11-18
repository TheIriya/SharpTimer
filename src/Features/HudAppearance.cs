using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpTimer;

internal sealed class HudAppearance
{
    private static readonly string[] LayoutTokens =
    ["replay", "timer", "speed", "preSpeed", "sync", "placement", "recordPlacement", "stage", "info", "mapInfo", "mode", "style", "bonus", "serverRecord", "keys", "progress"];
    private static readonly Regex TokenPattern = new(@"\{([A-Za-z][A-Za-z0-9]*)\}", RegexOptions.Compiled);

    public HudColors Colors { get; set; } = new();
    public HudFontClasses FontClasses { get; set; } = new();
    public HudFontSizes FontSizes { get; set; } = new();
    public HudTexts Texts { get; set; } = new();
    public HudPosition Position { get; set; } = new();
    public HudSectionStyles SectionStyles { get; set; } = new();
    public string[] Layout { get; set; } = ["{replay}{timer}", "{speed} {preSpeed}", "{placement} {recordPlacement} {stage}", "{info} {style} {mode} {bonus}", "{serverRecord}", "{mapInfo}", "{keys}"];

    public bool HasLayoutToken(string token) => Layout.Any(row => row.Contains($"{{{token}}}", StringComparison.Ordinal));

    public static HudAppearance Load(string path)
    {
        var config = JsonSerializer.Deserialize<HudAppearance>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        }) ?? throw new JsonException("HUD configuration must be an object.");

        if (config.Colors == null || config.FontClasses == null || config.FontSizes == null ||
            config.Texts == null || config.Position == null || config.SectionStyles == null || config.Layout == null ||
            config.Layout.Any(row => row == null) || config.SectionStyles.HasNullStyle())
            throw new JsonException("HUD settings and layout rows cannot be null.");

        foreach (string row in config.Layout)
            foreach (Match match in TokenPattern.Matches(row))
                if (!LayoutTokens.Contains(match.Groups[1].Value, StringComparer.Ordinal))
                    throw new JsonException($"Unknown HUD layout token: {match.Value}");
        config.Texts.Validate();
        return config;
    }

    public string Render(IReadOnlyDictionary<string, string> elements)
    {
        string rows = string.Join("<br>", Layout.Select(row => TokenPattern.Replace(row,
                match => elements.GetValueOrDefault(match.Groups[1].Value, "")))
            .Where(row => !string.IsNullOrWhiteSpace(row))
            .Select(row => Wrap(row, Position.Row, SectionStyles.Row)));
        string content = Wrap(rows, Position.Container, SectionStyles.Container);
        return string.IsNullOrEmpty(rows) ? content : content + "<br>";
    }

    public string Element(string key, string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var style = SectionStyles.For(key);
        return Wrap(value, Position.For(key), style);
    }

    public string Text(string key, IReadOnlyDictionary<string, string> values)
    {
        string template = Texts.For(key);
        return WebUtility.HtmlEncode(TokenPattern.Replace(template,
            match => values.GetValueOrDefault(match.Groups[1].Value, "")));
    }

    public string FontAttributes(string key, string fallbackClass)
    {
        string className = FontClasses.For(key, fallbackClass);
        string size = FontSizes.For(key);
        string style = string.IsNullOrWhiteSpace(size) ? "" : $"font-size:{size}";
        return (string.IsNullOrWhiteSpace(className) ? "" : $"class='{Attribute(className)}'") +
               (string.IsNullOrWhiteSpace(style) ? "" : $" style='{Attribute(style)}'");
    }

    public string Color(string key, string fallback = "white") => Attribute(Colors.For(key) ?? fallback);

    public string StyledText(string element, string value, string colorKey, string fontKey, string fallbackClass)
    {
        if (string.IsNullOrEmpty(value)) return "";
        string attributes = FontAttributes(fontKey, fallbackClass);
        string color = Color(colorKey);
        return Element(element, $"<font {attributes} color='{color}'>{value}</font>");
    }

    public static string Attribute(string? value) => WebUtility.HtmlEncode(value ?? "");

    private static string Wrap(string content, string? position, HudElementStyle? style)
    {
        string className = style?.Class ?? "";
        string css = string.Join(";", new[] { position, style?.Style }.Where(x => !string.IsNullOrWhiteSpace(x)));
        string attributes = string.IsNullOrWhiteSpace(className) ? "" : $" class='{Attribute(className)}'";
        if (!string.IsNullOrWhiteSpace(css)) attributes += $" style='{Attribute(css)}'";
        return string.IsNullOrEmpty(attributes) ? content : $"<div{attributes}>{content}</div>";
    }
}

internal sealed class HudColors
{
    // Legacy properties are retained for older local HUD files; per-element values below drive rendering.
    public string? Primary { get; set; }
    public string? Secondary { get; set; }
    public string? Tertiary { get; set; }
    public string Muted { get; set; } = "gray";
    public string Replay { get; set; } = "red";
    public string ReplayHeader { get; set; } = "#FF3030";
    public string TimerValue { get; set; } = "#00FF00";
    public string SpeedValue { get; set; } = "#FFA500";
    public string PreSpeed { get; set; } = "#808080";
    public string SyncValue { get; set; } = "#FFA500";
    public string Placement { get; set; } = "#808080";
    public string Stage { get; set; } = "#808080";
    public string Info { get; set; } = "#808080";
    public string MapInfo { get; set; } = "#FFFFFF";
    public string Mode { get; set; } = "#FFFFFF";
    public string Bonus { get; set; } = "#FFFFFF";
    public string Keys { get; set; } = "#FFFFFF";
    public string Progress { get; set; } = "#FF3030";
    public string RecordPlacement { get; set; } = "#808080";
    public string Style { get; set; } = "#FFFFFF";
    public string ServerRecord { get; set; } = "#FFCC66";

    public string? For(string key) => key switch
    {
        "replay" => ReplayHeader, "timer" => TimerValue,
        "speed" => SpeedValue, "preSpeed" => PreSpeed,
        "sync" => SyncValue, "placement" => Placement,
        "stage" => Stage, "info" => Info, "mapInfo" => MapInfo, "mode" => Mode,
        "bonus" => Bonus, "serverRecord" => ServerRecord, "keys" => Keys, "progress" => Progress,
        "recordPlacement" => RecordPlacement, "style" => Style,
        _ => null
    };
}

internal sealed class HudFontClasses
{
    public string Label { get; set; } = "fontSize-s stratum-bold-italic";
    public string Value { get; set; } = "fontSize-l horizontal-center";
    public string ReplayValue { get; set; } = "";
    public string Info { get; set; } = "fontSize-s stratum-bold-italic";
    public string Keys { get; set; } = "fontSize-ml stratum-light-mono";
    public string Replay { get; set; } = "horizontal-center";
    public string For(string key, string fallback) => key switch
    {
        "Label" => Label, "Value" => Value, "ReplayValue" => ReplayValue,
        "Info" => Info, "Keys" => Keys, "Replay" => Replay, _ => fallback
    };
}

internal sealed class HudFontSizes
{
    public string ReplayHeader { get; set; } = "";
    public string Timer { get; set; } = "";
    public string Speed { get; set; } = "";
    public string PreSpeed { get; set; } = "";
    public string Sync { get; set; } = "";
    public string Placement { get; set; } = "";
    public string Stage { get; set; } = "";
    public string MapInfo { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Bonus { get; set; } = "";
    public string Progress { get; set; } = "";
    public string RecordPlacement { get; set; } = "";
    public string Style { get; set; } = "";
    public string ServerRecord { get; set; } = "";
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public string ReplayValue { get; set; } = "";
    public string Info { get; set; } = "";
    public string Keys { get; set; } = "";
    public string Replay { get; set; } = "";
    public string For(string key) => key switch
    {
        "replay" => ReplayHeader, "timer" => Timer, "speed" => Speed, "preSpeed" => PreSpeed,
        "sync" => Sync, "placement" => Placement, "recordPlacement" => RecordPlacement,
        "stage" => Stage, "mapInfo" => MapInfo, "mode" => Mode, "style" => Style, "serverRecord" => ServerRecord,
        "bonus" => Bonus, "progress" => Progress,
        "Label" => Label, "Value" => Value, "ReplayValue" => ReplayValue,
        "Info" => Info, "Keys" => Keys, "Replay" => Replay, _ => ""
    };
}

internal sealed class HudTexts
{
    public string ReplayTime { get; set; } = "◉ REPLAY {time}";
    public string TimerValue { get; set; } = "{time}";
    public string BonusTimerValue { get; set; } = "Bonus #{bonus} Timer: {time}";
    public string Speed { get; set; } = "{speed}";
    public string PreSpeed { get; set; } = "({preSpeed})";
    public string Sync { get; set; } = "{sync}%";
    public string Placement { get; set; } = "({placement})";
    public string RecordPlacement { get; set; } = "[{recordPlacement}]";
    public string Stage { get; set; } = "{stage}";
    public string Info { get; set; } = "{info}";
    public string MapInfo { get; set; } = "{mapInfo}";
    public string Mode { get; set; } = "{mode}";
    public string Style { get; set; } = "{style}";
    public string ServerRecord { get; set; } = "{serverRecord}";
    public string NoRecord { get; set; } = "无记录";
    public string Bonus { get; set; } = "{bonus}";
    public string Keys { get; set; } = "{left} {forward} {right} {back} {jump} {duck}";
    public string Progress { get; set; } = "{progress}";
    public string Left { get; set; } = "A";
    public string Forward { get; set; } = "W";
    public string Right { get; set; } = "D";
    public string Back { get; set; } = "S";
    public string Jump { get; set; } = "J";
    public string Duck { get; set; } = "C";
    public string EmptyKey { get; set; } = "_";
    public string For(string key) => key switch
    {
        "replay" => ReplayTime, "timer" => TimerValue, "bonusTimer" => BonusTimerValue,
        "speed" => Speed, "preSpeed" => PreSpeed, "sync" => Sync, "placement" => Placement,
        "stage" => Stage, "info" => Info, "mapInfo" => MapInfo, "mode" => Mode, "style" => Style,
        "recordPlacement" => RecordPlacement,
        "bonus" => Bonus, "serverRecord" => ServerRecord, "keys" => Keys, "progress" => Progress, _ => ""
    };

    public void Validate()
    {
        string[] known = ["time", "speed", "preSpeed", "sync", "placement", "recordPlacement", "stage", "info", "mapInfo", "mode", "style", "bonus", "serverRecord", "keys", "progress", "left", "forward", "right", "back", "jump", "duck"];
        foreach (var property in GetType().GetProperties().Where(property => property.PropertyType == typeof(string)))
        {
            string template = (string?)property.GetValue(this) ?? throw new JsonException($"Text template {property.Name} cannot be null.");
            foreach (Match match in Regex.Matches(template, @"\{([^{}]+)\}"))
                if (!known.Contains(match.Groups[1].Value, StringComparer.Ordinal))
                    throw new JsonException($"Unknown text placeholder in {property.Name}: {match.Value}");
        }
    }
}

internal sealed class HudPosition
{
    public string Container { get; set; } = "";
    public string Row { get; set; } = "";
    public string Replay { get; set; } = "";
    public string Timer { get; set; } = "";
    public string Speed { get; set; } = "";
    public string PreSpeed { get; set; } = "";
    public string Sync { get; set; } = "";
    public string Placement { get; set; } = "";
    public string Stage { get; set; } = "";
    public string Info { get; set; } = "";
    public string MapInfo { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Bonus { get; set; } = "";
    public string Keys { get; set; } = "";
    public string Progress { get; set; } = "";
    public string RecordPlacement { get; set; } = "";
    public string Style { get; set; } = "";
    public string ServerRecord { get; set; } = "";
    public string For(string key) => key switch
    {
        "replay" => Replay, "timer" => Timer, "speed" => Speed, "preSpeed" => PreSpeed,
        "sync" => Sync, "placement" => Placement, "recordPlacement" => RecordPlacement,
        "stage" => Stage, "info" => Info, "mapInfo" => MapInfo, "mode" => Mode,
        "style" => Style, "bonus" => Bonus, "serverRecord" => ServerRecord, "keys" => Keys,
        "progress" => Progress, _ => ""
    };
}

internal sealed class HudSectionStyles
{
    public HudElementStyle Container { get; set; } = new();
    public HudElementStyle Row { get; set; } = new();
    public HudElementStyle Replay { get; set; } = new();
    public HudElementStyle Timer { get; set; } = new();
    public HudElementStyle Speed { get; set; } = new();
    public HudElementStyle PreSpeed { get; set; } = new();
    public HudElementStyle Sync { get; set; } = new();
    public HudElementStyle Placement { get; set; } = new();
    public HudElementStyle Stage { get; set; } = new();
    public HudElementStyle Info { get; set; } = new();
    public HudElementStyle MapInfo { get; set; } = new();
    public HudElementStyle Mode { get; set; } = new();
    public HudElementStyle Bonus { get; set; } = new();
    public HudElementStyle Keys { get; set; } = new();
    public HudElementStyle Progress { get; set; } = new();
    public HudElementStyle RecordPlacement { get; set; } = new();
    public HudElementStyle Style { get; set; } = new();
    public HudElementStyle ServerRecord { get; set; } = new();
    public HudElementStyle For(string key) => key switch
    {
        "Container" => Container, "Row" => Row, "replay" => Replay, "timer" => Timer,
        "speed" => Speed, "preSpeed" => PreSpeed, "sync" => Sync, "placement" => Placement,
        "recordPlacement" => RecordPlacement, "stage" => Stage, "info" => Info, "mapInfo" => MapInfo, "mode" => Mode, "style" => Style, "serverRecord" => ServerRecord,
        "bonus" => Bonus, "keys" => Keys, "progress" => Progress, _ => new()
    };
    public bool HasNullStyle() => Container == null || Row == null || Replay == null || Timer == null ||
        Speed == null || PreSpeed == null || Sync == null || Placement == null || RecordPlacement == null ||
        Stage == null || Info == null || MapInfo == null || Mode == null || Style == null || ServerRecord == null || Bonus == null || Keys == null || Progress == null;
}

internal sealed class HudElementStyle
{
    public string Class { get; set; } = "";
    public string Style { get; set; } = "";
}
