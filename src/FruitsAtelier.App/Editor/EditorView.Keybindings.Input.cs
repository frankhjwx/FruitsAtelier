using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private ShortcutScope ActiveShortcutScope => IsTestplaying ? ShortcutScope.Testplay : LibraryVisible ? ShortcutScope.Library
        : HitsoundingLibraryAvailable && hitsoundingLibraryFocused ? ShortcutScope.Samples
        : PatternGalleryInputAvailable && galleryFocused ? ShortcutScope.Gallery
        : TimingSetupInputAvailable && timingSetupFocused ? ShortcutScope.TimingPoints
        : TimingPageVisible ? ShortcutScope.Timing : HitsoundingPageVisible ? ShortcutScope.Hitsounding : ShortcutScope.Compose;

    private bool ShortcutCommandsAvailable => !IsEditingText && !AcknowledgementsVisible && !DiscardConfirmationVisible
        && !OsuLinkPickerVisible && !SynchronizationBlocksInput && !SynchronizationWaitVisible
        && (IsTestplaying || LibraryVisible && !librarySettingsOpen && !collectionDialogOpen && !resourcePage
            || DockContentInputAvailable);

    private bool RouteShortcut(ref int key, ref bool ctrl, ref bool shift, out bool alt)
    {
        alt = altHeld;
        int physical = ShortcutBinding.Normalize(key);
        shortcutPhysicalKeys.Add(physical);
        // Modifier flags also support synthetic host events and key presses after focus changes.
        SetPhysicalModifier(17, ctrl || physical == 17); SetPhysicalModifier(16, shift || physical == 16); SetPhysicalModifier(18, altHeld || physical == 18);
        if (LibrarySettings.ShortcutBindings is not { Count: > 0 }) return false;
        bool available = ShortcutCommandsAvailable;
        var scope = ActiveShortcutScope;
        var commands = ShortcutCommands.Where(c => c.Lazer == LibrarySettings.LazerEditing
            && (c.Scope == ShortcutScope.Global || available && c.Scope == scope)).ToArray();
        ShortcutCommand? selected = null; ShortcutBinding? gesture = null;
        foreach (var command in commands)
        {
            if (command.Scope == ShortcutScope.Testplay && command.Group == "movement") continue;
            foreach (var binding in CommandBindings(command))
            {
                if (!binding.Keys.Contains(physical) || !binding.Held(shortcutPhysicalKeys)) continue;
                if (IsEditingText && !command.Defaults.Any(binding.Same)) continue;
                if (gesture is null || binding.Keys.Length > gesture.Keys.Length) { selected = command; gesture = binding; }
            }
        }
        if (selected is not null)
        {
            // Keep the existing input handlers as the owners of text, draft, drag and undo guards.
            var canonical = selected.Defaults.FirstOrDefault(gesture!.Same) ?? selected.Defaults[0];
            if (selected.Scope == ShortcutScope.Testplay && selected.Id is "volumeUp" or "volumeDown") canonical = selected.Defaults[0];
            key = canonical.Keys.LastOrDefault(k => !ShortcutBinding.Modifier(k));
            if (key == 0) key = canonical.Keys[^1];
            ctrl = canonical.Keys.Contains(17); shift = canonical.Keys.Contains(16); alt = canonical.Keys.Contains(18);
            shortcutReleases[physical] = (key, gesture!);
            return false;
        }
        var input = ShortcutBinding.From(new[] { physical }.Concat(ctrl ? new[] { 17 } : [])
            .Concat(shift ? new[] { 16 } : []).Concat(altHeld ? new[] { 18 } : []).ToArray());
        // A removed default must not fall through to its original hard-coded handler.
        return commands.Any(c => !(c.Scope == ShortcutScope.Testplay && c.Group == "movement") && c.Defaults.Any(input.Same))
            || commands.Any(c => CommandBindings(c).Any(b => b.Keys.Length > 1 && b.Keys.Contains(physical)
                && b.Keys.Where(ShortcutBinding.Modifier).All(shortcutPhysicalKeys.Contains)));
    }
    private void SetPhysicalModifier(int key, bool down)
    {
        if (down) shortcutPhysicalKeys.Add(key); else shortcutPhysicalKeys.Remove(key);
    }
    private void ReleaseShortcut(int physical)
    {
        physical = ShortcutBinding.Normalize(physical);
        shortcutPhysicalKeys.Remove(physical);
        foreach (var pair in shortcutReleases.Where(pair => pair.Value.Binding.Keys.Contains(physical)).ToArray())
        {
            shortcutReleases.Remove(pair.Key);
            ReleaseShortcutState(pair.Value.Canonical);
        }
    }
    private void ConfigureTestplayShortcuts(CatchTestplaySession session)
    {
        var commands = ShortcutCommands.Where(c => c.Lazer == LibrarySettings.LazerEditing
            && c.Scope is ShortcutScope.Testplay or ShortcutScope.Global).ToArray();
        if (!commands.Any(c => LibrarySettings.ShortcutBindings?.ContainsKey(c.StorageKey) == true)) return;
        ShortcutBinding[] Movement(string id) => CommandBindings(commands.Single(c => c.Scope == ShortcutScope.Testplay && c.Id == id));
        session.ConfigureBindings(Movement("moveLeft"), Movement("moveRight"), Movement("dash"),
            commands.Where(c => c.Group != "movement").SelectMany(c => CommandBindings(c)).ToArray());
    }
}
