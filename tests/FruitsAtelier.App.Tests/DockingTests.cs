using FruitsAtelier.App.Editor;
using FruitsAtelier.App.Rendering;
using FruitsAtelier.Core;
using L = FruitsAtelier.Localization.Strings;

internal static class DockingTests
{
    public static void Run()
    {
        string language = L.Language;
        try
        {
            foreach (string locale in new[] { "en", "zh-CN" })
            {
                L.SetLanguage(locale);
                var ui = new Ui(overview: false);
                ui.LoadDocument(new MapDocument { DurationMs = 10000 });
                ui.Resize(1440, 900);
                var baseline = ui.View.Document.DeepClone();
                Click(ui.View.DifficultyCurveToggleBounds); ui.OpenPreview();
                CheckToolbar();
                ResizeDock(true, 0); ResizeDock(false, 0);
                Check(DockWidth(true) == DockWidth(false), "Both docks share the same minimum width");
                ResizeDock(true, 2000); ResizeDock(false, 2000);
                Check(DockWidth(true) == DockWidth(false), "Both docks share the same maximum width");
                ResizeDock(true, 280); ResizeDock(false, 360);
                Drag(EditorView.DockPanel.MovementStrain, ui.Width - 80, 200);
                Check(ui.View.DifficultyCurvePanelBounds.X > ui.Plot.Right && ui.View.DifficultyCurveVisible
                    && !ui.View.CatchPreviewVisible, "Strain merges on the right and becomes active");
                Check(ui.View.DockTabBounds(EditorView.DockPanel.Preview).Width > 0, "Inactive preview retains a named tab");
                Click(ui.View.DockTabBounds(EditorView.DockPanel.Preview));
                Check(ui.View.CatchPreviewVisible && ui.View.DifficultyCurveGraphBounds.Width == 0, "Switching tabs hides strain hit targets");
                Drag(EditorView.DockPanel.Preview, 8, 200);
                Check(ui.View.PreviewViewport.Right < ui.Plot.X && ui.View.DifficultyCurvePanelBounds.X > ui.Plot.Right,
                    "Preview moves left while strain remains on the right");
                CheckToolbar();
                var resize = ui.View.PreviewResizeBounds;
                ui.View.PointerDown(resize.X + 4, resize.Y + 50, 0, false, false);
                ui.View.PointerMove(resize.X + 44, resize.Y + 50, false, false);
                ui.View.PointerUp(resize.X + 44, resize.Y + 50, 0); ui.Paint();
                Check(ui.View.PreviewResizeBounds.X > resize.X + 20, "Left preview resizes toward the canvas");
                var leftPreview = ui.View.PreviewViewport;
                Drag(EditorView.DockPanel.Preview, ui.Plot.X + 20, ui.Plot.Y + 20);
                Check(ui.View.PreviewViewport == leftPreview, "Canvas rejects docking without changing layout");
                var tab = ui.View.DockTabBounds(EditorView.DockPanel.Preview);
                ui.View.PointerDown(tab.X + 10, tab.Y + 10, 0, false, false);
                ui.View.PointerMove(ui.Width - 50, 200, false, false); ui.Key(27);
                ui.View.PointerUp(ui.Width - 50, 200, 0); ui.Paint();
                Check(!ui.View.WantsCapture && ui.View.PreviewViewport == leftPreview, "Escape cancels docking");
                ui.View.PointerDown(tab.X + 10, tab.Y + 10, 0, false, false);
                ui.View.PointerMove(ui.Width - 50, 200, false, false); ui.View.CancelInteraction(); ui.Paint();
                Check(!ui.View.WantsCapture && ui.View.PreviewViewport == leftPreview, "Lost capture cancels docking");
                ui.Key(114);
                Check(ui.View.TimingPageVisible && ui.View.DockTabBounds(EditorView.DockPanel.Preview).Width == 0
                    && ui.View.PreviewViewport.Width == 0 && ui.View.DifficultyCurveGraphBounds.Width == 0, "Timing retains its fixed view without stale dock targets");
                var timingPlot = ui.Plot;
                ui.ClickText(L.Get("dock.layout")); ui.ClickText(L.Get("dock.resetLayout"));
                Check(ui.View.TimingPageVisible && ui.Plot == timingPlot, "Timing locks Layout reset");
                ui.Key(27);
                ui.View.PointerDown(tab.X + 10, tab.Y + 10, 0, false, false);
                ui.View.PointerMove(ui.Width - 50, 200, false, false);
                ui.View.PointerUp(ui.Width - 50, 200, 0); ui.Paint();
                Check(ui.Plot == timingPlot && !ui.View.WantsCapture, "Timing does not accept dock gestures");
                ui.ClickText(L.Get("timing.panel") + " ▾"); ui.ClickText(L.Get("timing.detailsPanel"));
                Check(!ui.View.TimingPageVisible && ui.View.PreviewViewport == leftPreview, "Mapping restores positions and resized width");
                Drag(EditorView.DockPanel.MovementStrain, 50, 200);
                Check(ui.View.DifficultyCurvePanelBounds.X == 0 && ui.View.DifficultyCurveVisible, "Strain also merges into the left group");
                var merged = ui.View.DockTabBounds(EditorView.DockPanel.MovementStrain);
                var previewTab = ui.View.DockTabBounds(EditorView.DockPanel.Preview);
                ui.View.PointerDown(merged.X + 10, merged.Y + 10, 0, false, false);
                ui.View.PointerMove(previewTab.X + 5, previewTab.Y + 10, false, false); ui.Paint();
                Check(ui.View.DockTabBounds(EditorView.DockPanel.MovementStrain).X < ui.View.DockTabBounds(EditorView.DockPanel.Preview).X,
                    "Reordered tabs preview before pointer release");
                Check(!ui.Canvas.PaintCalls.Any(p => p.Color == 0x59D3C3 && p.FillBounds is { } r && r.Height > 100 && p.Opacity == .2f),
                    "Same-dock reorder has no full dock overlay");
                ui.View.CancelInteraction(); ui.Paint();
                Check(ui.View.DockTabBounds(EditorView.DockPanel.MovementStrain) == merged, "Cancel restores original tab order");
                Drag(EditorView.DockPanel.MovementStrain, previewTab.X + 5, previewTab.Y + 10);
                Check(ui.View.DockTabBounds(EditorView.DockPanel.MovementStrain).X < ui.View.DockTabBounds(EditorView.DockPanel.Preview).X,
                    "Tabs reorder within a group");
                merged = ui.View.DockTabBounds(EditorView.DockPanel.MovementStrain);
                ui.Key(114); Check(ui.Plot == timingPlot, "Timing geometry is independent of mapping groups");
                ui.Key(27);
                Check(ui.View.DockTabBounds(EditorView.DockPanel.MovementStrain) == merged && ui.View.DifficultyCurveVisible,
                    "Mapping restores the active tab and order");
                Click(ui.View.DifficultyCurveToggleBounds);
                Check(!ui.View.DifficultyCurveVisible && !ui.View.CatchPreviewVisible, "Collapsing a group hides both panels");
                ui.Key(114); ui.Key(27);
                Check(!ui.View.DifficultyCurveVisible && !ui.View.CatchPreviewVisible, "Collapsed state survives view switches");
                Click(ui.View.DifficultyCurveToggleBounds);
                Check(ui.View.DifficultyCurveVisible, "Reopening restores the active tab");
                var graph = ui.View.DifficultyCurveGraphBounds;
                double seek = -1; ui.View.RequestSeek = value => seek = value;
                Click(new(graph.X + 10, graph.Y, 20, 2));
                Check(seek > 9900, "Docked strain retains seeking");
                Check(ui.View.Document.ContentEquals(baseline) && !ui.View.IsDirty && !ui.View.WantsCapture,
                    "Docking leaves content and undo unchanged and releases capture");
                ui.Resize(980, 620); CheckToolbar();
                ui.Key(114);
                var snap = ui.View.SnapSliderBounds;
                ui.Click(snap.Right - 31, snap.Y + snap.Height / 2);
                Check(ui.View.SnapDivisor == 16, "Timing snap remains interactive in the shared toolbar");
                ui.ClickText(L.Get("timing.panel") + " ▾"); ui.ClickText(L.Get("timing.detailsPanel"));
                Check(!ui.View.TimingPageVisible, "Left toolbar selector switches views in a narrow window");

                ui.ClickText(L.Get("dock.layout"));
                Check(ui.Canvas.Texts.Any(t => t.Value == "✓ " + L.Get("dock.preview")), "Layout checks existing tabs");
                ui.ClickText("✓ " + L.Get("dock.preview"));
                Check(!ui.View.CatchPreviewVisible && ui.View.DockTabBounds(EditorView.DockPanel.Preview).Width == 0,
                    "Layout removes a tab without showing stale content");
                var strainTab = ui.View.DockTabBounds(EditorView.DockPanel.MovementStrain);
                ui.View.PointerDown(strainTab.X + 10, strainTab.Y + 10, 2, false, false); ui.Paint();
                ui.ClickText(L.Get("dock.addTab", L.Get("dock.preview")));
                Check(ui.View.CatchPreviewVisible && ui.View.PreviewViewport.Right < ui.Plot.X, "Context menu adds an absent tab to its dock");
                var addedTab = ui.View.DockTabBounds(EditorView.DockPanel.Preview);
                ui.View.PointerDown(addedTab.X + 10, addedTab.Y + 10, 2, false, false); ui.Paint();
                ui.ClickText("✓ " + L.Get("dock.addTab", L.Get("dock.preview")));
                Check(ui.View.DockTabBounds(EditorView.DockPanel.Preview) == addedTab, "Existing tabs cannot be duplicated");
                ui.View.PointerDown(addedTab.X + 10, addedTab.Y + 10, 2, false, false); ui.Paint();
                ui.ClickText(L.Get("dock.closeTab"));
                Check(!ui.View.CatchPreviewVisible && ui.View.DifficultyCurveVisible, "Closing active tab selects its neighbour");
                strainTab = ui.View.DockTabBounds(EditorView.DockPanel.MovementStrain);
                ui.View.PointerDown(strainTab.X + 10, strainTab.Y + 10, 2, false, false); ui.Paint();
                ui.ClickText(L.Get("dock.closeTab"));
                Check(!ui.View.CatchPreviewVisible && !ui.View.DifficultyCurveVisible
                    && ui.View.DockResizeBounds(true).Width == 0 && ui.View.DockResizeBounds(false).Width == 0,
                    "Closing the last tabs clears both dock hit targets");
                ui.ClickText(L.Get("dock.layout")); ui.ClickText(L.Get("dock.resetLayout"));
                Check(ui.View.CatchPreviewVisible && ui.View.DifficultyCurveVisible && ui.View.PreviewViewport.X > ui.Plot.Right,
                    "Reset restores separate expanded docks");
                Check(ui.View.Document.ContentEquals(baseline) && !ui.View.IsDirty, "Layout management preserves content");

                float DockWidth(bool left) => left ? ui.View.DockResizeBounds(true).X + 4 : ui.Width - ui.View.DockResizeBounds(false).X - 4;
                void ResizeDock(bool left, float targetWidth)
                {
                    var handle = ui.View.DockResizeBounds(left);
                    float x = left ? targetWidth : ui.Width - targetWidth;
                    ui.View.PointerDown(handle.X + 4, handle.Y + 60, 0, false, false);
                    Check(ui.View.WantsCapture, "Both dock splitters capture resize gestures");
                    ui.View.PointerMove(x, handle.Y + 60, false, false);
                    ui.View.PointerUp(x, handle.Y + 60, 0); ui.Paint();
                }

                void CheckToolbar()
                {
                    var controls = ui.View.CanvasToolbarBounds;
                    var stats = ui.View.WorkspaceStatsBounds;
                    Check(ui.View.WorkspaceSelectorBounds.Right < controls.X && controls.Right == stats.X
                        && stats.Right == ui.Width, "Toolbar has three fixed sections");
                    Check(ui.View.ZoomSliderBounds.X >= controls.X && ui.View.CanvasScaleLockBounds.Right < ui.View.SnapSliderBounds.X
                        && ui.View.SnapSliderBounds.Right < stats.X, "Zoom and snap stay in the middle section");
                    Check(ui.View.DockTabBounds(EditorView.DockPanel.MovementStrain).Y >= controls.Bottom,
                        "Dock tabs stay below the shared toolbar");
                }

                void Click(Rect r) => ui.Click(r.X + Math.Min(10, r.Width / 2), r.Y + Math.Min(10, r.Height / 2));
                void Drag(EditorView.DockPanel panel, float x, float y)
                {
                    var r = ui.View.DockTabBounds(panel);
                    Check(r.Width > 0, "Drag begins on a visible tab");
                    ui.View.PointerDown(r.X + 10, r.Y + 10, 0, false, false);
                    Check(ui.View.WantsCapture, "Tab drag captures input");
                    ui.View.PointerMove(x, y, false, false); ui.Paint();
                    ui.View.PointerUp(x, y, 0); ui.Paint();
                }
            }
        }
        finally { L.SetLanguage(language); }
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
