using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

static class AcknowledgementsTests
{
    public static void Run()
    {
        string language = L.Language;
        string root = Path.GetFullPath("artifacts/tests/acknowledgements/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path = Path.Combine(root, "accepted.json");
            Check(!AcknowledgementsPreference.IsAccepted(path), "missing acknowledgement requires consent");
            foreach (string value in new[] { "{", "null", "{}", "{\"AcceptedVersion\":0}", "{\"AcceptedVersion\":1}", "{\"AcceptedVersion\":999}" })
            {
                File.WriteAllText(path, value);
                Check(!AcknowledgementsPreference.IsAccepted(path), "damaged or different revisions require consent");
            }
            File.Delete(path);
            foreach (string locale in L.AvailableLanguages)
            {
                L.SetLanguage(locale);
                Check(L.Get("acknowledgements.title") == "Acknowledgements" && L.Get("acknowledgements.confirm") == "Confirm"
                    && L.Get("acknowledgements.agree") == "I have read and agree to the acknowledgements above.", "notice controls remain English");
                Check(L.Get("acknowledgements.intro").Contains("Yumeno Himiko", StringComparison.Ordinal), "notice identifies the hobby project's author");
                foreach (string section in new[] { "intro", "development", "editing", "privacy", "ranked", "descriptions" })
                {
                    string value = L.Get("acknowledgements." + section);
                    L.SetLanguage("en");
                    Check(value == L.Get("acknowledgements." + section), "notice body remains English");
                    L.SetLanguage(locale);
                }
            }

            foreach (double scale in new[] { 1d, 1.25, 1.5, 1.75, 2 })
            {
                var size = EditorView.AcknowledgementsWindowSize(1920, 1040, scale);
                Check(size.Width <= 820 && size.Height <= 740 && size.Width * scale <= 1920 * .85 && size.Height * scale <= 1040 * .85,
                    "acknowledgements fit a compact independent window at common DPI scales");
            }
            foreach (var size in new[] { (820, 740), (760, 580), (640, 440) })
            {
                L.SetLanguage("zh-CN");
                var view = new EditorView(false);
                var canvas = new RecordingCanvas();
                int continued = 0, closed = 0, opened = 0, accepted = 0;
                view.RequestClose = () => closed++;
                view.RequestOpen = () => opened++;
                view.RequestAcknowledgementsAccepted = () => accepted++;
                var before = view.Document.DeepClone();
                string settingsPath = Path.Combine(root, $"{size.Item1}.json");
                Check(view.BeginAcknowledgements(() => continued++, settingsPath), "unconfirmed launches show the notice");
                Paint();
                Check(!view.AcknowledgementsAgreed && view.AcknowledgementsVisible && !view.PrepareFileOperation(), "consent starts unchecked and blocks file operations");
                Check(!view.CanDropAudio && !view.CanDropLibraryFiles && !view.CanDropBackground, "all file drops are blocked");
                var confirm = canvas.Texts.Single(t => t.Value == "Confirm");
                Check(confirm.Color == 0x5B6777, "unchecked Confirm is grey");
                Click(view.AcknowledgementsConfirmBounds);
                view.KeyDown(79, true, false); view.TextInput('x'); view.PointerDoubleClick(200, 200, false, false);
                view.KeyDown(13, false, false);
                Check(continued == 0 && opened == 0 && accepted == 0 && !File.Exists(settingsPath), "unchecked clicks and shortcuts cannot accept or open the editor");
                float oldScroll = view.AcknowledgementsScroll;
                view.KeyDown(35, false, false); Paint();
                Check(view.AcknowledgementsScroll > oldScroll && canvas.Texts.Any(t => t.Value == L.Get("acknowledgements.descriptionsTitle")), "End reveals the final section");
                var checkbox = view.AcknowledgementsCheckboxBounds;
                var cancel = view.AcknowledgementsCancelBounds;
                var confirmBounds = view.AcknowledgementsConfirmBounds;
                Check(checkbox.Y > canvas.Clips.Single().Bottom && cancel.Y > checkbox.Bottom
                    && cancel.Y == confirmBounds.Y && confirmBounds.Bottom < size.Item2,
                    "scrolling text, consent and buttons occupy separate rows inside the window");
                view.KeyDown(36, false, false); Paint();
                Check(view.AcknowledgementsScroll == 0 && checkbox == view.AcknowledgementsCheckboxBounds, "Home scrolls content while consent remains fixed");
                view.Wheel(100, 100, -120, false); Paint();
                Click(checkbox);
                Check(view.AcknowledgementsAgreed, "clicking consent enables confirmation");
                Click(checkbox); Click(confirmBounds);
                Check(!view.AcknowledgementsAgreed && continued == 0, "unchecking disables confirmation again");
                Click(checkbox); Click(confirmBounds);
                Check(!view.AcknowledgementsVisible && continued == 1 && accepted == 1 && AcknowledgementsPreference.IsAccepted(settingsPath),
                    "explicit confirmation saves the current revision and continues once");
                var restarted = new EditorView(false);
                Check(!restarted.BeginAcknowledgements(() => throw new Exception("unexpected continuation"), settingsPath)
                    && !restarted.AcknowledgementsVisible, "accepted users are not prompted on restart");
                Check(before.ContentEquals(view.Document) && !view.IsDirty && L.Language == "zh-CN", "notice preserves content and language");

                void Paint() { canvas.Clear(); view.Render(canvas, size.Item1, size.Item2); }
                void Click(Rect bounds) { view.PointerDown(bounds.X + 8, bounds.Y + 8, 0, false, false); view.PointerUp(bounds.X + 8, bounds.Y + 8, 0); Paint(); }
            }

            var failure = new EditorView(false);
            string invalidPath = Path.Combine(root, "directory.json");
            Directory.CreateDirectory(invalidPath);
            int failedContinue = 0;
            failure.BeginAcknowledgements(() => failedContinue++, invalidPath);
            var failureCanvas = new RecordingCanvas();
            failure.Render(failureCanvas, 980, 620);
            ClickFailure(failure.AcknowledgementsCheckboxBounds); ClickFailure(failure.AcknowledgementsConfirmBounds);
            Check(failure.AcknowledgementsVisible && failedContinue == 0
                && failureCanvas.Texts.Any(t => t.Value == L.Get("acknowledgements.saveFailed")), "failed persistence keeps the editor blocked with an English error");

            foreach (bool escape in new[] { false, true })
            {
                var canceled = new EditorView(false);
                string canceledPath = Path.Combine(root, "cancel.json");
                int closed = 0, continued = 0;
                canceled.RequestClose = () => closed++;
                canceled.BeginAcknowledgements(() => continued++, canceledPath);
                canceled.Render(new RecordingCanvas(), 980, 620);
                if (escape) canceled.KeyDown(27, false, false);
                else { var r = canceled.AcknowledgementsCancelBounds; canceled.PointerDown(r.X + 8, r.Y + 8, 0, false, false); }
                Check(closed == 1 && continued == 0 && !File.Exists(canceledPath), "Cancel and Escape exit without persisting consent");
            }

            var startup = new EditorView(false);
            var preferences = new LibrarySettings { Workspace = Path.Combine(root, "workspace"), FirstRunSetupCompleted = true, SongVolume = 73 };
            startup.InitializeLibrary(true, preferences, requireAcknowledgements: true, acknowledgementsPath: path);
            Check(startup.AcknowledgementsVisible && !Directory.Exists(preferences.Workspace) && !startup.FirstRunSetupVisible,
                "existing setup preferences do not bypass consent or start background workspace work");
            var startupCanvas = new RecordingCanvas();
            startup.Render(startupCanvas, 980, 620);
            ClickStartup(startup.AcknowledgementsCheckboxBounds); ClickStartup(startup.AcknowledgementsConfirmBounds);
            Check(!startup.AcknowledgementsVisible && !startup.FirstRunSetupVisible && startup.LibraryVisible
                && startup.LibrarySettings.SongVolume == 73, "accepted existing users retain their setup and enter the library");
            startup.StopFileMonitoring();

            var newUser = new EditorView(false);
            string newUserPath = Path.Combine(root, "new-user.json");
            newUser.InitializeLibrary(true, new LibrarySettings { Workspace = Path.Combine(root, "new-workspace") },
                forceFirstRunSetup: true, requireAcknowledgements: true, acknowledgementsPath: newUserPath);
            Check(newUser.AcknowledgementsVisible && !newUser.FirstRunSetupVisible, "new users must acknowledge before setup starts");
            var newUserCanvas = new RecordingCanvas();
            newUser.Render(newUserCanvas, 820, 740);
            ClickNewUser(newUser.AcknowledgementsCheckboxBounds); ClickNewUser(newUser.AcknowledgementsConfirmBounds);
            Check(!newUser.AcknowledgementsVisible && newUser.FirstRunSetupVisible && newUser.FirstRunStep == 0,
                "confirming acknowledgements opens setup before the main editor for new users");
            newUser.CancelFirstRunSetup();

            var keyboard = new EditorView(false);
            int keyboardContinued = 0;
            keyboard.BeginAcknowledgements(() => keyboardContinued++, Path.Combine(root, "keyboard.json"));
            keyboard.Render(new RecordingCanvas(), 980, 620);
            keyboard.KeyDown(9, false, false); keyboard.KeyDown(32, false, false);
            keyboard.KeyDown(9, false, false); keyboard.KeyDown(9, false, false); keyboard.KeyDown(13, false, false);
            Check(keyboardContinued == 1, "keyboard users can explicitly agree and confirm through Tab navigation");

            void ClickFailure(Rect r)
            {
                failure.PointerDown(r.X + 8, r.Y + 8, 0, false, false); failure.PointerUp(r.X + 8, r.Y + 8, 0);
                failureCanvas.Clear(); failure.Render(failureCanvas, 980, 620);
            }
            void ClickStartup(Rect r)
            {
                startup.PointerDown(r.X + 8, r.Y + 8, 0, false, false); startup.PointerUp(r.X + 8, r.Y + 8, 0);
                startupCanvas.Clear(); startup.Render(startupCanvas, 980, 620);
            }
            void ClickNewUser(Rect r)
            {
                newUser.PointerDown(r.X + 8, r.Y + 8, 0, false, false); newUser.PointerUp(r.X + 8, r.Y + 8, 0);
                newUserCanvas.Clear(); newUser.Render(newUserCanvas, 820, 740);
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
