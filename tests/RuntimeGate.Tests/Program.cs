using SharpTimer;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    passed++;
}

var gate = new RuntimeGate();
Check(gate.IsActive && gate.Enabled, "Enabled by default");
var original = gate.CurrentToken;
Check(!gate.SetEnabled(true) && !original.IsCancellationRequested, "Repeated enable preserves active work");

gate.SetEnabled(false);
Check(!gate.IsActive && original.IsCancellationRequested, "Disable cancels the current enable period");
int calls = 0;
gate.Invoke(original, () => calls++);
await gate.Run(() => { calls++; });
await gate.Run(async () => { await Task.Yield(); calls++; });
var disabledResult = await gate.Run<string>(() => { calls++; return "unexpected"; });
Check(calls == 0 && disabledResult == null, "Disabled callbacks, background work and reads do not run");

gate.SetEnabled(true);
gate.Invoke(original, () => calls++);
gate.Invoke(gate.CurrentToken, () => calls++);
Check(calls == 1, "Re-enabling accepts new callbacks and rejects old callbacks");

Check(await gate.Await(Task.FromResult(42)) == 42, "Active asynchronous reads return their result");
Check(await gate.Run(() => "active") == "active", "Active background reads return their result");

// Hold an in-flight request across disable/enable, then release it. Its continuation
// must not update gameplay state or schedule new work in the new enable period.
var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
bool staleContinuation = false;
var pending = gate.Run(async () =>
{
    entered.SetResult();
    await gate.Await(release.Task);
    staleContinuation = true;
});
await entered.Task;
gate.SetEnabled(false);
gate.SetEnabled(true);
release.SetResult();
await pending;
Check(!staleContinuation && gate.IsActive, "In-flight awaits cannot resume after disable/enable");

entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
release = new(TaskCreationOptions.RunContinuationsAsynchronously);
bool oldWorkActive = true;
int nestedCalls = 0;
pending = gate.Run(async () =>
{
    entered.SetResult();
    await release.Task;
    oldWorkActive = gate.IsActive;
    await gate.Run(() => { nestedCalls++; });
    gate.Invoke(gate.CurrentToken, () => nestedCalls++);
});
await entered.Task;
gate.SetEnabled(false);
gate.SetEnabled(true);
release.SetResult();
await pending;
Check(!oldWorkActive && nestedCalls == 0, "Old asynchronous context cannot create fresh work after re-enable");

var mapToken = gate.CurrentToken;
gate.CancelMapWork();
gate.Invoke(mapToken, () => nestedCalls++);
Check(mapToken.IsCancellationRequested && gate.IsActive && nestedCalls == 0, "Map changes invalidate callbacks while keeping the switch enabled");

gate.SetEnabled(false);
gate.CancelMapWork();
Check(!gate.IsActive && !gate.SetEnabled(false), "Map changes and repeated disable keep the plugin disabled");

try
{
    await gate.Await(Task.FromResult(1));
    throw new Exception("Disabled await returned a value");
}
catch (OperationCanceledException)
{
    Check(true, "Disabled asynchronous reads are canceled");
}

Console.WriteLine($"Passed {passed} runtime switch checks.");
