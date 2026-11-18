using System.Numerics;
using SharpTimer;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    passed++;
}

var camera = new ReplayCameraContinuity();
var origin = new Vector3(0, 0, 64);
var speed = new Vector3(640, 0, 0);
Check(camera.Advance(1, 0, origin, speed), "First frame starts with a fresh camera history");
Check(!camera.Advance(1, 1, origin + new Vector3(10, 0, 0), speed), "Normal movement interpolates smoothly");
Check(!camera.Advance(1, 2, origin + new Vector3(20, 0, 8), speed), "Small jump and collision corrections do not snap the camera");
var destination = new Vector3(4000, 3000, -2000);
Check(camera.Advance(1, 3, destination, Vector3.Zero), "Map teleport clears interpolation across the old and new location");
Check(!camera.Advance(1, 4, destination, Vector3.Zero), "Standing at the destination does not repeatedly reset the camera");
Check(camera.Advance(1, 5, origin, speed), "A second teleport in the same replay also resets the camera");
Check(camera.Advance(1, 0, origin, speed), "Looping back to the first frame resets interpolation");
Check(camera.Advance(2, 1, origin + new Vector3(10, 0, 0), speed), "A new pawn does not reuse the old pawn's camera history");
Check(camera.Advance(2, 10, origin, speed), "Seeking to a different frame resets the camera");
var fast = new ReplayCameraContinuity();
var fastSpeed = new Vector3(12800, 0, 0);
fast.Advance(3, 0, origin, fastSpeed);
Check(!fast.Advance(3, 1, origin + new Vector3(200, 0, 0), fastSpeed), "Fast valid movement is distinguished from teleportation");
Check(fast.Advance(3, 2, origin + new Vector3(0, 0, -1000), fastSpeed), "A teleport during fast movement still resets interpolation");
var other = new ReplayCameraContinuity();
other.Advance(4, 0, origin, Vector3.Zero);
Check(!other.Advance(4, 1, origin, Vector3.Zero), "Each replay has independent camera continuity");

Console.WriteLine($"Passed {passed} replay camera continuity checks.");
