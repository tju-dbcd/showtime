namespace ShowtimeBackend.Tests.OrderTicket;

internal static class OracleOrderTicketGate
{
    private static readonly SemaphoreSlim Semaphore = new(1, 1);

    public static async Task<IDisposable> EnterAsync()
    {
        await Semaphore.WaitAsync();
        return new Releaser();
    }

    private sealed class Releaser : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                Semaphore.Release();
        }
    }
}
