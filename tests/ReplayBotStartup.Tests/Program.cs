using SharpTimer;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    passed++;
}

var startup = new ReplayBotStartup();
Check(!ReplayBotStartup.ShouldFallbackToT(false, false, false, 3), "Allow delayed CT controller creation before fallback");
Check(ReplayBotStartup.ShouldFallbackToT(false, false, false, 4), "Try T after repeated CT add failures");
Check(!ReplayBotStartup.ShouldFallbackToT(true, true, true, 10), "Keep a ready CT bot when the map supports CT spawns");
Check(!ReplayBotStartup.ShouldFallbackToT(true, true, false, 7), "Allow a CT bot time to receive its pawn");
Check(ReplayBotStartup.ShouldFallbackToT(true, true, false, 8), "Try T when CT bot never receives a live pawn");
Check(!ReplayBotStartup.ShouldFallbackToT(true, false, false, 20), "Do not replace a T bot that is still waiting for its pawn");
int attempts = 0, delays = 0, prepared = 0;
bool result = await startup.Run(() => { prepared++; return Task.FromResult(true); },
    () => Task.FromResult(++attempts == 4), () => { delays++; return Task.CompletedTask; }, CancellationToken.None);
Check(result && prepared == 1 && attempts == 4 && delays == 3,
    "Slow pawn creation retries readiness without spawning extra bots");

attempts = delays = 0;
result = await startup.Run(() => Task.FromResult(true), () => { attempts++; return Task.FromResult(false); },
    () => { delays++; return Task.CompletedTask; }, CancellationToken.None, maxAttempts: 5);
Check(!result && attempts == 5 && delays == 4, "A missing or dead bot times out instead of waiting forever");

attempts = 0;
result = await startup.Run(() => Task.FromResult(false), () => { attempts++; return Task.FromResult(true); },
    () => throw new Exception("Unexpected delay"), CancellationToken.None);
Check(!result && attempts == 0, "Missing replay data prevents bot creation and readiness polling");

var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Task<bool> active = startup.Run(async () => { entered.SetResult(); await release.Task; return true; },
    () => Task.FromResult(true), () => Task.CompletedTask, CancellationToken.None);
await entered.Task;
bool duplicatePrepared = false;
result = await startup.Run(() => { duplicatePrepared = true; return Task.FromResult(true); },
    () => Task.FromResult(true), () => Task.CompletedTask, CancellationToken.None);
Check(!result && !duplicatePrepared, "Overlapping map, record and console requests cannot kick or replace a starting bot");
release.SetResult();
Check(await active, "The original startup can finish after a duplicate request");

using var cancellation = new CancellationTokenSource();
attempts = 0;
try
{
    await startup.Run(() => Task.FromResult(true), () => { attempts++; return Task.FromResult(false); },
        () => { cancellation.Cancel(); return Task.CompletedTask; }, cancellation.Token);
    throw new Exception("Canceled readiness polling completed");
}
catch (OperationCanceledException) { Check(attempts == 1, "Disabling or changing maps stops readiness retries"); }

using var duringPreparation = new CancellationTokenSource();
attempts = 0;
try
{
    await startup.Run(() => { duringPreparation.Cancel(); return Task.FromResult(true); },
        () => { attempts++; return Task.FromResult(true); }, () => Task.CompletedTask, duringPreparation.Token);
    throw new Exception("Canceled preparation started playback");
}
catch (OperationCanceledException) { Check(attempts == 0, "Cancellation during bot creation cannot start playback afterward"); }

try
{
    await startup.Run(() => Task.FromResult(true), () => throw new IOException("Replay failed to load"),
        () => Task.CompletedTask, CancellationToken.None);
    throw new Exception("Read failure was ignored");
}
catch (IOException) { Check(true, "Replay read errors reach the startup error handler"); }
Check(await startup.Run(() => Task.FromResult(true), () => Task.FromResult(true),
    () => Task.CompletedTask, CancellationToken.None), "Startup can be retried after timeout, cancellation or read errors");

Console.WriteLine($"Passed {passed} replay bot startup checks.");
