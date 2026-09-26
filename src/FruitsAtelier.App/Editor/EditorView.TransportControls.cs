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
            var r = new Rect(16 + i * 49, overview.Y + 4, 42, 32);
            if (i == 3) TestplayButtonBounds = r;
            bool hover = r.Contains(mouseX, mouseY);
            if (hover) c.Fill(r, 0x35404D, 4);
            uint ink = enabled[i] ? 0xF4F4F4u : 0x7B8490u;
            float cx = r.X + 21, cy = r.Y + 16;
            switch (i)
            {
                case 0:
                    for (int row = -9; row <= 9; row++)
                        c.Line(cx - 8, cy + row, cx + 9 - Math.Abs(row) * 17f / 9, cy + row, ink, 1.2f);
                    break;
                case 1:
                    c.Fill(new(cx - 9, cy - 10, 7, 20), ink);
                    c.Fill(new(cx + 2, cy - 10, 7, 20), ink);
                    break;
                case 2:
                    c.Fill(new(cx - 9, cy - 9, 18, 18), ink);
                    break;
                case 3:
                    c.Fill(new(cx - 13, cy - 10, 20, 20), ink, 3);
                    for (int row = -8; row <= 8; row++)
                        c.Line(cx + 7, cy + row, cx + 14 - Math.Abs(row) * 5f / 8, cy + row, ink, 1.2f);
                    break;
            }
            hits.Add(new(r, actions[i], enabled[i]));
            if (hover)
            {
                string hint = L.Get(hints[i]);
                float w = Math.Max(r.Width, c.MeasureText(hint, 11) + 16);
                float x = Math.Max(8, r.X + (r.Width - w) / 2);
                c.Fill(new(x, overview.Y - 25, w, 22), 0x111923, 4);
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
        pauseSnapDivisor = snapToGrid && snap ? divisor : null;
        if (RequestPausePlayback is not null) RequestPausePlayback();
        else RequestTogglePlayback?.Invoke();
    }

    private void Stop()
    {
        Pause(snapToGrid: false);
        SeekTo(0);
    }
}
