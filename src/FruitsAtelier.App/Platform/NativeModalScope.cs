namespace FruitsAtelier.App.Platform;

internal sealed class NativeModalScope : IDisposable
{
    [ThreadStatic] private static int depth;
    internal static bool Active => depth > 0;
    private readonly nint owner;
    private bool disposed;

    internal NativeModalScope(nint owner)
    {
        this.owner = owner;
        // Modal APIs pump the STA message queue, including owner paint and timer messages.
        depth++;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        depth--;
        if (!Active && owner != 0) Native.InvalidateRect(owner, 0, false);
    }
}
