using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private CatchTestplaySession? testplay;
    private CatchTestplayFrame? testplayFrame;
    private IDisposable? testplayDriver;
    private bool testplayEscapeConsumed;
    private bool testplayTabHeld;
    private bool testplayPauseHeld;
    private bool testplayBookmarkHeld;
    public bool TestplayPaused => testplay?.Paused ?? false;
    public bool TestplayAutoplay => testplay?.Autoplay ?? false;
    private double TestplayRealtime => timeProvider.GetTimestamp() * 1000d / timeProvider.TimestampFrequency;
    private double testplayStart;
    private bool testplayWithAudio;
    private double transportSampleAt, transportSamplePosition, transportSampleLeadMs;
    public bool IsTestplaying => testplay is not null;
    public int TestplayCombo => testplay?.Combo ?? 0;
    public double TestplayCatcherX => testplay?.X ?? 256;
    public Rect TestplayButtonBounds { get; private set; }
    public Action? RequestPausePlayback { get; set; }
    public Func<CatchTestplaySession, IDisposable>? RequestRunTestplay { get; set; }

    public void StartTestplay()
    {
        if (IsTestplaying || !HasEditorProject || LibraryVisible || ExportVisible || ErrorVisible ||
            DiscardConfirmationVisible || SliderDialogVisible || TimeJumpVisible || StreamDialogVisible || VolumeDialogVisible || DistanceSnapDialogVisible || IsEditingText ||
            drag != DragKind.None || draftTrack != Guid.Empty || draftBanana != Guid.Empty || AudioLoading) return;
        EnsureConversion();
        var session = new CatchTestplay(PreviewObjects(), PreviewCircleSize, playhead);
        if (session.Finished || AudioReady && playhead >= AudioDurationMs)
        { StatusMessage = L.Get("testplay.noNotes"); return; }
        menu = -1; contextItems.Clear(); languageMenuOpen = false;
        testplayStart = playhead;
        testplayTabHeld = false;
        testplayPauseHeld = false;
        testplayBookmarkHeld = false;
        testplayWithAudio = AudioReady;
        comboCurrent = comboPrevious = 0; comboChangedAt = double.NegativeInfinity;
        ResetHitsounds();
        var resolver = new HitsoundResolver(Document, PreviewObjects(), HitsoundSkinFolders);
        var sounds = PreviewObjects().ToDictionary(item => (item.SourceId, item.EventIndex), resolver.Resolve);
        foreach (var sound in sounds.Values.SelectMany(s => s).Distinct()) RequestPrepareHitsound?.Invoke(sound);
        var playSound = RequestHitsound;
        var scheduleSound = OperatingSystem.IsWindows() && AudioReady ? RequestScheduleHitsound : null;
        double now = TestplayRealtime;
        double clockStart = AudioPlaying ? Math.Min(AudioDurationMs,
            transportSamplePosition + Math.Max(0, now - transportSampleAt) * PlaybackSpeed
            + CatchTestplaySession.LiveHitsoundLead(transportSampleLeadMs, PlaybackSpeed)) : playhead;
        var clock = new CatchTestplayClock(clockStart, PlaybackSpeed, now, AudioReady && !AudioPlaying);
        if (AudioReady) clock.Synchronize(clockStart, now, now);
        testplay = new(session, clock, playhead, AudioReady, AudioPlaying, LibrarySettings.TestplayLeftKey,
            LibrarySettings.TestplayRightKey, LibrarySettings.TestplayDashKey, timeProvider, PreviewCircleSize, previewComboEnds,
            item =>
            {
                foreach (var sound in sounds[(item.SourceId, item.EventIndex)])
                {
                    if (scheduleSound is not null) scheduleSound(sound, item.TimeMs);
                    else playSound?.Invoke(sound);
                }
            });
        testplayFrame = testplay.Capture();
        if (AudioReady && !AudioPlaying) { RequestSeek?.Invoke(playhead); RequestTogglePlayback?.Invoke(); }
        try { if (testplay is not null) testplayDriver = RequestRunTestplay?.Invoke(testplay); }
        catch { StopTestplay(); throw; }
    }

    public void StopTestplay(bool atCurrentPosition = false)
    {
        if (!IsTestplaying) return;
        double returnTime = atCurrentPosition ? testplay!.TransportPosition : testplayStart;
        testplay!.Cancel();
        testplayDriver?.Dispose(); testplayDriver = null;
        testplay = null; testplayFrame = null;
        testplayBookmarkHeld = false;
        if (testplayWithAudio)
        {
            if (RequestPausePlayback is not null) RequestPausePlayback();
            else if (AudioPlaying) RequestTogglePlayback?.Invoke();
        }
        SeekTo(returnTime);
        StatusMessage = L.Get("testplay.returned");
    }

    private void ToggleTestplayPause()
    {
        if (testplay is null) return;
        double time = testplay.TogglePause();
        if (testplayWithAudio)
        {
            if (testplay.Paused) RequestPausePlayback?.Invoke();
            else { RequestSeek?.Invoke(time); RequestTogglePlayback?.Invoke(); }
        }
        AdvanceTestplay();
    }

    private void AdvanceTestplay()
    {
        if (testplay is null) return;
        if (testplayDriver is null) testplay.Tick();
        testplayFrame = testplay.Capture();
        playhead = testplayFrame.TimeMs;
        foreach (var change in testplayFrame.ComboChanges)
        {
            var item = change.Object;
            comboBreakAlpha = ComboOpacity(item.TimeMs);
            comboPrevious = comboCurrent; comboCurrent = change.Combo; comboChangedAt = item.TimeMs;
            comboColour = ObjectColour(item);
        }
        if (testplayFrame.Ended)
        {
            var error = testplayFrame.Error;
            StopTestplay();
            if (error is not null) ShowError(L.Get("window.operationFailed", error.Message));
        }
    }

    public void KeyUp(int virtualKey)
    {
        if (virtualKey is 17 or 162 or 163) placementCtrl = false;
        if (virtualKey == 27) testplayEscapeConsumed = false;
        if (virtualKey == 9) testplayTabHeld = false;
        if (virtualKey == 80) testplayPauseHeld = false;
        if (virtualKey == 66) testplayBookmarkHeld = false;
        if (testplayDriver is null) testplay?.SetKey(virtualKey, false);
        if (IsTestplaying) AdvanceTestplay();
    }

    private void DrawTestplay(ICanvas c)
    {
        c.Fill(new(0, 0, width, height), Background);
        float stageHeight = Math.Max(1, Math.Min(height, width * .75f));
        var stage = new Rect((width - stageHeight * 4 / 3) / 2, (height - stageHeight) / 2,
            stageHeight * 4 / 3, stageHeight);
        c.Fill(stage, 0x151A22); c.Clip(stage);
        float fieldWidth = stage.Width * .8f, left = stage.X + stage.Width * .1f;
        float catchY = stage.Y + stage.Height * .15f + 340 * fieldWidth / 512;
        double speed = CatchScrollTiming.PixelsPerMs(PreviewApproachRate, fieldWidth);
        double ahead = (catchY - stage.Y + CatchSize.FruitDiameter(PreviewCircleSize) * fieldWidth / 512) / speed;
        var frame = testplayFrame!;
        for (int i = frame.JudgedCount; i < previewObjects.Count && previewObjects[i].TimeMs <= playhead + ahead; i++)
        {
            var item = previewObjects[i];
            DrawCatchObject(c, item, left + (float)item.X * fieldWidth / 512,
                catchY - (float)((item.TimeMs - playhead) * speed), fieldWidth, circleSize: PreviewCircleSize, hyperStarts: previewHyperdash, animated: true);
        }
        foreach (var item in frame.MissedObjects)
            DrawCatchObject(c, item, left + (float)item.X * fieldWidth / 512,
                catchY + (float)((playhead - item.TimeMs) * speed), fieldWidth,
                (float)Math.Clamp(1 - (playhead - item.TimeMs) / CatchTestplay.MissLifetimeMs, 0, 1),
                circleSize: PreviewCircleSize, hyperStarts: previewHyperdash, animated: true);
        float x = left + (float)frame.X * fieldWidth / 512;
        uint tint = frame.HyperDashing ? skin?.HyperDashColour ?? 0xFF0000 : 0xFFFFFF;
        foreach (var trail in frame.Trails)
            DrawCatcherTrail(c, trail, left, fieldWidth, catchY);
        DrawCatcherBody(c, x, catchY, fieldWidth, tint, 1, false, frame.FacingLeft);
        DrawCaughtPlate(c, frame.Plate, left, fieldWidth, catchY);
        DrawTestplayCombo(c, x, catchY - 175 * fieldWidth / 512, fieldWidth / 512);
        c.Unclip();
        string[] hints = ["testplay.hintAutoplay", "testplay.hintPause", "testplay.hintBookmark", "testplay.hintQuickExit", "testplay.hintCurrentExit"];
        for (int i = 0; i < hints.Length; i++)
            c.Text(L.Get(hints[i]), 12, 12 + i * 20, 13, 0xD6E5B5, Math.Max(100, width - 24));
        if (TestplayPaused) c.Text(L.Get("testplay.paused"), 12, 118, 15, Accent, 250, true);
    }

    // ppy/osu 48c4800e: LegacyCatchComboCounter, LegacyRollingCounter and CatcherArea.
    // MIT licence retained in Core/Gameplay/LICENSE.osu.txt.
    private int comboCurrent, comboPrevious;
    private double comboChangedAt = double.NegativeInfinity;
    private float comboBreakAlpha;
    private uint comboColour;
    private float ComboOpacity(double time)
    {
        float age = (float)(time - comboChangedAt);
        return comboCurrent == 0 ? comboBreakAlpha * MathF.Pow(1 - Math.Clamp(age / 400, 0, 1), 2)
            : 1 - Math.Clamp((age - 1000) / 300, 0, 1);
    }
    private void DrawTestplayCombo(ICanvas c, float x, float y, float fieldScale)
    {
        float age = (float)(playhead - comboChangedAt), alpha = ComboOpacity(playhead);
        if (alpha <= 0) return;
        if (comboCurrent == 0)
        {
            float remaining = 1 - Math.Clamp(age / Math.Max(1, comboPrevious * 75f), 0, 1);
            Draw((int)Math.Round(comboPrevious * remaining * remaining), 1, 0xFFFFFF, alpha, false);
            return;
        }
        float burst = Math.Clamp(age / 400, 0, 1);
        Draw(comboCurrent, 1.5f + .4f * (1 - (1 - burst) * (1 - burst)), comboColour, alpha * (1 - burst), true);
        float scale = age < 250 ? .8f + .7f * MathF.Pow(1 - age / 250, 2)
            : age < 310 ? 1 + .1f * (age - 250) / 60 : age < 340 ? 1.1f - .1f * (age - 310) / 30 : 1;
        Draw(age < 250 ? comboPrevious : comboCurrent, scale, 0xFFFFFF, alpha, false);

        void Draw(int value, float scale, uint colour, float opacity, bool additive)
        {
            if (opacity <= 0) return;
            scale *= .8f * fieldScale;
            if (skin?.DrawCombo(c, value, x, y, scale, colour, opacity, additive) == true) return;
            string text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            float size = 40 * scale;
            uint faded = 0;
            for (int shift = 0; shift <= 16; shift += 8)
                faded |= (uint)(((colour >> shift) & 255) * opacity + ((0x151A22 >> shift) & 255) * (1 - opacity)) << shift;
            c.Text(text, x - c.MeasureText(text, size) / 2, y - size / 2, size, faded);
        }
    }

    private int bindingCapture = -1;
    private int[] draftTestplayKeys = [37, 39, 16];
    public bool CapturingTestplayKey => bindingCapture >= 0 && LibraryVisible && librarySettingsOpen;
    // Esc, Tab, F1 and F2 belong to testplay navigation; OS/media keys cannot reliably reach both hosts.
    private static bool IsBindingKey(int key) => key is >= 65 and <= 90 or >= 48 and <= 57 or >= 33 and <= 40
        or >= 96 and <= 111 or >= 114 and <= 135 or >= 186 and <= 192 or >= 219 and <= 223
        or 8 or 12 or 13 or 16 or 17 or 18 or 20 or 32 or 45 or 46 or 144 or 145 or 226;
    private static string KeyName(int key) => key switch
    {
        37 => "←", 38 => "↑", 39 => "→", 40 => "↓", 16 => "Shift", 32 => "Space",
        8 => "Backspace", 12 => "Clear", 13 => "Enter", 17 => "Ctrl", 18 => "Alt", 20 => "Caps Lock",
        33 => "Page Up", 34 => "Page Down", 35 => "End", 36 => "Home", 45 => "Insert", 46 => "Delete",
        >= 96 and <= 105 => $"Num {key - 96}",
        106 => "Num *", 107 => "Num +", 108 => "Num Separator", 109 => "Num -", 110 => "Num .", 111 => "Num /",
        >= 112 and <= 135 => $"F{key - 111}", 144 => "Num Lock", 145 => "Scroll Lock",
        186 => ";", 187 => "=", 188 => ",", 189 => "-", 190 => ".", 191 => "/", 192 => "`",
        219 => "[", 220 => "\\", 221 => "]", 222 => "'", 223 => "OEM 8", 226 => "OEM 102",
        _ => ((char)key).ToString()
    };
    private void DrawTestplayBindings(ICanvas c)
    {
        string[] labels = ["testplay.left", "testplay.right", "testplay.dash"];
        float cell = Math.Min(220, (width - SettingsContentX - 32) / 3);
        for (int i = 0; i < 3; i++)
        {
            int action = i;
            c.Text(L.Get(labels[i]), SettingsContentX + i * cell, 160, 12, Muted, cell - 8);
            Button(c, new(SettingsContentX + i * cell, 188, cell - 12, 30), bindingCapture == i ? L.Get("testplay.pressKey") : KeyName(draftTestplayKeys[i]),
                () => { libraryField = -1; bindingCapture = action; }, bindingCapture == i);
        }
    }

    private void CaptureTestplayKey(int key)
    {
        if (key == 27) { bindingCapture = -1; return; }
        if (!IsBindingKey(key)) return;
        int previous = Array.IndexOf(draftTestplayKeys, key);
        if (previous >= 0) draftTestplayKeys[previous] = draftTestplayKeys[bindingCapture];
        draftTestplayKeys[bindingCapture] = key;
        bindingCapture = -1;
    }
}
