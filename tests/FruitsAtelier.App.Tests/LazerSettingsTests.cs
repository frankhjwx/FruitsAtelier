using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class LazerSettingsTests
{
    public static void Run()
    {
        string language = L.Language;
        string root = Path.GetFullPath(Path.Combine("artifacts/tests/lazer-settings", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "settings.json");
        File.WriteAllText(path, "{}");
        var legacy = LibrarySettings.Load(path);
        Check(!legacy.LazerEditing && legacy.CatchHitLighting == CatchHitLightingMode.FollowMode,
            "Existing settings must keep mapping mode and follow-mode lighting.");
        File.WriteAllText(path, "{\"CatchHitLighting\":123}");
        Check(LibrarySettings.Load(path).CatchHitLighting == CatchHitLightingMode.FollowMode,
            "Unknown lighting preferences must fall back to following the editing mode.");
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var ui = new Ui(false);
                var settings = ui.View.LibrarySettings;
                settings.Workspace = Path.Combine(root, locale);
                settings.OsuRoot = "";
                settings.PlaybackLineFromBottom = .63;
                settings.ReverseCanvasScroll = true;
                settings.CanvasZoom = .72;
                settings.CanvasTimeZoom = .84;
                settings.UniformCanvasScale = false;
                settings.ObjectTimelineScale = .43;
                settings.TestplayLeftKey = 65;
                settings.TestplayRightKey = 68;
                settings.TestplayDashKey = 32;
                var before = ui.View.Document.DeepClone();
                bool dirty = ui.View.IsDirty;
                Check(!ui.View.IsLazerEditing && !ui.View.HitLightingEnabled, "Mapping defaults must disable hit lighting.");
                Experimental();
                Toggle(false);
                Check(!ui.View.IsLazerEditing, "Editing mode must remain a draft before Apply.");
                ui.Key(27);
                Experimental();
                Check(ui.Canvas.Texts.Any(t => t.Value == ToggleLabel(false)), "Cancel must discard the lazer editing draft.");
                Toggle(false);
                ui.View.ApplySettings(path); ui.Paint(); ui.Key(27);
                Check(ui.View.IsLazerEditing && ui.View.HitLightingEnabled, "Follow mode must enable lighting for lazer editing.");
                AssertRetained(LibrarySettings.Load(path));
                foreach (var mode in new[] { CatchHitLightingMode.Off, CatchHitLightingMode.On, CatchHitLightingMode.FollowMode })
                {
                    var bounds = ui.View.SkinSelectorBounds;
                    ui.Click(bounds.X + 4, bounds.Y + 4);
                    Check(!ui.Canvas.Texts.Any(t => t.Value.Contains(L.Get("skin.hitLightingFollowMode"))),
                        "The skin selector must contain only skin choices.");
                    ui.Key(27);
                    var previous = ui.View.LibrarySettings.CatchHitLighting;
                    Appearance(); SelectLighting(mode);
                    Check(ui.View.LibrarySettings.CatchHitLighting == previous,
                        "Lighting changes must remain a draft before Apply.");
                    ui.Key(27);
                    Appearance();
                    Check(ui.Canvas.Texts.Any(t => t.Value == L.Get(LightingKey(previous)) + " ▾"),
                        "Closing Settings must discard the lighting draft.");
                    SelectLighting(mode);
                    ui.View.ApplySettings(path); ui.Paint(); ui.Key(27);
                    Check(LibrarySettings.Load(path).CatchHitLighting == mode,
                        "Applying Appearance settings must persist the lighting choice.");
                    Check(ui.View.HitLightingEnabled == (mode != CatchHitLightingMode.Off),
                        "The effective lazer lighting policy is incorrect.");
                    Experimental();
                    Toggle(true);
                    ui.View.ApplySettings(path); ui.Paint(); ui.Key(27);
                    var restored = LibrarySettings.Load(path);
                    Check(!restored.LazerEditing && restored.CatchHitLighting == mode,
                        "Applying mapping mode must preserve the selected lighting policy.");
                    Check(ui.View.HitLightingEnabled == (mode == CatchHitLightingMode.On),
                        "Mapping lighting must honor the explicit On override.");
                    AssertRetained(restored);
                    Experimental(); Toggle(false);
                    ui.View.ApplySettings(path); ui.Paint(); ui.Key(27);
                }
                Check(before.ContentEquals(ui.View.Document) && dirty == ui.View.IsDirty,
                    "Mode and lighting settings must preserve beatmap content and dirty state.");

                void Appearance()
                {
                    ui.View.OpenSettings(); ui.Paint();
                    ui.ClickText(L.Get("settings.appearance"));
                }
                string LightingKey(CatchHitLightingMode mode) => mode switch
                {
                    CatchHitLightingMode.Off => "skin.hitLightingOff",
                    CatchHitLightingMode.On => "skin.hitLightingOn",
                    _ => "skin.hitLightingFollowMode"
                };
                void SelectLighting(CatchHitLightingMode mode)
                {
                    var bounds = ui.View.HitLightingSettingsBounds;
                    ui.Click(bounds.X + 4, bounds.Y + 4);
                    ui.ClickText(L.Get(LightingKey(mode)));
                }
                void Experimental()
                {
                    ui.View.OpenSettings(); ui.Paint();
                    ui.ClickText(L.Get("settings.experimental"));
                }
                string ToggleLabel(bool enabled) => L.Get("settings.lazerEditing", L.Get(enabled ? "settings.romanisedOn" : "settings.romanisedOff"));
                void Toggle(bool enabled) => ui.ClickText(ToggleLabel(enabled));
                void AssertRetained(LibrarySettings value)
                {
                    Check(value.PlaybackLineFromBottom == .63 && value.ReverseCanvasScroll
                        && value.CanvasZoom == .72 && value.CanvasTimeZoom == .84 && !value.UniformCanvasScale
                        && value.ObjectTimelineScale == .43
                        && value.TestplayLeftKey == 65 && value.TestplayRightKey == 68 && value.TestplayDashKey == 32,
                        "Switching editing mode must preserve the existing view and custom testplay settings.");
                }
            }
        }
        finally { L.SetLanguage(language); }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
