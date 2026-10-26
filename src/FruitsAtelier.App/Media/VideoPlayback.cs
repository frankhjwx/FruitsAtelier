using System.Buffers;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace FruitsAtelier.App.Media;

// The audio transport supplies every requested timestamp; decoding owns no playback clock.
internal sealed class VideoPlayback : IDisposable
{
    internal sealed record Frame(byte[] Pixels, int Width, int Height, double Time, long Serial)
    {
        internal int Readers;
        internal bool Retired;
        internal long Generation;
        internal double EndTime = double.NaN;
    }
    private readonly object gate = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly List<Frame> frames = [];
    private readonly Thread worker;
    private bool disposed;
    private bool ended;
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task Completion => completion.Task;
    private double target, ahead;
    private long generation, serial;
    internal string Path { get; }
    internal string? Error { get; private set; }
    private bool errorObserved;
    internal string? ReadError() { lock (gate) { if (Error is not null) errorObserved = true; return Error; } }
    internal int SeekCount { get; private set; }
    internal double DurationMs { get; private set; }
    internal double FirstFrameTime { get; private set; } = double.NaN;
    internal bool ContainsTime(double time) => time >= 0 && (!double.IsFinite(FirstFrameTime) || time >= FirstFrameTime)
        && (DurationMs <= 0 || time < DurationMs);
    private bool reachedEnd;
    internal float Opacity(double positionMs) => (float)Math.Clamp(Math.Min(positionMs / 500,
        DurationMs > 0 ? (DurationMs - positionMs) / 500 : 1), 0, 1);
    internal double LastDecodedTime { get; private set; }
    internal string DiagnosticState { get { lock (gate) return $"target={target} decoded={LastDecodedTime} generation={generation} seeks={SeekCount} frames={string.Join(',', frames.Select(f => f.Time))}"; } }
    internal long BufferedBytes { get { lock (gate) return frames.Sum(f => (long)f.Pixels.Length); } }
    internal bool NeedsRedraw(long displayedSerial)
    {
        lock (gate)
        {
            if (disposed) return false;
            if (Error is not null) return !errorObserved;
            if (ended) return false;
            return SelectFrame(target) is { } chosen && chosen.Serial != displayedSerial;
        }
    }

    internal VideoPlayback(string path)
    {
        Path = path;
        worker = new Thread(Run) { IsBackground = true, Name = "Beatmap video" };
        worker.Start();
    }

    internal bool WithFrame(double time, bool playing, double rate, Action<Frame> draw)
    {
        Frame? chosen = null;
        lock (gate)
        {
            if (disposed || ended || !double.IsFinite(time)) return false;
            if (time < target - 2 || Math.Abs(time - target) > 200) generation++;
            target = Math.Max(0, time);
            ahead = playing ? Math.Clamp(100 * rate, 50, 150) : 0;
            wake.Set();
            if (time < 0) return false;
            chosen = SelectFrame(time);
            if (chosen is null) return false;
            chosen.Readers++;
        }
        try { draw(chosen); return true; }
        finally { lock (gate) { chosen.Readers--; if (chosen.Retired && chosen.Readers == 0) ArrayPool<byte>.Shared.Return(chosen.Pixels); } }
    }
    private Frame? SelectFrame(double time)
    {
        Frame? chosen = null;
        foreach (var frame in frames)
            if (frame.Time <= time + .001 && (time < frame.EndTime || (reachedEnd && frame.Time >= LastDecodedTime - .001 && time <= DurationMs))
                && (chosen is null || frame.Time > chosen.Time || frame.Time == chosen.Time && frame.Serial > chosen.Serial)) chosen = frame;
        return chosen;
    }
    private static void Retire(Frame frame)
    {
        frame.Retired = true;
        if (frame.Readers == 0) ArrayPool<byte>.Shared.Return(frame.Pixels);
    }

