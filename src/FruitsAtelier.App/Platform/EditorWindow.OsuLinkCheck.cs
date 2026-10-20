using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Platform;

internal sealed partial class EditorWindow
{
    private void CheckOsuLinks()
    {
        string language = L.Language;
        string folder = Path.Combine(Artifacts, "tests", "osu-links", "native"); Directory.CreateDirectory(folder);
        var read = view.ReadOsuLinkSettings; var request = view.RequestSelectOsuLinkFallback; var readApps = view.ReadOsuLinkApplications; var clear = view.RequestClearOsuLinkBinding;
        var bind = view.RequestBindOsuLinks;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            foreach (int dpi in new[] { 96, 144, 192 })
            foreach (var size in new[] { (980, 620), (1440, 900) })
            {
                L.SetLanguage(locale); canvas!.Resize(size.Item1 * dpi / 96, size.Item2 * dpi / 96, dpi);
                var document = DemoMap.Create(); view.LoadDocument(document); view.CloseLibrary();
                var apps = OsuLinkApplications.Discover(Path.Combine(folder, "icons"));
                if (apps.Count == 0) apps = [new("test.osu", "osu!", Path.Combine(AppContext.BaseDirectory, "assets", "branding", "mark.png"))];
                File.WriteAllText(Path.Combine(folder, "applications.json"), System.Text.Json.JsonSerializer.Serialize(apps));
                string selectedId = apps[0].Id;
                view.ReadOsuLinkSettings = () => (selectedId, true, apps.Any(a => a.Id == selectedId));
                view.ReadOsuLinkApplications = () => apps;
                string? chosen = null; view.RequestSelectOsuLinkFallback = id => { chosen = selectedId = id; };
                bool cleared = false; view.RequestClearOsuLinkBinding = () => cleared = true;
                view.OpenSettings(); Draw();
                var r = view.SettingsBounds;
                Click(r.X + 40, r.Y + 78 + 7 * 48 + 19); Draw("settings");
                var choose = view.OsuLinkTargetButtonBounds;
                Click(choose.X + 20, choose.Y + 16); Draw("applications");
                var appRow = view.OsuLinkFallbackButtonBounds;
                Click(appRow.X + 20, appRow.Y + (apps.Count > 1 ? 56 : 0) + 16); Draw("pending");
                if (chosen is not null) throw new InvalidOperationException("Application selection saved before confirmation.");
                var confirm = view.OsuLinkConfirmButtonBounds;
                Click(confirm.X + 20, confirm.Y + 16); Draw("selected");
                var clearBounds = view.OsuLinkClearButtonBounds;
                Click(clearBounds.X + 20, clearBounds.Y + 16);
                if (!cleared) throw new InvalidOperationException("Clear association action was not reached.");
                bool rebound = false; view.RequestBindOsuLinks = () => rebound = true;
                view.ReadOsuLinkSettings = () => ("", false, false);
                view.RefreshOsuLinkSettings(); Draw("unbound");
                Click(clearBounds.X + 20, clearBounds.Y + 16);
                if (!rebound || view.OsuLinkFallbackRequired) throw new InvalidOperationException("Unbound FA did not offer rebinding without fallback setup.");
                view.ReadOsuLinkSettings = () => (selectedId, true, apps.Any(a => a.Id == selectedId));
                view.RefreshOsuLinkSettings(); Draw();
                if (chosen is null) throw new InvalidOperationException("Native osu! target picker action was not reached.");
                double position = view.PlayheadMs;
                if (view.HandleEditorLink("osu://edit/00:13:247%20(2,3)") || view.PlayheadMs != position || view.IsDirty)
                    throw new InvalidOperationException("Settings allowed an editor timestamp to navigate.");
                Draw("notice");
                selectedId = ""; view.RefreshOsuLinkSettings(); Draw("required");
                chosen = null;
                var required = view.OsuLinkFallbackButtonBounds;
                Click(required.X + 20, required.Y + 16); Draw();
                confirm = view.OsuLinkConfirmButtonBounds;
                Click(confirm.X + 20, confirm.Y + 16);
                if (chosen is null || view.OsuLinkFallbackRequired) throw new InvalidOperationException("Required fallback setup did not accept a registered application.");
                view.KeyDown(27, false, false); Draw();
                if (!view.HandleEditorLink("osu://edit/00:01:500") || view.PlayheadMs != 1500 || !view.Document.ContentEquals(document) || view.IsDirty)
                    throw new InvalidOperationException("Native timestamp navigation changed content or failed to seek.");
                FazFileAssociation.PendingLinks.Enqueue("osu://edit/00:02:000");
                HandleMessage(hwnd, 0x000F, 0, 0);
                if (view.PlayheadMs != 2000 || !FazFileAssociation.PendingLinks.IsEmpty)
                    throw new InvalidOperationException("Paint polling did not handle editor activation without a timer message.");

                void Draw(string? name = null)
                {
                    canvas.Begin(); view.Render(canvas, size.Item1, size.Item2);
                    canvas.End(capture: name is null ? null : () =>
                    {
                        var frame = canvas.CapturePixels();
                        SaveFrame(frame.Width, frame.Height, frame.Pixels, Path.Combine(folder, $"{name}-{locale}-{size.Item1}-{dpi}.bmp"));
                    });
                }
                void Click(float x, float y) { view.PointerDown(x, y, 0, false, false); view.PointerUp(x, y, 0); }
            }
            AppLog.Write("osu! link native settings, notice and navigation checks passed.");
        }
        finally { L.SetLanguage(language); view.ReadOsuLinkSettings = read; view.RequestSelectOsuLinkFallback = request; view.ReadOsuLinkApplications = readApps; view.RequestClearOsuLinkBinding = clear; view.RequestBindOsuLinks = bind; }
    }
}
