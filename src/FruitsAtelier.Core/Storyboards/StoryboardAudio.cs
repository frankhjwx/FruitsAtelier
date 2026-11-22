using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace FruitsAtelier.Core;

public sealed record StoryboardAudio(string SourcePath)
{
    [JsonIgnore] public byte[]? Data { get; private set; }
    public void Cache(string savedPath, long remainingBytes = 16 * 1024 * 1024)
    {
        lock (this)
            Data ??= Capture(File.Exists(savedPath) ? savedPath : SourcePath, remainingBytes).Data;
    }
    public static StoryboardAudio Capture(string path, long remainingBytes = 16 * 1024 * 1024)
    {
        byte[] data;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length > Math.Min(16 * 1024 * 1024, remainingBytes)) data = [];
            else
            {
                data = new byte[(int)stream.Length];
                stream.ReadExactly(data);
                if (stream.ReadByte() != -1) data = [];
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { data = []; }
        return new(Path.GetFullPath(path)) { Data = data };
    }
    public static bool ValidRelativePath(string path)
    {
        var parts = path.Split('/');
        return parts.Length == 3 && parts[0] == "fa-storyboard" && parts[1].Length == 64
            && parts[1].All(char.IsAsciiHexDigit) && parts[2].Length > 0
            && parts[2].IndexOfAny(['\\', '"', '\r', '\n', ':']) < 0
            && Path.GetExtension(parts[2]).ToLowerInvariant() is ".wav" or ".ogg" or ".mp3";
    }
    [JsonIgnore] public string RelativePath => "fa-storyboard/" + Convert.ToHexString(SHA256.HashData(Data ?? [])) + "/" + Path.GetFileName(SourcePath);
}
