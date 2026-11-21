using System.Numerics;

namespace SharpTimer;

internal enum ZoneToolSelectionMode { Feet, Shoot }
internal enum ZoneToolRegionKind { Start, End, BonusStart, BonusEnd }

internal sealed class ZoneToolPendingRegion
{
    private bool _completed;

    public ZoneToolPendingRegion(ZoneToolRegionKind region, string? previousFirstCorner, string? previousSecondCorner)
    {
        Region = region;
        PreviousFirstCorner = previousFirstCorner;
        PreviousSecondCorner = previousSecondCorner;
    }

    public ZoneToolRegionKind Region { get; }
    public string? PreviousFirstCorner { get; }
    public string? PreviousSecondCorner { get; }
    public float? SecondCornerHeight { get; private set; }

    public bool SetSecondCornerHeight(float height)
    {
        if (_completed || !float.IsFinite(height) || height <= 0) return false;
        SecondCornerHeight = height;
        return true;
    }

    public bool TryRestorePrevious(out string? firstCorner, out string? secondCorner)
    {
        firstCorner = PreviousFirstCorner;
        secondCorner = PreviousSecondCorner;
        return !_completed;
    }

    public void Complete() => _completed = true;
}

// Shooting only updates a candidate. Menu commands confirm points, while the height command sets a pending height.
internal sealed class ZoneToolShotInput
{
    public ZoneToolSelectionMode Mode { get; private set; } = ZoneToolSelectionMode.Feet;
    public Vector3? Candidate { get; private set; }
    private bool _armed;
    private int _shotTick;

    public void SelectMode(ZoneToolSelectionMode mode)
    {
        Mode = mode;
        Candidate = null;
        _armed = false;
    }

    public void Reset()
    {
        SelectMode(ZoneToolSelectionMode.Feet);
    }

    public void ClearCandidate()
    {
        Candidate = null;
        _armed = false;
    }

    public void Arm(int tick)
    {
        _shotTick = tick;
        _armed = Mode == ZoneToolSelectionMode.Shoot;
    }

    public bool Capture(Vector3 hit, int tick)
    {
        if (Mode != ZoneToolSelectionMode.Shoot || !_armed || tick < _shotTick || tick - _shotTick > 16 ||
            !float.IsFinite(hit.X) || !float.IsFinite(hit.Y) || !float.IsFinite(hit.Z)) return false;
        _armed = false;
        Candidate = hit;
        return true;
    }

    public bool TryConfirm(Vector3 feet, out Vector3 point)
    {
        point = default;
        if (Mode == ZoneToolSelectionMode.Shoot && Candidate == null) return false;
        point = Mode == ZoneToolSelectionMode.Feet ? feet : Candidate!.Value;
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)) return false;
        Candidate = null;
        _armed = false;
        return true;
    }

    public bool TryConfirmFeet(Vector3 feet, out Vector3 point)
    {
        point = default;
        if (!float.IsFinite(feet.X) || !float.IsFinite(feet.Y) || !float.IsFinite(feet.Z)) return false;
        point = feet;
        Candidate = null;
        _armed = false;
        return true;
    }
}