    private unsafe void Run()
    {
        AVFormatContext* format = null;
        AVCodecContext* codec = null;
        AVPacket* packet = null;
        AVFrame* frame = null;
        AVFrame* preceding = null;
        SwsContext* scaler = null;
        AVIOContext* io = null;
        FileStream? input = null;
        avio_alloc_context_read_packet? readCallback = null;
        avio_alloc_context_seek? seekCallback = null;
        try
        {
            FFmpegRuntime.Initialize();
            input = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            readCallback = (_, data, length) =>
            {
                try
                {
                    if (Volatile.Read(ref disposed)) return ffmpeg.AVERROR_EXIT;
                    int count = input.Read(new Span<byte>(data, length));
                    return count == 0 ? ffmpeg.AVERROR_EOF : count;
                }
                catch { return -5; }
            };
            seekCallback = (_, offset, whence) =>
            {
                try
                {
                    if (Volatile.Read(ref disposed)) return -1;
                    if ((whence & ffmpeg.AVSEEK_SIZE) != 0) return input.Length;
                    return input.Seek(offset, (SeekOrigin)(whence & 3));
                }
                catch { return -1; }
            };
            bool seekingSupported = true;
        OpenInput:
            byte* buffer = (byte*)ffmpeg.av_malloc(32768);
            if (buffer == null) throw new OutOfMemoryException();
            io = ffmpeg.avio_alloc_context(buffer, 32768, 0, null, readCallback, null, seekCallback);
            if (io == null) { ffmpeg.av_free(buffer); throw new OutOfMemoryException(); }
            format = ffmpeg.avformat_alloc_context();
            if (format == null) throw new OutOfMemoryException();
            format->pb = io; format->flags |= ffmpeg.AVFMT_FLAG_CUSTOM_IO | ffmpeg.AVFMT_FLAG_GENPTS;
            AVDictionary* options = null;
            ffmpeg.av_dict_set(&options, "ignore_editlist", "1", 0);
            try { Check(ffmpeg.avformat_open_input(&format, "pipe:", null, &options)); }
            finally { ffmpeg.av_dict_free(&options); }
            Check(ffmpeg.avformat_find_stream_info(format, null));
            int index = ffmpeg.av_find_best_stream(format, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, null, 0);
            Check(index);
            AVStream* stream = format->streams[index];
            AVCodec* decoder = ffmpeg.avcodec_find_decoder(stream->codecpar->codec_id);
            if (decoder == null) throw new NotSupportedException("Video codec unavailable.");
            codec = ffmpeg.avcodec_alloc_context3(decoder);
            Check(ffmpeg.avcodec_parameters_to_context(codec, stream->codecpar));
            codec->thread_count = 2;
            Check(ffmpeg.avcodec_open2(codec, decoder, null));
            packet = ffmpeg.av_packet_alloc(); frame = ffmpeg.av_frame_alloc(); preceding = ffmpeg.av_frame_alloc();
            double precedingTime = double.NaN;
            double unit = stream->time_base.num * 1000d / stream->time_base.den;
            double start = stream->start_time == ffmpeg.AV_NOPTS_VALUE ? 0 : stream->start_time * unit;
            DurationMs = stream->duration != ffmpeg.AV_NOPTS_VALUE && stream->duration > 0 ? stream->duration * unit
                : format->duration > 0 ? format->duration / 1000d : 0;
            double cursor = double.NegativeInfinity;
            double seekDestination = double.NegativeInfinity;
            long activeGeneration = -1;
            bool decodingBeginning = !seekingSupported;
            if (!seekingSupported) lock (gate) { activeGeneration = generation; seekDestination = target; }
            bool eof = false;
            while (true)
            {
                double requested, horizon; long requestedGeneration;
                lock (gate)
                {
                    if (disposed) break;
                    requested = target; horizon = target + ahead; requestedGeneration = generation;
                }
                bool cached;
                lock (gate) cached = SelectFrame(requested) is not null;
                if ((requestedGeneration != activeGeneration && (!cached || eof))
                    || (double.IsFinite(cursor) && ((!cached && requested < cursor - 150
                            && !frames.Any(f => f.Generation == requestedGeneration && f.Time > requested && f.Time <= requested + 1000))
                        || (requested > cursor + 1500 && requested > seekDestination + 1500))))
                {
                    // Keyframe decoding can omit reordered frames just before that keyframe's PTS.
                    // A short preroll ensures the selected presentation frame is available.
                    int seekResult = seekingSupported
                        ? ffmpeg.av_seek_frame(format, index, (long)((Math.Max(0, requested - 1000) + start) / unit), ffmpeg.AVSEEK_FLAG_BACKWARD) : -1;
                    if (seekResult < 0)
                    {
                        // Some legacy AVI containers have no usable seek index; restart their
                        // demuxer and decode forward while retaining the bounded presentation cache.
                        seekingSupported = false;
                        ffmpeg.av_frame_free(&frame); ffmpeg.av_frame_free(&preceding); ffmpeg.av_packet_free(&packet); ffmpeg.avcodec_free_context(&codec);
                        if (scaler != null) { ffmpeg.sws_freeContext(scaler); scaler = null; }
                        ffmpeg.avformat_close_input(&format);
                        ffmpeg.av_free(io->buffer); io->buffer = null; ffmpeg.avio_context_free(&io);
                        input.Position = 0;
                        goto OpenInput;
                    }
                    ffmpeg.avcodec_flush_buffers(codec); eof = false; cursor = double.NegativeInfinity;
                    decodingBeginning = requested <= 1000;
                    ffmpeg.av_frame_unref(preceding); precedingTime = double.NaN;
                    reachedEnd = false;
                    seekDestination = requested;
                    SeekCount++;
                }
                activeGeneration = requestedGeneration;
                if (cursor > horizon || eof) { wake.WaitOne(100); continue; }
                int received = ffmpeg.avcodec_receive_frame(codec, frame);
                if (received == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                {
                    int read;
                    do
                    {
                        ffmpeg.av_packet_unref(packet);
                        read = ffmpeg.av_read_frame(format, packet);
                    } while (read >= 0 && packet->stream_index != index);
                    if (read < 0) { ffmpeg.avcodec_send_packet(codec, null); }
                    else Check(ffmpeg.avcodec_send_packet(codec, packet));
                    continue;
                }
                if (received == ffmpeg.AVERROR_EOF)
                {
                    if (double.IsFinite(precedingTime))
                    {
                        Publish(preceding, ref scaler, precedingTime, DurationMs, requestedGeneration);
                        ffmpeg.av_frame_unref(preceding); precedingTime = double.NaN;
                    }
                    eof = true;
                    lock (gate)
                    {
                        reachedEnd = true;
                        foreach (var decoded in frames)
                            if (decoded.Generation == requestedGeneration && double.IsNaN(decoded.EndTime)) decoded.EndTime = DurationMs;
                    }
                    continue;
                }
                Check(received);
                long pts = frame->best_effort_timestamp != ffmpeg.AV_NOPTS_VALUE ? frame->best_effort_timestamp : frame->pts;
                cursor = pts == ffmpeg.AV_NOPTS_VALUE ? (double.IsFinite(cursor) ? cursor + 33.333 : 0) : pts * unit - start;
                LastDecodedTime = cursor;
                if (decodingBeginning && !double.IsFinite(FirstFrameTime)) { FirstFrameTime = cursor; decodingBeginning = false; }
                lock (gate)
                {
                    foreach (var decoded in frames)
                        if (decoded.Generation == requestedGeneration && decoded.Time < cursor && double.IsNaN(decoded.EndTime)) decoded.EndTime = cursor;
                }
                if (cursor < requested)
                {
                    ffmpeg.av_frame_unref(preceding); Check(ffmpeg.av_frame_ref(preceding, frame)); precedingTime = cursor;
                    ffmpeg.av_frame_unref(frame); continue;
                }
                if (double.IsFinite(precedingTime))
                {
                    Publish(preceding, ref scaler, precedingTime, cursor, requestedGeneration);
                    ffmpeg.av_frame_unref(preceding); precedingTime = double.NaN;
                }
                Publish(frame, ref scaler, cursor, double.NaN, requestedGeneration);
                ffmpeg.av_frame_unref(frame);
            }
        }
        catch (Exception ex) { Error = ex.Message; }
        finally
        {
            if (scaler != null) ffmpeg.sws_freeContext(scaler);
            if (frame != null) ffmpeg.av_frame_free(&frame);
            if (preceding != null) ffmpeg.av_frame_free(&preceding);
            if (packet != null) ffmpeg.av_packet_free(&packet);
            if (codec != null) ffmpeg.avcodec_free_context(&codec);
            if (format != null) ffmpeg.avformat_close_input(&format);
            if (io != null) { ffmpeg.av_free(io->buffer); io->buffer = null; ffmpeg.avio_context_free(&io); }
            GC.KeepAlive(readCallback); GC.KeepAlive(seekCallback); input?.Dispose();
            lock (gate) { ended = true; foreach (var value in frames) Retire(value); frames.Clear(); }
            wake.Dispose();
            completion.TrySetResult();
        }
    }

    private unsafe void Publish(AVFrame* frame, ref SwsContext* scaler, double time, double endTime, long requestedGeneration)
    {
        double scale = Math.Min(1, Math.Min(1920d / frame->width, 1080d / frame->height));
        int width = Math.Max(1, (int)(frame->width * scale)), height = Math.Max(1, (int)(frame->height * scale));
        byte[] pixels = ArrayPool<byte>.Shared.Rent(checked(width * height * 4));
        try
        {
            scaler = ffmpeg.sws_getCachedContext(scaler, frame->width, frame->height, (AVPixelFormat)frame->format,
                width, height, AVPixelFormat.AV_PIX_FMT_BGRA, ffmpeg.SWS_BILINEAR, null, null, null);
            if (scaler == null) throw new InvalidOperationException("Video colour conversion unavailable.");
            fixed (byte* output = pixels)
            {
                byte_ptrArray4 data = default; data[0] = output;
                int_array4 lines = default; lines[0] = width * 4;
                ffmpeg.sws_scale(scaler, frame->data, frame->linesize, 0, frame->height, data, lines);
            }
            lock (gate)
            {
                if (disposed || requestedGeneration != generation) return;
                while (frames.Count >= 12 || frames.Sum(f => (long)f.Pixels.Length) + pixels.Length > 96 * 1024 * 1024)
                {
                    var oldest = frames.MaxBy(f => Math.Abs(f.Time - target))!;
                    frames.Remove(oldest); Retire(oldest);
                }
                frames.Add(new(pixels, width, height, time, ++serial) { Generation = requestedGeneration, EndTime = endTime }); pixels = null!;
            }
        }
        finally { if (pixels is not null) ArrayPool<byte>.Shared.Return(pixels); }
    }

    private static unsafe void Check(int result)
    {
        if (result >= 0) return;
        byte* message = stackalloc byte[256];
        ffmpeg.av_strerror(result, message, 256);
        throw new InvalidOperationException($"FFmpeg: {Marshal.PtrToStringUTF8((nint)message)} ({result}).");
    }
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; if (!ended) wake.Set(); } }
}

internal static class FFmpegRuntime
{
    private static readonly object gate = new();
    private static bool initialized;
    internal static void Initialize()
    {
        lock (gate)
        {
            if (initialized) return;
            ffmpeg.GetOrLoadLibrary = name =>
            {
                int version = ffmpeg.LibraryVersionMap[name];
                string file = OperatingSystem.IsWindows() ? $"{name}-{version}.dll" : $"lib{name}.{version}.dylib";
                string rid = OperatingSystem.IsWindows() ? "win-x64" : "osx";
                string nested = System.IO.Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", file);
                string flat = System.IO.Path.Combine(AppContext.BaseDirectory, file);
                return NativeLibrary.Load(File.Exists(flat) ? flat : nested);
            };
            ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
            initialized = true;
        }
    }
}
