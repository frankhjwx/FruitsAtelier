using System.Diagnostics;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using FruitsAtelier.Core;

namespace FruitsAtelier.App.Media;

internal static class VideoMetadata
{
    // Custom I/O confines probing to this file, including containers with external stream references.
    internal static unsafe AimodVideoMetadata Read(string path, CancellationToken cancellation = default)
    {
        FFmpegRuntime.Initialize();
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var elapsed = Stopwatch.StartNew();
        bool Interrupted() => cancellation.IsCancellationRequested || elapsed.Elapsed > TimeSpan.FromSeconds(15);
        avio_alloc_context_read_packet read = (_, data, length) =>
        {
            try
            {
                if (Interrupted()) return ffmpeg.AVERROR_EXIT;
                int count = input.Read(new Span<byte>(data, length));
                return count == 0 ? ffmpeg.AVERROR_EOF : count;
            }
            catch (IOException) { return -5; }
        };
        avio_alloc_context_seek seek = (_, offset, whence) =>
        {
            try
            {
                if (Interrupted()) return -1;
                return (whence & ffmpeg.AVSEEK_SIZE) != 0 ? input.Length : input.Seek(offset, (SeekOrigin)(whence & 3));
            }
            catch (IOException) { return -1; }
        };
        byte* buffer = (byte*)ffmpeg.av_malloc(32768);
        if (buffer == null) throw new OutOfMemoryException();
        AVIOContext* io = ffmpeg.avio_alloc_context(buffer, 32768, 0, null, read, null, seek);
        if (io == null) { ffmpeg.av_free(buffer); throw new OutOfMemoryException(); }
        AVFormatContext* format = null;
        try
        {
            format = ffmpeg.avformat_alloc_context();
            if (format == null) throw new OutOfMemoryException();
            format->pb = io;
            format->flags |= ffmpeg.AVFMT_FLAG_CUSTOM_IO;
            format->probesize = 8 * 1024 * 1024;
            format->max_analyze_duration = 5 * ffmpeg.AV_TIME_BASE;
            AVDictionary* options = null;
            ffmpeg.av_dict_set(&options, "protocol_whitelist", "pipe", 0);
            try { Check(ffmpeg.avformat_open_input(&format, "pipe:", null, &options)); }
            finally { ffmpeg.av_dict_free(&options); }
            Check(ffmpeg.avformat_find_stream_info(format, null));
            cancellation.ThrowIfCancellationRequested();
            if (Interrupted()) throw new IOException("Video metadata probe timed out.");
            int video = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            Check(video);
            var properties = format->streams[video]->codecpar;
            bool hasAudio = false;
            for (int i = 0; i < format->nb_streams; i++)
                hasAudio |= format->streams[i]->codecpar->codec_type == AVMediaType.AVMEDIA_TYPE_AUDIO;
            if (properties->width <= 0 || properties->height <= 0) throw new IOException("Video dimensions are unavailable.");
            return new(properties->width, properties->height, hasAudio);
        }
        finally
        {
            if (format != null) ffmpeg.avformat_close_input(&format);
            ffmpeg.av_free(io->buffer);
            ffmpeg.avio_context_free(&io);
            GC.KeepAlive(read); GC.KeepAlive(seek);
        }

        void Check(int result)
        {
            cancellation.ThrowIfCancellationRequested();
            if (result >= 0) return;
            byte* message = stackalloc byte[256];
            ffmpeg.av_strerror(result, message, 256);
            throw new IOException($"FFmpeg: {Marshal.PtrToStringUTF8((nint)message)} ({result}).");
        }
    }
}
