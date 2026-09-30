using FruitsAtelier.Core;

namespace FruitsAtelier.App.Editor;

public sealed partial class EditorView
{
    private readonly List<(StackEnvelope Stack, int Snap)> stackHistory = [];
    private int stackHistoryIndex;
    private void ResetStackHistory()
    {
        stackHistory.Clear(); stackHistory.Add((stackDraft.DeepClone(), StreamSnapDivisor)); stackHistoryIndex = 0;
    }
    private void RecordStackDraft()
    {
        if (!stackMode || stackHistory.Count == 0) return;
        var current = stackHistory[stackHistoryIndex];
        if (current.Snap == StreamSnapDivisor && StackEnvelope.Equal(current.Stack, stackDraft)) return;
        stackHistory.RemoveRange(stackHistoryIndex + 1, stackHistory.Count - stackHistoryIndex - 1);
        stackHistory.Add((stackDraft.DeepClone(), StreamSnapDivisor)); stackHistoryIndex++;
        if (stackHistory.Count > 128) { stackHistory.RemoveAt(0); stackHistoryIndex--; }
    }
    private bool StackHistoryKey(int key, bool ctrl, bool shift)
    {
        if (!ctrl || key is not (90 or 89)) return false;
        stackNumericField = -1; stackNumericError = "";
        if (stackFruitDragging >= 0 || stackPointDragging >= 0) { CancelStackDrag(); return true; }
        if (streamSnapDragging)
        {
            streamSnapDragging = false; StreamSnapDivisor = stackHistory[stackHistoryIndex].Snap;
            RefreshStackPreview(); return true;
        }
        int next = stackHistoryIndex + (key == 89 || shift ? 1 : -1);
        if (next < 0 || next >= stackHistory.Count) return true;
        stackHistoryIndex = next; stackDraft = stackHistory[next].Stack.DeepClone(); StreamSnapDivisor = stackHistory[next].Snap;
        stackSelectedFruit = -1; stackSelectedPoint = 0;
        RefreshStackPreview(); return true;
    }
}
