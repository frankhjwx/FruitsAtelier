using FruitsAtelier.App.Rendering;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private void DrawTransportControls(ICanvas c)
    {
        Action[] actions = [Play, Pause, Stop, StartTestplay];
        bool[] enabled = [AudioReady && !AudioPlaying, AudioReady && AudioPlaying, true, !AudioLoading];
        string[] hints = ["timeline.transport.play", "timeline.transport.pause", "timeline.transport.stop", "testplay.start"];
        for (int i = 0; i < actions.Length; i++)
        {
            var r = TransportButtonBounds(i);
            if (i == 3) TestplayButtonBounds = r;
            bool hover = r.Contains(mouseX, mouseY);
            if (hover) c.Fill(r, Chrome(0x35404D, HoverSurface), 4);
            uint ink = enabled[i] ? Chrome(0xF4F4F4, Foreground) : Chrome(0x7B8490, Muted);
            float scale = TransportScale;
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            switch (i)
            {
                case 0:
                    for (int row = -9; row <= 9; row++)
                        c.Line(cx - 8 * scale, cy + row * scale, cx + (9 - Math.Abs(row) * 17f / 9) * scale, cy + row * scale, ink, 1.2f * scale);
                    break;
                case 1:
                    c.Fill(new(cx - 9 * scale, cy - 10 * scale, 7 * scale, 20 * scale), ink);
                    c.Fill(new(cx + 2 * scale, cy - 10 * scale, 7 * scale, 20 * scale), ink);
                    break;
                case 2:
                    c.Fill(new(cx - 9 * scale, cy - 9 * scale, 18 * scale, 18 * scale), ink);
                    break;
                case 3:
                    c.Fill(new(cx - 13 * scale, cy - 10 * scale, 20 * scale, 20 * scale), ink, 3 * scale);
                    for (int row = -8; row <= 8; row++)
                        c.Line(cx + 7 * scale, cy + row * scale, cx + (14 - Math.Abs(row) * 5f / 8) * scale, cy + row * scale, ink, 1.2f * scale);
                    break;
            }
            hits.Add(new(r, actions[i], enabled[i]));
            if (hover)
            {
                string hint = L.Get(hints[i]);
                float w = Math.Max(r.Width, c.MeasureText(hint, 11) + 16);
                float x = Math.Max(8, r.X + (r.Width - w) / 2);
                c.Fill(new(x, overview.Y - 25, w, 22), Chrome(0x111923, Background), 4);
                c.Text(hint, x + 8, overview.Y - 20, 11, Foreground, w - 16);
            }
        }
    }

    private void Play()
    {
        if (AudioReady && !AudioPlaying) TogglePlayback();
    }

    private void Pause() => Pause(snapToGrid: true);

    private void Pause(bool snapToGrid)
    {
        pauseSnapDivisor = null;
        if (!AudioPlaying) return;
        pauseSnapDivisor = !IsLazerEditing && snapToGrid && snap && !TimingPageVisible ? divisor : null;
        if (RequestPausePlayback is not null) RequestPausePlayback();
        else RequestTogglePlayback?.Invoke();
    }

    private void Stop()
    {
        Pause(snapToGrid: false);
        SeekTo(0);
    }
}
