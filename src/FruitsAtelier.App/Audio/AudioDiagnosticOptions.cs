namespace FruitsAtelier.App.Audio;

internal sealed record AudioDiagnosticOptions(string TempoProfile, int LatencyMs, bool CapturePcm)
{
    public static AudioDiagnosticOptions Read(bool enabled) => !enabled ? new("default", 80, false) : new(
        Environment.GetEnvironmentVariable("FRUITSATELIER_AUDIO_TEMPO") == "short-window" ? "short-window" : "default",
        Environment.GetEnvironmentVariable("FRUITSATELIER_AUDIO_BUFFER") == "160" ? 160 : 80,
        Environment.GetEnvironmentVariable("FRUITSATELIER_AUDIO_CAPTURE") == "1");
}
