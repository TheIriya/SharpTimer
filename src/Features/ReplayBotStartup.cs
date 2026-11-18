namespace SharpTimer;

// Wait for the controller, pawn and movement services rather than assuming bot_add
// creates them all in the same server frame. Only one startup may run at a time.
internal sealed class ReplayBotStartup
{
    private readonly SemaphoreSlim _startup = new(1, 1);

    public static bool ShouldFallbackToT(bool botFound, bool botOnCt, bool hasLivePawn, int attempts) =>
        !botFound ? attempts >= 4 : botOnCt && !hasLivePawn && attempts >= 8;

    public async Task<bool> Run(Func<Task<bool>> prepare, Func<Task<bool>> tryStart,
        Func<Task> waitForNextAttempt, CancellationToken token, int maxAttempts = 80)
    {
        if (!await _startup.WaitAsync(0, token)) return false;
        try
        {
            token.ThrowIfCancellationRequested();
            bool prepared = await prepare();
            token.ThrowIfCancellationRequested();
            if (!prepared) return false;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();
                bool started = await tryStart();
                token.ThrowIfCancellationRequested();
                if (started) return true;
                if (attempt + 1 < maxAttempts) await waitForNextAttempt();
            }
            return false;
        }
        finally { _startup.Release(); }
    }
}
