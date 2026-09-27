using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace FruitsAtelier.App.Audio;

internal sealed record AudioDiagnosticProfile(string Name, bool EventDriven, int BufferMs)
{
    internal static AudioDiagnosticProfile Select(bool enabled, string? name) => enabled ? name switch
    {
        "event-50" => new("event-50", true, 50),
        "poll-10" => new("poll-10", false, 10),
        "poll-50" => new("poll-50", false, 50),
        _ => new("event-10", true, 10)
    } : new("event-10", true, 10);

    internal IWavePlayer CreatePlayer() => new WasapiOut(AudioClientShareMode.Shared, EventDriven, BufferMs);
}
