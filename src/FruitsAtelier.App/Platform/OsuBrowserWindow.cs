using System.Diagnostics;
using FruitsAtelier.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Forms = System.Windows.Forms;
using L = FruitsAtelier.Localization.Strings;

namespace FruitsAtelier.App.Platform;

// A dedicated STA message loop keeps browser callbacks out of the editor's render loop.
internal sealed class OsuBrowserWindow : IDisposable
{
    internal const uint ViewportChangedMessage = 0x8003;
    private readonly string? profile;
    public OsuBrowserWindow(string? profile = null) { this.profile = profile; }
    public readonly System.Collections.Concurrent.ConcurrentQueue<(string Link, string Page)> EditorLinks = new();
    private BrowserState state = new();
    public BrowserState State => Volatile.Read(ref state);
    public string? PageUrl => State.Url;
    public void Execute(BrowserCommand command, string? mapUrl)
    {
        lock (gate)
            if (!disposed && form is { IsHandleCreated: true }) form.BeginInvoke((Action)(() => form?.Execute(command, mapUrl)));
    }
    private readonly object gate = new();
    private BrowserForm? form;
    private Thread? thread;
    private string? target, navigated;
    private nint parent;
    private System.Drawing.Rectangle bounds;
    private bool visible, disposed;

    public void Update(nint owner, System.Drawing.Rectangle viewport, string? url)
    {
        lock (gate)
        {
            if (disposed) return;
            bool show = viewport.Width > 0 && viewport.Height > 0 && url is not null;
            if (parent == owner && bounds == viewport && target == url && visible == show) return;
            parent = owner; bounds = viewport; target = url; visible = show;
            if (form is { IsHandleCreated: true }) form.BeginInvoke((Action)Apply);
            else if (thread is null && show)
            {
                thread = new Thread(Run) { IsBackground = true, Name = "osu! browser" };
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
            }
        }
    }

    private void Run()
    {
        using var window = new BrowserForm(profile, embedded: true) { TopLevel = false, FormBorderStyle = Forms.FormBorderStyle.None, MinimumSize = System.Drawing.Size.Empty };
        window.FormClosed += (_, _) => Forms.Application.ExitThread();
        lock (gate)
        {
            if (disposed) return;
            window.EditorLink = (link, page) => EditorLinks.Enqueue((link, page));
            window.StateChanged = value => Volatile.Write(ref state, value);
            _ = window.Handle;
            SetParent(window.Handle, parent);
            form = window;
            Apply();
        }
        Forms.Application.Run();
        lock (gate) { form = null; thread = null; }
    }

