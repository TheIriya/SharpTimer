using System.Globalization;
using System.Numerics;

namespace SharpTimer;

internal static class HudSpeedMetrics
{
    public static string Format(double speed) => Math.Round(speed).ToString("0", CultureInfo.InvariantCulture);

    public static double Measure(Vector3 speed, bool use2D) => use2D
        ? Math.Sqrt(speed.X * speed.X + speed.Y * speed.Y)
        : speed.Length();
}

internal sealed class ReplayHudMetrics
{
    private bool? _wasOnGround;

    public Vector3? LastSpeed { get; private set; }
    public double? TakeoffSpeed { get; private set; }

    public void Reset()
    {
        _wasOnGround = null;
        LastSpeed = null;
        TakeoffSpeed = null;
    }

    public void RecordFrame(Vector3 speed, bool onGround)
    {
        if (float.IsFinite(speed.X) && float.IsFinite(speed.Y) && float.IsFinite(speed.Z))
        {
            LastSpeed = speed;
            if (_wasOnGround == true && !onGround)
                TakeoffSpeed = HudSpeedMetrics.Measure(speed, use2D: true);
        }

        _wasOnGround = onGround;
    }
}
