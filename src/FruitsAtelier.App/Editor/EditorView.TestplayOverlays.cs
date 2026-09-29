using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private const double TestplayMenuFadeMs = 600;
    private const double TestplayMenuFadeInMs = 300;
    private double testplayGameplayStart, testplayMenuStartedAt;
    private double? testplayResumeAt;
    private float testplayMenuOpacityFrom, testplayMenuOpacityTarget;
    private double testplayMenuOpacityAt;
    private float TestplayMenuOpacity
    {
        get
        {
            float t = (float)Math.Clamp((TestplayRealtime - testplayMenuOpacityAt) /
                (testplayMenuOpacityTarget == 1 ? TestplayMenuFadeInMs : TestplayMenuFadeMs), 0, 1);
            return testplayMenuOpacityFrom + (testplayMenuOpacityTarget - testplayMenuOpacityFrom) * t;
        }
    }
    private void FadeTestplayMenu(float target, bool opening = false)
    {
        testplayMenuOpacityFrom = opening ? 0 : TestplayMenuOpacity;
        testplayMenuOpacityTarget = target;
        testplayMenuOpacityAt = TestplayRealtime;
    }
    private int testplayMenuSelection;
    private int testplayHoveredMenu = -1;
    private readonly Rect[] testplayMenuBounds = new Rect[3];
    private readonly Dictionary<string, Hitsound> testplayMenuSounds = [];
    public Action<Hitsound?>? RequestTestplayMenuLoop { get; set; }
    public bool TestplaySkipVisible => IsTestplaying && !TestplayPaused && playhead < testplayGameplayStart - 1000;
    public bool TestplayPauseMenuVisible => TestplayPaused && testplayResumeAt is null;
    public Rect TestplaySkipBounds { get; private set; }
    private static readonly string[] pauseComponents = ["pause-continue", "pause-retry", "pause-back"];
    private static readonly string[] pauseLabels = ["testplay.continue", "testplay.retry", "testplay.quit"];

    private void PrepareTestplayMenuSounds()
    {
        testplayMenuStartedAt = TestplayRealtime;
        testplayMenuSounds.Clear();
        foreach (string name in pauseComponents.SelectMany(n => new[] { n + "-click", n + "-hover" }).Concat(["menuhit", "menuclick", "menuback", "pause-loop"]))
        {
            string? path = LibrarySettings.UseSkinSounds ? skin?.MenuSound(name) : null;
            path ??= HitsoundDefaults.FindInterface(name);
            var sound = new Hitsound(CatchObjectKind.Fruit, path, 1, name);
            testplayMenuSounds[name] = sound;
            RequestPrepareHitsound?.Invoke(sound);
        }
    }

    private void SetTestplayPauseLoop(bool play)
        => RequestTestplayMenuLoop?.Invoke(play ? testplayMenuSounds.GetValueOrDefault("pause-loop") : null);

    private void PlayTestplayMenuSound(string name, string fallback = "menuhit")
    {
        if (testplayMenuSounds.TryGetValue(name, out var sound) || testplayMenuSounds.TryGetValue(fallback, out sound))
            RequestAuditionHitsound?.Invoke(sound);
    }

    private void SkipTestplayIntro()
    {
        if (!TestplaySkipVisible || testplay?.SkipIntro(testplayGameplayStart - 1000) != true) return;
        if (testplayWithAudio) RequestSeek?.Invoke(testplayGameplayStart - 1000);
        PlayTestplayMenuSound("menuhit");
        AdvanceTestplay();
    }

    private void ActivateTestplayMenu(int item)
    {
        if (!TestplayPauseMenuVisible) return;
        switch (item)
        {
            case 0: ToggleTestplayPause(); break;
            case 1:
                RestartTestplay();
                break;
            case 2: StopTestplay(); break;
        }
        PlayTestplayMenuSound(pauseComponents[item] + "-click");
    }

    private void RestartTestplay()
    {
        bool autoplay = TestplayAutoplay;
        ResetTestplayPointer();
        SetTestplayPauseLoop(false);
        testplay?.Cancel();
        testplayDriver?.Dispose(); testplayDriver = null;
        testplay = null; testplayFrame = null;
        testplayResumeAt = null;
        testplayAutoNotice = null;
        testplayTabHeld = testplaySpeedHeld = testplayPauseHeld = testplayBookmarkHeld = false;
        testplayMenuStartedAt = TestplayRealtime;
        PrepareTestplayMenuSounds();
        ResetHitsounds();
        playhead = testplayStart;
        BeginTestplay(new CatchTestplay(PreviewObjects(), PreviewCircleSize, testplayStart), audioAlreadyPlaying: false);
        if (autoplay) testplay?.ToggleAutoplay();
        AdvanceTestplay();
    }

    private void DrawTestplayOverlays(ICanvas c)
    {
        float opacity = TestplayMenuOpacity;
        if (TestplayPaused)
        {
            c.Fill(new(0, 0, width, height), 0, opacity: .75f * opacity);
            float scale = height / 768f;
            if (skin?.MenuTexture("pause-overlay") is { } overlay)
            {
                float w = overlay.PixelWidth / (float)overlay.Density * scale, h = overlay.PixelHeight / (float)overlay.Density * scale;
                c.Image(overlay.FilePath, new((width - w) / 2, (height - h) / 2, w, h), opacity: opacity);
            }
            else
            {
                string title = L.Get("testplay.paused");
                c.TextOpacity(title, width / 2 - c.MeasureText(title, 28) / 2, height * .12f, 28, Foreground, width, true, opacity);
            }
            for (int i = 0; i < pauseComponents.Length; i++)
            {
                int item = i;
                testplayMenuBounds[i] = DrawTestplaySkinButton(c, pauseComponents[i], L.Get(pauseLabels[i]), width / 2,
                    (224 + i * 176) * scale, false, () => ActivateTestplayMenu(item), testplayMenuSelection == i && testplayKeyboardSelection, opacity);
            }
            if (testplayKeyboardSelection) DrawTestplaySelectionArrows(c, opacity);
            if (testplayResumeAt is null) DrawBackgroundDimSetting(c, new(Math.Max(8, width / 2 - 160), height - 58, Math.Min(320, width - 16), 38), false, opacity);
        }
        else if (TestplaySkipVisible)
            TestplaySkipBounds = DrawTestplaySkinButton(c, "play-skip", L.Get("testplay.skip"), width, height,
                true, SkipTestplayIntro, false);
        DrawTestplayWarningArrows(c);
    }

    private void TestplayPointerMove(float x, float y)
    {
        MoveTestplayCursor(x, y);
        if (!TestplayPauseMenuVisible) return;
        testplayKeyboardSelection = false;
        int hovered = -1;
        for (int i = 0; i < testplayMenuBounds.Length; i++)
            if (testplayMenuBounds[i].Contains(x, y)) { hovered = i; break; }
        if (hovered >= 0)
        {
            testplayMenuSelection = hovered;
            if (testplayHoveredMenu != hovered) PlayTestplayMenuSound(pauseComponents[hovered] + "-hover", "menuclick");
        }
        testplayHoveredMenu = hovered;
    }

    private Rect DrawTestplaySkinButton(ICanvas c, string component, string label, float x, float y,
        bool bottomRight, Action action, bool selected, float opacity = 1)
    {
        float scale = height / 768f;
        var texture = skin?.MenuTexture(component, TestplayRealtime - testplayMenuStartedAt);
        float w = texture is null ? (bottomRight ? 180 : 320) * scale : texture.PixelWidth / (float)texture.Density * scale;
        float h = texture is null ? 64 * scale : texture.PixelHeight / (float)texture.Density * scale;
        var bounds = new Rect(x - (bottomRight ? w : w / 2), y - (bottomRight ? h : h / 2), w, h);
        bool hovered = (bottomRight || !testplayKeyboardSelection) && bounds.Contains(mouseX, mouseY);
        int animationIndex = bottomRight ? 3 : Array.IndexOf(pauseComponents, component);
        float zoom = TestplayButtonScale(animationIndex, hovered || selected);
        var drawn = new Rect(bounds.X - w * (zoom - 1) / 2, bounds.Y - h * (zoom - 1) / 2, w * zoom, h * zoom);
        if (texture is null || !c.Image(texture.FilePath, drawn, selected || hovered ? 0xFFFFFFu : 0xDDDDDDu, opacity: opacity))
        {
            c.Fill(drawn, selected || hovered ? 0x356963u : 0x26343Du, 8, opacity);
            c.TextOpacity(label, drawn.X + (drawn.Width - c.MeasureText(label, 18 * zoom)) / 2,
                drawn.Y + (drawn.Height - 22 * zoom) / 2, 18 * zoom, Foreground, drawn.Width, false, opacity);
        }
        if (testplayResumeAt is null) hits.Add(new(bounds, action, true));
        return bounds;
    }

    private void TestplayPointerDown(float x, float y, int button)
    {
        mouseX = x; mouseY = y;
        testplayCursorPressedAt = TestplayRealtime;
        testplayCursorPressed = true;
        if (!TestplayPauseMenuVisible && BeginVolumePopoverPointer(x, y, button)) return;
        if (button != 0 || testplayResumeAt is not null) return;
        for (int i = hits.Count - 1; i >= 0; i--)
            if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); break; }
    }

    private bool TestplayMenuKey(int key)
    {
        if (!TestplayPauseMenuVisible) return testplayResumeAt is not null;
        if (key is 38 or 40)
        {
            testplayKeyboardSelection = true;
            testplaySelectionAt = TestplayRealtime;
            testplayMenuSelection = (testplayMenuSelection + (key == 38 ? 2 : 1)) % 3;
            PlayTestplayMenuSound(pauseComponents[testplayMenuSelection] + "-hover", "menuclick");
        }
        else if (key == 13) ActivateTestplayMenu(testplayMenuSelection);
        return key is 38 or 40 or 13 or 32;
    }
}
