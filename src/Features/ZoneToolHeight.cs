using System.Globalization;
using System.Numerics;

namespace SharpTimer;

internal static class ZoneToolHeight
{
    public static bool TryGetSecondCornerZ(string? firstCorner, float height, out float z)
    {
        z = 0;
        if (!float.IsFinite(height) || height <= 0 || !TryParseCorner(firstCorner, out var first))
            return false;

        z = first.Z + height;
        return float.IsFinite(z);
    }

    public static bool TrySetSecondCornerHeight(string? firstCorner, string? secondCorner, float height,
        out string adjustedSecondCorner)
    {
        adjustedSecondCorner = "";
        if (!TryParseCorner(secondCorner, out var second) ||
            !TryGetSecondCornerZ(firstCorner, height, out float adjustedZ))
            return false;

        adjustedSecondCorner = FormattableString.Invariant($"{second.X} {second.Y} {adjustedZ}");
        return true;
    }

    private static bool TryParseCorner(string? corner, out Vector3 point)
    {
        point = default;
        if (string.IsNullOrWhiteSpace(corner)) return false;

        string[] values = corner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (values.Length != 3 ||
            !float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
            !float.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) ||
            !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
            return false;

        point = new Vector3(x, y, z);
        return true;
    }
}
