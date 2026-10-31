namespace FruitsAtelier.Mac;

internal sealed class MacRecoveryClose(Action<Exception> report)
{
    internal bool Pending { get; private set; }

    internal async Task Request(Func<Task<bool>> confirm, Action close)
    {
        if (Pending) return;
        Pending = true;
        try { if (await confirm()) close(); }
        catch (Exception error) { report(error); }
        finally { Pending = false; }
    }
}
