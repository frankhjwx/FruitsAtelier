namespace FruitsAtelier.Mac;

internal sealed class MacRenderRecovery(Action<Exception> recover, Action<Exception> fallback)
{
    private bool fallbackRequested;

    internal void BeginFrame(bool recovering)
    {
        if (!recovering) fallbackRequested = false;
    }

    internal void Fail(Exception error, bool recovering)
    {
        if (!recovering) recover(error);
        else if (!fallbackRequested)
        {
            fallbackRequested = true;
            fallback(error);
        }
    }
}
