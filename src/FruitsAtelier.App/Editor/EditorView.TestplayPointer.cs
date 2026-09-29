using FruitsAtelier.App.Rendering;
using FruitsAtelier.App.Skinning;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool testplayKeyboardSelection, testplayCursorPressed;
    private double testplaySelectionAt, testplayCursorPressedAt, testplayTrailAt;
    private float testplayTrailX = -1, testplayTrailY = -1;
    private int testplayTrailNext;
    private readonly (float X, float Y, double At)[] testplayTrail = new (float, float, double)[512];
    private readonly (float From, float Target, double At)[] testplayButtonAnimations = new (float, float, double)[4];
    public bool TestplayUsesCursor => IsTestplaying;

    private void ResetTestplayPointer()
    {
        testplayKeyboardSelection = testplayCursorPressed = false;
        testplayHoveredMenu = -1;
        testplayTrailX = testplayTrailY = -1;
        testplayTrailNext = 0;
        Array.Fill(testplayTrail, (0f, 0f, double.NegativeInfinity));
        Array.Fill(testplayButtonAnimations, (1f, 1f, 0d));
    }

    private float TestplayButtonScale(int index, bool active)
    {
        var a = testplayButtonAnimations[index];
        float t = (float)Math.Clamp((TestplayRealtime - a.At) / 200, 0, 1);
        float value = a.From + (a.Target - a.From) * (1 - MathF.Pow(1 - t, 5));
        float target = active ? 1.1f : 1;
        if (a.Target != target) testplayButtonAnimations[index] = (value, target, TestplayRealtime);
        return value;
    }

    private void MoveTestplayCursor(float x, float y)
    {
        if (!TestplayPaused || x < 0 || y < 0) return;
        double now = TestplayRealtime;
        bool continuous = skin?.MenuTexture("cursormiddle") is not null;
        if (testplayTrailX >= 0 && continuous)
        {
            float dx = x - testplayTrailX, dy = y - testplayTrailY;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            var texture = skin?.MenuTexture("cursortrail");
            float interval = Math.Max(2, (texture?.PixelWidth ?? 16) / (float)(texture?.Density ?? 1) * height / 768f / 2.5f);
            int steps = Math.Min(64, (int)(distance / interval));
            for (int i = 1; i <= steps; i++)
            {
                float fraction = i / (float)(steps + 1);
                Add(testplayTrailX + dx * fraction, testplayTrailY + dy * fraction);
            }
        }
        else if (now - testplayTrailAt >= 1000d / 60) Add(x, y);
        testplayTrailX = x; testplayTrailY = y;
        void Add(float px, float py)
        {
            testplayTrail[testplayTrailNext] = (px, py, now);
            testplayTrailNext = (testplayTrailNext + 1) % testplayTrail.Length;
            testplayTrailAt = now;
        }
    }

    private void DrawTestplayCursor(ICanvas c)
    {
        if (!TestplayPaused || mouseX < 0 || mouseY < 0 || mouseX >= width || mouseY >= height) return;
        bool continuous = skin?.MenuTexture("cursormiddle") is not null;
        float scale = height / 768f;
        float rotation = skin?.CursorRotate == true ? (float)(TestplayRealtime % 10000 / 10000 * 360) : 0;
        if (skin?.MenuTexture("cursortrail") is { } trail)
            for (int i = 0; i < testplayTrail.Length; i++)
            {
                var point = testplayTrail[(testplayTrailNext + i) % testplayTrail.Length];
                float alpha = 1 - (float)((TestplayRealtime - point.At) / (continuous ? 500 : 150));
                if (alpha <= 0) continue;
                Draw(trail, point.X, point.Y, scale, alpha, skin.CursorTrailRotate ? rotation : 0, continuous);
            }
        float expand = testplayCursorPressed && skin?.CursorExpand != false
            ? 1 + .3f * (float)Math.Clamp((TestplayRealtime - testplayCursorPressedAt) / 100, 0, 1) : 1;
        if (skin?.MenuTexture("cursor") is { } cursor)
            Draw(cursor, mouseX, mouseY, scale * expand, 1, rotation, false);
        else
        {
            c.Circle(mouseX, mouseY, 12 * scale * expand, 0xFFFFFF, false, 2 * scale);
            c.Circle(mouseX, mouseY, 3 * scale, 0x5AE3D2);
        }
        if (skin?.MenuTexture("cursormiddle") is { } middle)
            Draw(middle, mouseX, mouseY, scale, 1, 0, false);
        void Draw(SkinTexture texture, float x, float y, float size, float alpha, float angle, bool additive)
        {
            float w = texture.PixelWidth / (float)texture.Density * size, h = texture.PixelHeight / (float)texture.Density * size;
            bool centre = continuous && additive || skin?.CursorCentre != false;
            c.SpriteImage(texture.FilePath, new(x - (centre ? w / 2 : 0), y - (centre ? h / 2 : 0), w, h),
                0xFFFFFF, new(0, 0, texture.PixelWidth, texture.PixelHeight), alpha, angle, additive);
        }
    }

    private void DrawTestplaySelectionArrows(ICanvas c, float opacity)
    {
        Rect button = testplayMenuBounds[testplayMenuSelection];
        float scale = height / 768f;
        float t = (float)Math.Clamp((TestplayRealtime - testplaySelectionAt) / 200, 0, 1);
        float offset = 16 * (1 - t) * scale;
        float x = Math.Min(width * .15f, Math.Max(48 * scale, button.X - 64 * scale)) - offset;
        DrawTestplayArrow(c, x, button.Y + button.Height / 2, false, false, opacity);
        DrawTestplayArrow(c, width - x, button.Y + button.Height / 2, true, false, opacity);
    }

    private void DrawTestplayWarningArrows(ICanvas c)
    {
        if (TestplayPaused && testplayResumeAt is null) return;
        double until = testplayGameplayStart + 2000 - playhead;
        foreach (var period in breakPeriods)
            if (playhead >= period.StartMs && playhead <= period.EndMs) { until = period.EndMs - playhead; break; }
        if (testplayResumeAt is double resume) until = resume - TestplayRealtime;
        // Stable video calibration: seven 100 ms flashes, 100 ms apart,
        // starting about 1450 ms before the first object / break end.
        double elapsed = 1450 - until;
        if (elapsed < 0 || elapsed >= 1300 || elapsed % 200 >= 100) return;
        for (int row = 0; row < 2; row++)
        {
            float y = height * (row == 0 ? .14f : .86f);
            DrawTestplayArrow(c, width * .09f, y, false, true, 1);
            DrawTestplayArrow(c, width * .91f, y, true, true, 1);
        }
    }

    private void DrawTestplayArrow(ICanvas c, float x, float y, bool left, bool warning, float opacity)
    {
        var specific = skin?.MenuTexture(warning ? "arrow-warning" : "arrow-pause");
        var texture = specific ?? skin?.MenuTexture("play-warningarrow");
        uint tint = specific is not null ? 0xFFFFFFu : warning ? 0xFF2020u : 0x168AFFu;
        float scale = height / 768f;
        if (texture is not null)
        {
            float w = texture.PixelWidth / (float)texture.Density * scale, h = texture.PixelHeight / (float)texture.Density * scale;
            if (c.CatcherImage(texture.FilePath, new(x - w / 2, y - h / 2, w, h), tint, opacity, false, left)) return;
        }
        float direction = left ? -1 : 1, size = 28 * scale;
        c.Line(x - direction * size, y, x + direction * size, y, tint, 10 * scale, opacity);
        c.Line(x + direction * size, y, x, y - size, tint, 10 * scale, opacity);
        c.Line(x + direction * size, y, x, y + size, tint, 10 * scale, opacity);
    }
}
