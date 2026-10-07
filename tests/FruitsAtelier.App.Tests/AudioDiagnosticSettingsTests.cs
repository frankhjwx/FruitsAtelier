using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class AudioDiagnosticSettingsTests
{
    internal static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (bool library in new[] { false, true })
            foreach (var size in new[] { (760, 580), (1440, 900) })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false); ui.Resize(size.Item1, size.Item2);
                string root = Path.GetFullPath(Path.Combine("artifacts/tests/audio-settings", Guid.NewGuid().ToString("N")));
                ui.View.LibrarySettings.Workspace = root;
                ui.View.LibrarySettings.OsuRoot = "";
                ui.View.SupportsAudioDiagnostics = true;
                ui.View.AudioDiagnosticStatus = () => "audioDiagnostic.active";
                int markers = 0;
                ui.View.RequestAudioDiagnosticMarker = () => markers++;
                var export = new TaskCompletionSource<string>();
                int exports = 0;
                ui.View.RequestAudioDiagnosticExport = () => { exports++; return export.Task; };
                ui.Key(119, true, true); ui.Key(119, true, true); ui.View.KeyUp(119);
                if (markers != 1) throw new Exception("Diagnostic marker shortcut repeats while held.");
                ui.Key(119, true, true); ui.View.KeyUp(119);
                if (markers != 2) throw new Exception("Diagnostic marker shortcut did not rearm.");
                markers = 0;
                if (library) ui.View.ShowLibrary();
                var before = ui.View.Document.DeepClone();
                ui.View.OpenSettings(); ui.Paint();
                ui.ClickText(L.Get("settings.audio"));
                void Reveal(string text)
                {
                    var rect = ui.View.SettingsBounds;
                    for (int i = 0; i < 30; i++)
                    {
                        var label = ui.Canvas.Texts.Single(t => t.Value == text);
                        if (label.Y >= rect.Y + 120 && label.Y + 30 < rect.Bottom - 92) return;
                        ui.View.Wheel(rect.X + 250, rect.Y + 180, label.Y < rect.Y + 120 ? 120 : -120, false);
                        ui.Paint();
                    }
                    throw new Exception("Audio diagnostic control is unreachable: " + text);
                }
                Reveal(L.Get("audioDiagnostic.off")); ui.ClickText(L.Get("audioDiagnostic.off"));
                if (ui.View.LibrarySettings.AudioDiagnostics) throw new Exception("Diagnostic draft applied before Apply.");
                string profile = L.Get("audioDiagnostic.profile", "event-10") + " ▾";
                Reveal(profile); ui.ClickText(profile); ui.ClickText(L.Get("audioDiagnostic.poll-50"));
                Reveal(L.Get("audioDiagnostic.framesOff")); ui.ClickText(L.Get("audioDiagnostic.framesOff"));
                string config = Path.Combine(root, "settings.json");
                ui.View.ApplySettings(config);
                var loaded = LibrarySettings.Load(config);
                if (!loaded.AudioDiagnostics || !loaded.AudioDiagnosticFrames || loaded.AudioDiagnosticProfile != "poll-50")
                    throw new Exception("Audio diagnostic preferences did not survive reload.");
                Reveal(L.Get("audioDiagnostic.marker")); ui.ClickText(L.Get("audioDiagnostic.marker"));
                if (markers != 1) throw new Exception("Marker was not dispatched.");
                Reveal(L.Get("audioDiagnostic.export")); ui.ClickText(L.Get("audioDiagnostic.export"));
                ui.Paint(); ui.Paint();
                if (exports != 1 || !ui.Canvas.Texts.Any(t => t.Value == L.Get("audioDiagnostic.exporting")))
                    throw new Exception("Export did not remain asynchronous.");
                export.SetException(new IOException("injected export failure")); ui.Paint();
                if (!ui.Canvas.Texts.Any(t => t.Value.Contains("injected export failure")))
                    throw new Exception("Export failure was not presented.");
                ui.Key(27);
                if (!before.ContentEquals(ui.View.Document) || ui.View.IsDirty || ui.View.LibraryVisible != library)
                    throw new Exception("Audio diagnostics changed document content or destination.");
                loaded.AudioDiagnosticProfile = "unknown";
                if (loaded.AudioDiagnosticProfile != "event-10") throw new Exception("Invalid output profile did not fall back safely.");
            }
        }
        finally { L.SetLanguage(language); }
    }
}
