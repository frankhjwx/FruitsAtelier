using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool dropletSettingsFromGeneral;

    // Imported maps and older projects need an opt-in before new sliders randomize.
    private SliderDropletMode NewSliderDropletMode => LibrarySettings.DefaultDropletMode == SliderDropletMode.FollowMap
        && !Document.DerandomizeFSliderDroplets && !Document.RandomizeNewSliders
        ? SliderDropletMode.Derandomize : LibrarySettings.DefaultDropletMode;

    private void RememberDropletSettings()
    {
        LibrarySettings.DerandomizeNewProjects = Document.DerandomizeFSliderDroplets;
        LibrarySettings.DefaultDropletHardRock = Document.DerandomizeDropletsForHardRock;
        LibrarySettings.DefaultDropletStrength = Document.RandomizeDropletStrength;
        LibrarySettings.DefaultDropletSeed = Document.RandomizeDropletSeed;
        RequestViewPreference?.Invoke();
    }

    private void OpenDropletSettingsFromGeneral()
    {
        FinishBackgroundDimDrag(); FinishVolumeDrag();
        if (AudioPlaying) RequestPausePlayback?.Invoke();
        InitializeSongSetup();
        songTab = 4; SongSetupVisible = true;
        dropletSettingsFromGeneral = true;
        librarySettingsOpen = false;
        libraryField = bindingCapture = -1;
        contextItems.Clear(); hits.Clear(); fields.Clear();
    }

    private void DrawGlobalDropletSettings(ICanvas c)
    {
        hits.Clear(); fields.Clear(); songFieldBounds.Clear(); songSliders.Clear();
        var r = new Rect((width - Math.Min(620, width - 32)) / 2, (height - 240) / 2, Math.Min(620, width - 32), 240);
        c.Fill(new(0, 0, width, height), Background, opacity: .7f);
        c.Fill(r, Panel, 8); c.Stroke(r, Grid, radius: 8);
        c.Text(L.Get("randomize.title"), r.X + 22, r.Y + 16, 19, Foreground, r.Width - 80, true);
        Button(c, new(r.Right - 54, r.Y + 10, 32, 28), "×", CloseSongSetup);
        DrawDropletDefaults(c, new(r.X + 22, r.Y + 56, r.Width - 44, 110));
        Button(c, new(r.Right - 116, r.Bottom - 46, 94, 32), L.Get("song.ok"), CloseSongSetup, true);
    }

    private void DrawDropletDefaults(ICanvas c, Rect bounds)
    {
        c.Line(bounds.X, bounds.Y, bounds.Right, bounds.Y, Grid);
        c.Text(L.Get("randomize.globalDefaults"), bounds.X, bounds.Y + 12, 12, Foreground, bounds.Width, true);
        ToggleSwitch(c, new(bounds.X, bounds.Y + 36, bounds.Width, 32), L.Get("randomize.defaultConversion"),
            LibrarySettings.DerandomizeDroplets, () => ChangeDropletDefault(conversion: true));
        ToggleSwitch(c, new(bounds.X, bounds.Y + 74, bounds.Width, 32), L.Get("randomize.defaultProjects"),
            LibrarySettings.DerandomizeNewProjects, () => ChangeDropletDefault(conversion: false));
    }

    private void ChangeDropletDefault(bool conversion)
    {
        if (conversion) LibrarySettings.DerandomizeDroplets = !LibrarySettings.DerandomizeDroplets;
        else LibrarySettings.DerandomizeNewProjects = !LibrarySettings.DerandomizeNewProjects;
        RequestViewPreference?.Invoke();
    }
}
