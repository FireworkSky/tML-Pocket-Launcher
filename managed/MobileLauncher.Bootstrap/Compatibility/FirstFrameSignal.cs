namespace MobileLauncher.Bootstrap.Compatibility;

/// <summary>Dismiss the startup surface after the first successful presentation.</summary>
public sealed class FirstFrameSignal(Action notify)
{
    private int signaled;

    public object? AfterPresent(Func<object?> present)
    {
        var result = present();
        if (Volatile.Read(ref signaled) == 0 &&
            Interlocked.CompareExchange(ref signaled, 1, 0) == 0)
        {
            try { notify(); }
            catch (Exception error)
            {
                Volatile.Write(ref signaled, 0);
                Console.Error.WriteLine($"[Startup] First frame notification failed: {error.Message}");
            }
        }
        return result;
    }
}
