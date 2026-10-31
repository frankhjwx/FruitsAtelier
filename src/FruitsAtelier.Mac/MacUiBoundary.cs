using FruitsAtelier.App.Editor;

namespace FruitsAtelier.Mac;

internal sealed class MacUiBoundary(EditorView view, Action<Exception> log, Action refresh)
{
    private bool reporting;

    internal static string Preview(string message)
    {
        if (message.Length <= 8192) return message;
        int length = char.IsHighSurrogate(message[8191]) ? 8191 : 8192;
        return message[..length] + "…";
    }

    internal void Run(Action operation)
    {
        try { operation(); }
        catch (Exception error) { Report(error); }
    }

    internal void Report(Exception error)
    {
        if (reporting) return;
        reporting = true;
        try
        {
            Log(error);
            if (view.RecoveryActive) return;
            view.ShowRenderError(error.Message);
            try { refresh(); }
            catch (Exception refreshError) { Log(refreshError); }
        }
        finally { reporting = false; }
    }

    private void Log(Exception error)
    {
        // Unavailable diagnostic storage must not prevent recovery of the retained project.
        try { log(error); }
        catch (Exception) { }
    }
}
