using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private bool SettingsOwnsInput => librarySettingsOpen && !ErrorVisible && !DiscardConfirmationVisible
        && !OsuLinkPickerVisible && !AcknowledgementsVisible;

    private void SettingsPointerDown(float x, float y, int button)
    {
        mouseX = x; mouseY = y; ResetTextCaret();
        if (!CapturingShortcut && !shortcutResetMode)
        {
            if (LanguagePointerDown(x, y, button) || BeginSettingsColourDrag(x, y, button)) return;
            if (contextItems.Count > 0)
            {
                if (button == 0) ActivateContextMenu(x, y); else contextItems.Clear();
                return;
            }
            if (BeginWorkspaceScroll(x, y, button) || BeginVolumeDrag(x, y, button) || BeginBackgroundDimDrag(x, y, button)) return;
        }
        if (button == 0)
            for (int i = hits.Count - 1; i >= 0; i--)
                if (hits[i].Bounds.Contains(x, y)) { if (hits[i].Enabled) hits[i].Action(); break; }
    }

    private void SettingsPointerMove(float x, float y)
    {
        mouseX = x; mouseY = y;
        if (CapturingShortcut || shortcutResetMode) return;
        if (workspaceScrollDragging) MoveWorkspaceScroll(y);
        else if (settingsColourDrag != 0) UpdateSettingsColourDrag(x, y);
        else if (backgroundDimDragging) UpdateBackgroundDimDrag(x);
        else if (volumeDrag >= 0) UpdateVolumeDrag(x);
        else if (textSelecting) MoveInputSelection(x);
    }

    private void SettingsPointerUp(float x, float y, int button)
    {
        if (button != 0) return;
        SettingsPointerMove(x, y);
        workspaceScrollDragging = false; settingsColourDrag = 0; textSelecting = false;
        FinishVolumeDrag(); FinishBackgroundDimDrag();
    }

    private void SettingsWheel(float x, float y, float delta)
    {
        if (CapturingShortcut || shortcutResetMode)
        {
            if (shortcutConfirming) shortcutConflictScroll = Math.Max(0, shortcutConflictScroll - delta / 120 * 44);
            return;
        }
        if (pendingLanguage is not null) return;
        if (languageMenuOpen)
        {
            if (languageMenuBounds.Contains(x, y)) languageFirstRow = Math.Clamp(languageFirstRow - (int)(delta / 120),
                0, Math.Max(0, L.AvailableLanguages.Count - languageVisibleRows));
            return;
        }
        ScrollStorage(x, y, delta);
    }

    private void SettingsKeyDown(int key, bool ctrl, bool shift)
    {
        if (CapturingTestplayKey) { CaptureTestplayKey(key); return; }
        if (ScreenshotKeyDown(key, ctrl, shift) || FullscreenKeyDown(key, ctrl, shift)) return;
        ResetTextCaret();
        if (languageMenuOpen)
        {
            if (!ctrl && !shift && !altHeld)
            {
                if (key == 27) languageMenuOpen = false;
                else if (key is 38 or 40)
                {
                    languageSelection = (languageSelection + (key == 38 ? L.AvailableLanguages.Count - 1 : 1)) % L.AvailableLanguages.Count;
                    KeepLanguageSelectionVisible();
                }
                else if (key == 13) SelectLanguage(L.AvailableLanguages[languageSelection]);
            }
            return;
        }
        LibraryKey(key, ctrl, shift);
    }
}
