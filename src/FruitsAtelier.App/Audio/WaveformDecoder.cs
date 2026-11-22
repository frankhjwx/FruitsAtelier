using FruitsAtelier.Core;
using NAudio.Wave;

namespace FruitsAtelier.App.Audio;

internal static class WaveformDecoder
{
    internal static WaveStream OpenMemory(string path, byte[] data) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".ogg" => new NAudio.Vorbis.VorbisWaveReader(new MemoryStream(data, writable: false)),
        ".wav" => new WaveFileReader(new MemoryStream(data, writable: false)),
        ".mp3" => new StreamMediaFoundationReader(new MemoryStream(data, writable: false)),
        _ => throw new InvalidDataException()
    };
    public static Task<AudioWaveform> LoadMemory(string path, byte[] data, CancellationToken token) => Task.Run(() =>
    {
        using var reader = OpenMemory(path, data);
        var source = reader.ToSampleProvider();
        return AudioWaveform.Read(source.WaveFormat.SampleRate, source.WaveFormat.Channels,
            b => source.Read(b, 0, b.Length), token);
    }, token);
    public static Task<AudioWaveform> Load(string path, CancellationToken token) => Task.Run(() =>
    {
        using WaveStream reader = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ogg" => new NAudio.Vorbis.VorbisWaveReader(path),
            ".wav" => new WaveFileReader(path),
            ".mp3" => MediaFoundationAudioReader.Open(path, () => token.IsCancellationRequested),
            _ => throw new NotSupportedException()
        };
        var source = reader.ToSampleProvider();
        return AudioWaveform.Read(source.WaveFormat.SampleRate, source.WaveFormat.Channels,
            b => source.Read(b, 0, b.Length), token);
    }, token);
}
