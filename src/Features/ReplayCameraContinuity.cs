using System.Numerics;

namespace SharpTimer;

// Separate real discontinuities from ordinary fast movement. Only a teleport,
// replay loop or new pawn should reset the client's interpolation history.
internal sealed class ReplayCameraContinuity
{
    private nint _pawn;
    private int _frame = -1;
    private Vector3 _position;
    private Vector3 _velocity;

    public bool Advance(nint pawn, int frame, Vector3 position, Vector3 velocity)
    {
        bool reset = pawn != _pawn || frame != _frame + 1 || _frame < 0 ||
            Vector3.DistanceSquared(position, _position + _velocity / 64f) > 64f * 64f;
        _pawn = pawn;
        _frame = frame;
        _position = position;
        _velocity = velocity;
        return reset;
    }
}