    private void Apply()
    {
        lock (gate)
        {
            if (disposed || form is null) return;
            if (!visible)
            {
                if (form.Visible)
                {
                    form.Hide();
                    // Queue repaint on the owner after its current paint and this STA's visibility change finish.
                    Native.PostMessage(parent, ViewportChangedMessage, 0, 0);
                }
                return;
            }
            form.Bounds = bounds;
            form.Show();
            Native.PostMessage(parent, ViewportChangedMessage, 0, 0);
            // Compare the map target, not the browser URL: ordinary website navigation must survive redraws.
            if (target is { } url && navigated != url)
            {
                navigated = url;
                var context = OsuBrowser.PageContext(PageUrl);
                var requested = OsuBrowser.PageContext(url);
                if (context.Map <= 0 || context.Map != requested.Map || context.Set != requested.Set) form.Open(url);
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SetParent(nint child, nint parent);

    public void Dispose()
    {
        Thread? closing;
        lock (gate)
        {
            disposed = true; closing = thread;
            if (form is { IsHandleCreated: true }) form.BeginInvoke((Action)(() => { form.AllowClose = true; form.Close(); }));
        }
        closing?.Join(TimeSpan.FromSeconds(6));
    }

    private sealed class BrowserForm : Forms.Form
    {
        private readonly string profile;
        private readonly CoreWebView2Environment? sharedEnvironment;
        private readonly List<BrowserForm> children = [];
        private readonly HashSet<ulong> canceledNavigations = [];
        internal readonly WebView2 browser = new() { Dock = Forms.DockStyle.Fill };
        internal readonly Forms.ToolStrip tools = new() { Dock = Forms.DockStyle.Fill, AutoSize = false, GripStyle = Forms.ToolStripGripStyle.Hidden };
        private readonly Forms.ToolStripLabel address = new();
        private readonly Forms.Label status = new() { Dock = Forms.DockStyle.Bottom, AutoSize = true, Padding = new Forms.Padding(8) };
        private Task? initialization;
        private string target = "https://osu.ppy.sh/";
        public Action<string, string>? EditorLink;
        public Action<BrowserState>? StateChanged;
        private bool loading, failed;
        private void Publish() => StateChanged?.Invoke(new(browser.CoreWebView2?.Source, browser.CoreWebView2?.CanGoBack == true, browser.CoreWebView2?.CanGoForward == true, loading, failed));
        public bool AllowClose;
        private bool shuttingDown;

        public BrowserForm(string? profile = null, CoreWebView2Environment? sharedEnvironment = null, bool embedded = false)
        {
            this.profile = profile ?? OsuBrowser.ProfileDirectory;
            this.sharedEnvironment = sharedEnvironment;
            Text = L.Get("browser.title"); Width = 1200; Height = 850;
            MinimumSize = new System.Drawing.Size(640, 480);
            AutoScaleMode = Forms.AutoScaleMode.Dpi;
            AddButton("browser.back", () => { if (browser.CoreWebView2?.CanGoBack == true) browser.GoBack(); });
            AddButton("browser.forward", () => { if (browser.CoreWebView2?.CanGoForward == true) browser.GoForward(); });
            AddButton("browser.reload", () => { if (browser.CoreWebView2 is null) Open(target); else browser.Reload(); });
            AddButton("browser.external", () =>
            {
                string url = browser.Source?.AbsoluteUri ?? target;
                if (OsuBrowser.IsWebLink(url)) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            });
            tools.Items.Add(address);
            var layout = new Forms.TableLayoutPanel { Dock = Forms.DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Forms.Padding.Empty };
            layout.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Percent, 100));
            layout.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Absolute, 34));
            layout.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Percent, 100));
            layout.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.AutoSize));
            browser.Margin = tools.Margin = status.Margin = Forms.Padding.Empty;
            layout.Controls.Add(tools, 0, 0); layout.Controls.Add(browser, 0, 1); layout.Controls.Add(status, 0, 2);
            if (embedded) { layout.Controls.Remove(browser); Controls.Add(browser); Disposed += (_, _) => layout.Dispose(); }
            else Controls.Add(layout);
            FormClosing += async (_, e) =>
            {
                if (!AllowClose) { e.Cancel = true; Hide(); return; }
                if (shuttingDown || browser.CoreWebView2 is not { } core) return;
                shuttingDown = true; e.Cancel = true;
                foreach (var child in children.ToArray()) { child.AllowClose = true; child.Close(); }
                var exited = new TaskCompletionSource();
                var environment = core.Environment;
                void Exited(object? sender, CoreWebView2BrowserProcessExitedEventArgs args) => exited.TrySetResult();
                environment.BrowserProcessExited += Exited;
                browser.Dispose();
                try { await exited.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (TimeoutException) { }
                finally { environment.BrowserProcessExited -= Exited; Close(); }
            };
        }

        public void Execute(BrowserCommand command, string? mapUrl)
        {
            try
            {
                var core = browser.CoreWebView2;
                switch (command)
                {
                    case BrowserCommand.Back: if (core?.CanGoBack == true) core.GoBack(); break;
                    case BrowserCommand.Forward: if (core?.CanGoForward == true) core.GoForward(); break;
                    case BrowserCommand.Refresh:
                        if (mapUrl is null) break;
                        if (core is null) Open(mapUrl); else { target = mapUrl; core.Navigate(mapUrl); }
                        break;
                    case BrowserCommand.External:
                        string url = core?.Source ?? mapUrl ?? "";
                        if (OsuBrowser.IsWebLink(url)) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                        break;
                }
            }
            catch (Exception) { failed = true; loading = false; Publish(); }
        }

        private void AddButton(string key, Action action)
        {
            string symbol = key switch { "browser.back" => "←", "browser.forward" => "→", "browser.reload" => "↻", _ => "↗" };
            var button = new Forms.ToolStripButton(symbol) { ToolTipText = L.Get(key), AccessibleName = L.Get(key), DisplayStyle = Forms.ToolStripItemDisplayStyle.Text, Overflow = Forms.ToolStripItemOverflow.Never };
            button.Click += (_, _) => { try { action(); } catch (Exception) { status.Text = L.Get("browser.failed"); } }; tools.Items.Add(button);
        }

        public async void Open(string url)
        {
            target = url;
            try
            {
                status.Text = L.Get("browser.loading"); loading = true; failed = false; Publish();
                initialization ??= Initialize();
                await initialization;
                if (!IsDisposed && target == url && browser.CoreWebView2.Source != url) browser.CoreWebView2.Navigate(url);
            }
            catch (Exception)
            {
                initialization = null;
                if (!IsDisposed) { status.Text = L.Get("browser.failed"); loading = false; failed = true; Publish(); }
            }
        }

        internal async Task Initialize()
        {
            Directory.CreateDirectory(profile);
            var environment = sharedEnvironment ?? await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
            await browser.EnsureCoreWebView2Async(environment);
            var core = browser.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.NavigationStarting += (_, e) =>
            {
                if (!OsuBrowser.IsWebLink(e.Uri))
                {
                    e.Cancel = true; canceledNavigations.Add(e.NavigationId);
                    if (e.IsUserInitiated && EditorTimestamp.IsEditorLink(e.Uri) && OsuBrowser.IsWebsite(core.Source)) EditorLink?.Invoke(e.Uri, core.Source);
                    return;
                }
                status.Text = L.Get("browser.loading"); loading = true; failed = false; Publish();
            };
            core.SourceChanged += (_, _) =>
            {
                address.Text = Uri.TryCreate(core.Source, UriKind.Absolute, out var uri) && OsuBrowser.IsWebLink(core.Source) ? uri.GetLeftPart(UriPartial.Authority) : "";
                Publish();
            };
            core.HistoryChanged += (_, _) => Publish();
            core.NavigationCompleted += (_, e) => { if (canceledNavigations.Remove(e.NavigationId)) return; status.Text = e.IsSuccess ? "" : L.Get("browser.failed"); loading = false; failed = !e.IsSuccess; Publish(); };
            core.NewWindowRequested += async (_, e) =>
            {
                e.Handled = true;
                if (!OsuBrowser.IsWebLink(e.Uri))
                {
                    if (e.IsUserInitiated && EditorTimestamp.IsEditorLink(e.Uri) && OsuBrowser.IsWebsite(core.Source)) EditorLink?.Invoke(e.Uri, core.Source);
                    return;
                }
                using var deferral = e.GetDeferral();
                var child = new BrowserForm(profile, core.Environment) { AllowClose = true, EditorLink = EditorLink };
                try
                {
                    children.Add(child);
                    child.FormClosed += (_, _) => children.Remove(child);
                    child.Show();
                    child.initialization = child.Initialize();
                    await child.initialization;
                    if (!child.IsDisposed) e.NewWindow = child.browser.CoreWebView2;
                }
                catch (Exception) { child.Dispose(); status.Text = L.Get("browser.failed"); }
            };
            core.ProcessFailed += (_, _) => { status.Text = L.Get("browser.failed"); loading = false; failed = true; Publish(); };
        }

        internal async Task Check(string directory, string mode)
        {
            initialization ??= Initialize();
            await initialization;
            var core = browser.CoreWebView2;
            var loaded = new TaskCompletionSource<bool>();
            core.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
            if (mode == "web") core.Navigate("https://osu.ppy.sh/beatmapsets/1859304");
            else
            {
                string fixture = Path.Combine(directory, "fixture"); Directory.CreateDirectory(fixture);
                File.WriteAllText(Path.Combine(fixture, "index.html"), "<meta charset='utf-8'><title>FA browser fixture</title><h1>Local browser check</h1><textarea id='draft' rows='10' cols='70'></textarea>");
                core.SetVirtualHostNameToFolderMapping("fa-browser.test", fixture, CoreWebView2HostResourceAccessKind.DenyCors);
                core.Navigate("https://fa-browser.test/index.html");
            }
            if (!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(45))) throw new InvalidOperationException("Navigation failed.");
            if (mode == "write")
            {
                await core.ExecuteScriptAsync("document.cookie='fa_test=persisted; Max-Age=86400; Secure; SameSite=Lax'; localStorage.setItem('fa_test','persisted'); document.getElementById('draft').value='\\u4e2d\\u6587 editing fixture';");
                string value = await core.ExecuteScriptAsync("document.getElementById('draft').value");
                if (!value.Contains("editing fixture")) throw new InvalidOperationException("Editing check failed.");
            }
            if (mode == "read")
            {
                string persisted = await core.ExecuteScriptAsync("document.cookie.includes('fa_test=persisted') && localStorage.getItem('fa_test') === 'persisted'");
                if (persisted != "true") throw new InvalidOperationException("Cross-process persistence check failed: " + await core.ExecuteScriptAsync("JSON.stringify({cookie: document.cookie, storage:localStorage.getItem('fa_test')})"));
            }
            if (mode == "empty")
            {
                string empty = await core.ExecuteScriptAsync("!document.cookie.includes('fa_test=') && localStorage.getItem('fa_test') === null");
                if (empty != "true") throw new InvalidOperationException("Clearing browser data retained fixture cookies or local storage.");
            }
            // Let the browser commit the fixture cookie before the short-lived writer exits.
            await Task.Delay(mode == "web" ? 5000 : mode == "write" ? 3000 : 500);
            using var screenshot = File.Create(Path.Combine(directory, mode + ".png"));
            await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot);
            File.WriteAllText(Path.Combine(directory, mode + ".txt"), $"PASS {mode}; runtime {core.Environment.BrowserVersionString}; profile {profile}");
        }
    }

    private sealed class BrowserCheckWindow : Forms.Form
    {
        public int ViewportChanges;
        protected override void WndProc(ref Forms.Message message)
        {
            if (message.Msg == ViewportChangedMessage) { ViewportChanges++; Invalidate(); }
            base.WndProc(ref message);
        }
    }

    internal static Task ClearProfileAsync(string? profile = null)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try
            {
                using var window = new BrowserForm(profile) { AllowClose = true, ShowInTaskbar = false, Opacity = 0 };
                Exception? failure = null;
                window.Shown += async (_, _) =>
                {
                    try
                    {
                        await window.Initialize();
                        await window.browser.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
                    }
                    catch (Exception error) { failure = error; }
                    finally { window.Close(); }
                };
                Forms.Application.Run(window);
                if (failure is null) completion.TrySetResult(); else completion.TrySetException(failure);
            }
            catch (Exception error) { completion.TrySetException(error); }
        }) { IsBackground = true, Name = "Clear osu! browser data" };
        worker.SetApartmentState(ApartmentState.STA); worker.Start();
        return completion.Task;
    }

    private static int RunEmbeddedCheck(string directory)
    {
        Directory.CreateDirectory(directory);
        using var window = new BrowserCheckWindow { Width = 760, Height = 620, Text = "FA embedded browser check", ShowInTaskbar = false, Opacity = 0 };
        using var host = new OsuBrowserWindow(Path.Combine(directory, "embedded-fixture-profile"));
        int result = 1;
        window.Shown += async (_, _) =>
        {
            try
            {
                var bounds = new System.Drawing.Rectangle(16, 40, 680, 500);
                host.Update(window.Handle, bounds, "about:blank");
                var ready = new TaskCompletionSource();
                for (int i = 0; host.form is null && i < 200; i++) await Task.Delay(20);
                if (host.form is not { } child) throw new InvalidOperationException("Embedded host did not start.");
                child.BeginInvoke((Action)(async () =>
                {
                    try
                    {
                        await child.Check(directory, "write");
                        if (child.browser.Top != 0 || child.browser.Size != child.ClientSize)
                            throw new InvalidOperationException("Embedded webpage does not fill its dedicated viewport.");
                        ready.SetResult();
                    }
                    catch (Exception error) { ready.SetException(error); }
                }));
                await ready.Task.WaitAsync(TimeSpan.FromSeconds(45));
                host.Update(window.Handle, System.Drawing.Rectangle.Empty, "about:blank");
                await Task.Delay(100);
                host.Update(window.Handle, bounds, "about:blank");
                await Task.Delay(200);
                var retained = new TaskCompletionSource();
                child.BeginInvoke((Action)(async () =>
                {
                    try
                    {
                        string value = await child.browser.CoreWebView2.ExecuteScriptAsync("document.getElementById('draft').value");
                        if (!child.Visible || !value.Contains("editing fixture")) throw new InvalidOperationException("Tab hide/show lost the draft.");
                        retained.SetResult();
                    }
                    catch (Exception error) { retained.SetException(error); }
                }));
                await retained.Task.WaitAsync(TimeSpan.FromSeconds(10));
                foreach (var viewport in new[] { System.Drawing.Rectangle.Empty, new System.Drawing.Rectangle(8, 60, 300, 420),
                    new System.Drawing.Rectangle(350, 80, 320, 360), System.Drawing.Rectangle.Empty, bounds })
                {
                    int notifications = window.ViewportChanges;
                    host.Update(window.Handle, viewport, "about:blank");
                    var applied = new TaskCompletionSource();
                    child.BeginInvoke((Action)(() =>
                    {
                        if (child.Visible != !viewport.IsEmpty || !viewport.IsEmpty && child.Bounds != viewport)
                            applied.SetException(new InvalidOperationException("Hide/move/resize retained the previous native viewport."));
                        else applied.SetResult();
                    }));
                    await applied.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    for (int i = 0; window.ViewportChanges == notifications && i < 100; i++) await Task.Delay(10);
                    if (window.ViewportChanges == notifications) throw new InvalidOperationException("Viewport transition did not queue an owner repaint.");
                }
                var captured = new TaskCompletionSource();
                child.BeginInvoke((Action)(async () =>
                {
                    try
                    {
                        using var bitmap = new System.Drawing.Bitmap(child.Width, child.Height);
                        child.DrawToBitmap(bitmap, child.ClientRectangle);
                        using var stream = new MemoryStream();
                        await child.browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                        stream.Position = 0;
                        using var page = System.Drawing.Image.FromStream(stream);
                        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
                        var origin = child.PointToClient(child.browser.PointToScreen(System.Drawing.Point.Empty));
                        graphics.DrawImage(page, new System.Drawing.Rectangle(origin, child.browser.Size));
                        bitmap.Save(Path.Combine(directory, "embedded.png"));
                        captured.SetResult();
                    }
                    catch (Exception error) { captured.SetException(error); }
                }));
                await captured.Task.WaitAsync(TimeSpan.FromSeconds(10));
                File.WriteAllText(Path.Combine(directory, "embedded.txt"), "PASS child placement, hide/move/resize owner repaint, viewport bounds, local editing and retained draft after tab hide/show");
                result = 0;
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(directory, "embedded.txt"), error.ToString()); }
            finally { host.Dispose(); window.Close(); }
        };
        Forms.Application.Run(window);
        return result;
    }

    internal static int RunCheck(string directory, string mode)
    {
        if (mode == "embed") return RunEmbeddedCheck(Path.GetFullPath(directory));
        if (mode == "clear") { ClearProfileAsync(Path.Combine(Path.GetFullPath(directory), "fixture-profile")).GetAwaiter().GetResult(); return 0; }
        if (mode is not ("write" or "read" or "web" or "empty")) throw new ArgumentException("Unknown browser check mode.");
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        using var window = new BrowserForm(Path.Combine(directory, mode == "web" ? "anonymous-profile" : "fixture-profile")) { AllowClose = true, ShowInTaskbar = false, Opacity = 0 };
        int result = 1;
        window.Shown += async (_, _) =>
        {
            try { await window.Check(directory, mode); result = 0; }
            catch (Exception error) { File.WriteAllText(Path.Combine(directory, mode + ".txt"), error.ToString()); }
            finally { window.Close(); }
        };
        Forms.Application.Run(window);
        return result;
    }
}
