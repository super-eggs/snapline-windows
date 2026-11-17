using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Snapline
{
    internal sealed class App : Application
    {
        private Forms.NotifyIcon tray;
        private FolderCollector collector;
        private DispatcherTimer clipboardTimer;
        private uint clipboardSequence;
        private uint pendingClipboardSequence;
        private DateTime clipboardRetryAt;
        private bool clipboardBusy, exiting;
        private Store store;
        private Shelf shelf;
        private CaptureHotkey captureHotkey;
        private CaptureHotkey pinHotkey, togglePinsHotkey;
        private PinBoard pins;
        private ImageWorker pinWorker;
        private bool pinBusy, shortcutDialogOpen, pinDemo;
        private sealed class ClipboardPin { internal BitmapSource Image; internal uint Sequence; }
        private CaptureWindow captureWindow;
        private bool captureStarting;
        private bool demo;
        private bool captureDemo;
        private bool startup;
        private Mutex mutex;
        private string demoRoot;
        private string smokeReport;
        [STAThread] public static int Main(string[] args)
        {
            // Static screenshot windows do not need the large D3D texture caches held in shared system memory.
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            int testIndex = Array.IndexOf(args, "--self-test");
            if (testIndex >= 0) return SelfTests.Run(args.Length > testIndex + 1 ? Path.GetFullPath(args[testIndex + 1]) : Path.Combine(Path.GetTempPath(), "Snapline-self-test.txt"));
            var app = new App();
            app.startup = Array.IndexOf(args, "--startup") >= 0;
            app.demo = Array.IndexOf(args, "--demo") >= 0;
            app.captureDemo = Array.IndexOf(args, "--capture-demo") >= 0;
            if (app.captureDemo) app.demo = true;
            app.pinDemo = Array.IndexOf(args, "--pin-demo") >= 0;
            if (app.pinDemo) app.demo = true;
            int smokeIndex = Array.IndexOf(args, "--smoke-test");
            if (smokeIndex >= 0)
            {
                app.demo = true;
                app.smokeReport = args.Length > smokeIndex + 1 ? Path.GetFullPath(args[smokeIndex + 1]) : Path.Combine(Path.GetTempPath(), "Snapline-smoke-test.txt");
            }
            bool owns;
            app.mutex = new Mutex(true, app.smokeReport != null ? "Local\\Snapline.Smoke." + Guid.NewGuid().ToString("N") : app.demo ? "Local\\Snapline.Windows.Demo" : "Local\\Snapline.Windows", out owns);
            if (!owns)
            {
                if (!app.startup) MessageBox.Show("截图晾衣绳已经运行，可从右下角托盘唤出。", "截图晾衣绳");
                app.mutex.Dispose(); return 0;
            }
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
            {
                if (e.Exception is IOException || e.Exception is UnauthorizedAccessException || e.Exception is System.Runtime.InteropServices.ExternalException)
                {
                    e.Handled = true;
                    if (app.shelf != null) app.shelf.Status("操作暂未完成，请重试；可通过托盘打开截图文件夹。");
                }
            };
            app.Startup += delegate { app.Initialize(); };
            try { return app.Run(); }
            finally { app.mutex.ReleaseMutex(); app.mutex.Dispose(); }
        }
        private async void Initialize()
        {
            string data = demo ? Path.Combine(Path.GetTempPath(), "Snapline-demo-" + Guid.NewGuid().ToString("N"))
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snapline");
            if (demo) demoRoot = data;
            store = new Store(data, demo);
            shelf = new Shelf(store, demo);
            if (pinDemo) shelf.ShowInTaskbar = true;
            pins = new PinBoard(CopyPinnedImage, CopyPickedColor, captureDemo || pinDemo);
            shelf.PinRequested += delegate(BitmapSource image) { ShowPin(image); };
            MainWindow = shelf;
            store.Error += delegate(string text) { Dispatcher.BeginInvoke(new Action(delegate { shelf.Status(text); })); };
            shelf.Copied += delegate { clipboardSequence = Native.GetClipboardSequenceNumber(); };
            shelf.Show();
            Task loading = demo ? Task.FromResult(true) : store.LoadAsync();
            if (demo)
            {
                for (int i = 0; i < 3; i++) store.Add(DemoImage(i));
                shelf.Reveal(true);
            }
            else
            {
                shelf.Hide();
                SetupCollector();
                clipboardSequence = Native.GetClipboardSequenceNumber();
                clipboardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                clipboardTimer.Tick += delegate { CollectClipboard(); };
                clipboardTimer.Start();
            }
            SetupTray();
            if (!demo)
            {
                captureHotkey = new CaptureHotkey(shelf, StartCapture);
                if (!captureHotkey.Register(store.Settings.CaptureHotkey))
                {
                    tray.BalloonTipTitle = "截图快捷键未启用";
                    tray.BalloonTipText = store.Settings.CaptureHotkey + " 已被占用或无效，请在托盘“截图快捷键”中修改；仍可点击“截图”使用。";
                    tray.ShowBalloonTip(5000);
                }
                pinHotkey = new CaptureHotkey(shelf, PastePin, 4);
                togglePinsHotkey = new CaptureHotkey(shelf, delegate { if (!shortcutDialogOpen) pins.ToggleAll(); }, 6);
                bool pasteRegistered = pinHotkey.Register(store.Settings.PinHotkey), toggleRegistered = togglePinsHotkey.Register(store.Settings.TogglePinsHotkey);
                if (!pasteRegistered || !toggleRegistered)
                {
                    tray.BalloonTipTitle = "部分贴图快捷键未启用";
                    tray.BalloonTipText = "贴图或隐藏贴图快捷键已被占用，可从托盘使用，或修改相应快捷键。"; tray.ShowBalloonTip(5000);
                }
            }
            try { await loading; }
            catch (Exception) { shelf.Status("历史图片未能加载，可通过托盘打开收集文件夹。"); }
            if (captureDemo) StartCapture();
            if (pinDemo)
            {
                pinHotkey = new CaptureHotkey(shelf, PastePin, 4);
                if (!pinHotkey.Register("F3")) pinHotkey.Register("Ctrl+Alt+Shift+F10");
                store.Settings.PinHotkey = pinHotkey.Gesture;
                togglePinsHotkey = new CaptureHotkey(shelf, delegate { pins.ToggleAll(); }, 6);
                if (!togglePinsHotkey.Register("Shift+F3")) togglePinsHotkey.Register("Ctrl+Alt+Shift+F9");
                store.Settings.TogglePinsHotkey = togglePinsHotkey.Gesture;
                ShowPin(DemoImage(0));
            }
            if (smokeReport != null)
            {
                // Reproduce a tray/hotkey reveal: pointer outside the shelf.
                // Move only this isolated demo window; never move the user's pointer.
                Native.Point cursor; Native.Rect bounds;
                Native.GetCursorPos(out cursor);
                IntPtr smokeHandle = new System.Windows.Interop.WindowInteropHelper(shelf).Handle;
                int extraCaptures = Math.Min(57, (int)Math.Ceiling(shelf.Width / 140) + 5);
                for (int i = 0; i < extraCaptures; i++)
                {
                    var fixture = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null,
                        new byte[] { (byte)(40 + i), 130, 190, 255 }, 4);
                    fixture.Freeze(); store.Add(fixture);
                }
                int expectedCaptures = store.Items.Count;
                // Send a horizontal-wheel message only to this isolated test window.
                Native.SendMessage(smokeHandle, 0x020E, new IntPtr(120 << 16), IntPtr.Zero);
                bool smoothStart = shelf.ScrollTarget > 0 && shelf.ScrollOffset == 0;
                Native.GetWindowRect(smokeHandle, out bounds);
                var area = Forms.Screen.FromHandle(smokeHandle).WorkingArea;
                int y = cursor.Y < area.Top + area.Height / 2 ? area.Bottom - (bounds.Bottom - bounds.Top) : area.Top;
                Native.SetWindowPos(smokeHandle, new IntPtr(-1), bounds.Left, y, 0, 0, 0x0011);
                // Wait past the auto-hide interval with the real window timer running.
                var smokeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3200) };
                smokeTimer.Tick += delegate
                {
                    smokeTimer.Stop();
                    int result = 0;
                    string report;
                    try
                    {
                        if (!shelf.IsVisible) throw new InvalidOperationException("手动展开的窗口在 3.2 秒内自动隐藏");
                        if (store.Items.Count != expectedCaptures || !tray.Visible) throw new InvalidOperationException("启动状态不完整");
                        bool startupSettingDisabled = false;
                        foreach (Forms.ToolStripItem item in tray.ContextMenuStrip.Items)
                        {
                            var setting = item as Forms.ToolStripMenuItem;
                            if (setting != null && setting.Text == "开机自启（登录后）")
                                startupSettingDisabled = !setting.Enabled && !setting.Checked;
                        }
                        if (!startupSettingDisabled) throw new InvalidOperationException("演示模式必须显示并禁用开机自启设置");
                        if (!smoothStart || shelf.ScrollOffset <= 0 || Math.Abs(shelf.ScrollOffset - shelf.ScrollTarget) > .3)
                            throw new InvalidOperationException("原生横向滚轮消息或实际滚动动画未完成");
                        shelf.Toggle(); if (shelf.IsVisible) throw new InvalidOperationException("窗口无法收起");
                        shelf.Toggle(); if (!shelf.IsVisible) throw new InvalidOperationException("窗口无法重新展开");
                        Native.Rect screenWindow; Native.GetWindowRect(smokeHandle, out screenWindow);
                        var display = Forms.Screen.FromHandle(smokeHandle).Bounds;
                        if (screenWindow.Left != display.Left || Math.Abs((screenWindow.Right - screenWindow.Left) - display.Width) > 1)
                            throw new InvalidOperationException("窗口未覆盖当前显示器完整宽度");
                        var layerProbe = new Window { Width = 64, Height = 64, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                        layerProbe.Show();
                        IntPtr probeHandle = new System.Windows.Interop.WindowInteropHelper(layerProbe).Handle;
                        store.SetLayer(WindowLayer.Bottom);
                        shelf.Reveal(true);
                        bool belowProbe = false;
                        IntPtr nextWindow = probeHandle;
                        for (int i = 0; i < 10000 && nextWindow != IntPtr.Zero; i++)
                        {
                            nextWindow = Native.GetWindow(nextWindow, 2);
                            if (nextWindow == smokeHandle) { belowProbe = true; break; }
                        }
                        layerProbe.Close();
                        if (shelf.Topmost || (Native.GetWindowLongPtr(smokeHandle, -20).ToInt64() & 8) != 0 || !belowProbe)
                            throw new InvalidOperationException("置底配置未将窗口放在普通测试窗口下方");
                        store.SetLayer(WindowLayer.Normal);
                        if (shelf.Topmost || (Native.GetWindowLongPtr(smokeHandle, -20).ToInt64() & 8) != 0)
                            throw new InvalidOperationException("普通层级仍然置顶");
                        store.SetLayer(WindowLayer.Top);
                        if (!shelf.Topmost || (Native.GetWindowLongPtr(smokeHandle, -20).ToInt64() & 8) == 0)
                            throw new InvalidOperationException("置顶配置未恢复原生窗口置顶属性");
                        var preview = new Preview(store, store.Items[store.Items.Count - 1], true);
                        preview.Show(); preview.UpdateLayout(); preview.FitImage(); preview.ChangeZoom(2); preview.UpdateLayout();
                        preview.Rotate(90); var rotatedPreview = preview.RenderEdited();
                        if (rotatedPreview.PixelWidth != Store.ReadImage(store.Items[store.Items.Count - 1].Path).PixelHeight)
                            throw new InvalidOperationException("实际预览窗口旋转保存尺寸异常");
                        preview.FitImage(); preview.UpdateLayout();
                        store.SetLayer(WindowLayer.Bottom);
                        if (preview.Topmost) throw new InvalidOperationException("预览窗口未同步置底配置");
                        store.SetLayer(WindowLayer.Top);
                        if (!preview.Topmost) throw new InvalidOperationException("预览窗口未同步置顶配置");
                        preview.Close();
                        CaptureTests.VerifyDesktopPixels();
                        var content = (FrameworkElement)shelf.Content; content.UpdateLayout();
                        // Export an independent layout so compositor caches cannot omit
                        // transparent-window visuals in RenderTargetBitmap.
                        var exportShelf = new Shelf(store, true);
                        exportShelf.ApplyLayout(shelf.Width);
                        var exportContent = (FrameworkElement)exportShelf.Content;
                        exportContent.Measure(new Size(shelf.Width, shelf.Height));
                        exportContent.Arrange(new Rect(0, 0, shelf.Width, shelf.Height));
                        exportContent.UpdateLayout();
                        var render = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                        render.Render(exportContent); exportShelf.Dispose(); exportShelf.Close();
                        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(smokeReport), "running-shelf.png"), Store.Encode(render));
                        report = "PASS startup setting is visible, disabled and unchecked in demo mode\nPASS native desktop capture reads the exact opaque RGB pixels and physical size of an isolated synthetic window patch\nPASS persisted top/normal/bottom layers update native topmost style and bottom remains below an independent normal window after reveal; preview follows live changes\nPASS real preview window opens, fits, zooms and renders a quarter-turn image at original resolution\nPASS native horizontal-wheel message starts smooth movement and real DispatcherTimer animation settles on target\nPASS manual reveal remains visible after 3.2 seconds with real auto-hide timer enabled and initial pointer outside\nPASS native window matches current monitor left edge and complete physical width\nPASS application startup, tray initialization, demo captures, window hide/reveal, annotation window startup, independent WPF layout export\nPASS real clipboard and screenshot directory monitoring disabled in smoke test\n";
                    }
                    catch (Exception ex) { result = 1; report = "FAIL " + ex; }
                    Directory.CreateDirectory(Path.GetDirectoryName(smokeReport)); File.WriteAllText(smokeReport, report);
                    Shutdown(result);
                };
                smokeTimer.Start();
            }
        }
        private async void StartCapture()
        {
            if (exiting || shortcutDialogOpen || captureStarting || captureWindow != null) return;
            captureStarting = true;
            bool restoreShelf = shelf.IsVisible;
            shelf.Busy = true; shelf.Hide();
            try
            {
                // Let the desktop compositor remove our shelf and the tray menu first.
                await Task.Delay(160);
                if (exiting) return;
                ScreenCapture image;
                if (demo)
                {
                    var bounds = Forms.Screen.PrimaryScreen.Bounds;
                    var sample = new TransformedBitmap(DemoImage(0), new ScaleTransform(bounds.Width / 640.0, bounds.Height / 400.0)); sample.Freeze();
                    image = new ScreenCapture { Bounds = bounds, Image = sample };
                    image.Windows.Add(new CaptureTarget(IntPtr.Zero, new Rect(100, 100, Math.Max(1, bounds.Width - 300), Math.Max(1, bounds.Height - 300))));
                }
                else image = ScreenCapture.Take();
                captureWindow = new CaptureWindow(image, async delegate(BitmapSource result)
                {
                    await store.AddAsync(delegate { return result; });
                    if (!captureDemo) { Clipboard.SetImage(result); clipboardSequence = Native.GetClipboardSequenceNumber(); }
                    if (tray != null) { tray.BalloonTipTitle = "截图已完成"; tray.BalloonTipText = "已复制到剪贴板，并收进晾衣绳。"; tray.ShowBalloonTip(2000); }
                }, CopyPickedColor, delegate(BitmapSource result) { pins.Show(result); });
                if (captureDemo) captureWindow.ShowInTaskbar = true;
                captureWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                if (!(ex is System.ComponentModel.Win32Exception || ex is NotSupportedException || ex is System.Runtime.InteropServices.ExternalException)) throw;
                MessageBox.Show("无法开始截图：" + ex.Message, "Snapline 截图");
            }
            finally
            {
                captureWindow = null; captureStarting = false; shelf.Busy = false;
                if (restoreShelf && !exiting) shelf.Reveal(false);
                if (captureDemo && !pins.HasImages) Shutdown();
            }
        }
        private void CopyPickedColor(string value)
        {
            if (demo) return;
            Clipboard.SetText(value); clipboardSequence = Native.GetClipboardSequenceNumber();
        }
        private void CopyPinnedImage(BitmapSource image)
        {
            if (demo) return;
            Clipboard.SetImage(image); clipboardSequence = Native.GetClipboardSequenceNumber();
        }
        private void ShowPin(BitmapSource image)
        {
            try { pins.Show(image); }
            catch (NotSupportedException ex) { MessageBox.Show(ex.Message, "Snapline 贴图"); }
        }
        private async void PastePin()
        {
            if (exiting || shortcutDialogOpen || pinBusy) return;
            if (captureWindow != null) { captureWindow.PinSelection(); return; }
            if (captureStarting) return;
            pinBusy = true;
            try
            {
                if (demo) { pins.Show(PinBoard.FromText("#2580DB"), uint.MaxValue); return; }
                if (pinWorker == null) pinWorker = new ImageWorker();
                var content = await pinWorker.Run(delegate
                {
                    uint sequence = Native.GetClipboardSequenceNumber();
                    BitmapSource image = ClipboardImages.Read(delegate(string format) { return Clipboard.ContainsData(format) ? Clipboard.GetData(format) : null; }, delegate { return Clipboard.ContainsImage() ? Clipboard.GetImage() : null; });
                    if (image == null && Clipboard.ContainsText()) image = PinBoard.FromText(Clipboard.GetText());
                    if (Native.GetClipboardSequenceNumber() != sequence) throw new System.Runtime.InteropServices.ExternalException();
                    if (image != null && !image.IsFrozen) image.Freeze();
                    return new ClipboardPin { Image = image, Sequence = sequence };
                });
                if (exiting) return;
                if (content.Image != null) pins.Show(content.Image, content.Sequence);
                else if (pins.RestoreLast() == null) MessageBox.Show("剪贴板中没有可贴出的图片、颜色或文字。请先截图或复制内容。", "Snapline 贴图");
            }
            catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show("剪贴板正被占用或内容刚刚变化，请再次按贴图快捷键。", "Snapline 贴图"); }
            catch (NotSupportedException ex) { MessageBox.Show(ex.Message, "Snapline 贴图"); }
            finally { pinBusy = false; }
        }
        private void ConfigureHotkey(CaptureHotkey hotkey, bool paste, bool toggle = false)
        {
            if (hotkey == null) return;
            shortcutDialogOpen = true;
            try { hotkey.Configure(store, paste, toggle); }
            finally { shortcutDialogOpen = false; }
        }
        private void SetupCollector()
        {
            if (collector != null) { collector.Dispose(); collector = null; }
            if (!store.Settings.WatchEnabled || string.IsNullOrWhiteSpace(store.Settings.WatchFolder)) return;
            collector = new FolderCollector(store.Settings.WatchFolder,
                delegate(string path)
                {
                    return (Task<bool>)Dispatcher.Invoke(new Func<Task<bool>>(delegate { return store.ImportAsync(path); }));
                }, store.Report);
            collector.Start();
        }
        private async void CollectClipboard()
        {
            if (clipboardBusy || exiting) return;
            uint sequence = Native.GetClipboardSequenceNumber();
            bool retryPending = store.HasPendingClipboard;
            if (!retryPending && sequence == clipboardSequence) return;
            if (!store.Settings.ClipboardEnabled) { clipboardSequence = sequence; return; }
            if (retryPending)
            {
                if (DateTime.UtcNow < clipboardRetryAt) return;
                sequence = pendingClipboardSequence;
            }
            try
            {
                clipboardBusy = true;
                // Only request image data. Never read clipboard text, filenames, or history.
                if (retryPending) await store.RetryClipboardAsync();
                else await store.AddAsync(delegate
                {
                    if (Native.GetClipboardSequenceNumber() != sequence) throw new System.Runtime.InteropServices.ExternalException();
                    var image = ClipboardImages.Read(
                        delegate(string format) { return Clipboard.ContainsData(format) ? Clipboard.GetData(format) : null; },
                        delegate { return Clipboard.ContainsImage() ? Clipboard.GetImage() : null; });
                    if (Native.GetClipboardSequenceNumber() != sequence) throw new System.Runtime.InteropServices.ExternalException();
                    return image;
                }, true);
                if (Native.GetClipboardSequenceNumber() == sequence) clipboardSequence = sequence;
            }
            catch (System.Runtime.InteropServices.ExternalException) { /* A busy clipboard is retried next tick. */ }
            catch (Exception)
            {
                if (store.HasPendingClipboard)
                {
                    pendingClipboardSequence = sequence;
                    clipboardRetryAt = DateTime.UtcNow.AddSeconds(5);
                    shelf.Status("图片尚未保存，已保留并等待重试，请检查磁盘空间与权限。");
                }
                else
                {
                    if (Native.GetClipboardSequenceNumber() == sequence) clipboardSequence = sequence;
                    shelf.Status("剪贴板图片无法收集，可尝试手动导入。");
                }
            }
            finally { clipboardBusy = false; }
        }
        private void SetupTray()
        {
            tray = new Forms.NotifyIcon();
            tray.Icon = AppIcon.TrayImage();
            tray.Text = demo ? "截图晾衣绳 · 演示模式" : "截图晾衣绳";
            var menu = new Forms.ContextMenuStrip();
            var capture = menu.Items.Add("截图", null, delegate { StartCapture(); });
            capture.Enabled = !demo;
            var configureCapture = menu.Items.Add("截图快捷键…", null, delegate
            {
                ConfigureHotkey(captureHotkey, false);
            });
            configureCapture.Enabled = !demo;
            var paste = menu.Items.Add("贴到屏幕", null, delegate { PastePin(); });
            var togglePins = menu.Items.Add("隐藏 / 显示全部贴图", null, delegate { pins.ToggleAll(); });
            menu.Items.Add("恢复上一张关闭的贴图", null, delegate
            {
                try { pins.RestoreLast(); }
                catch (NotSupportedException ex) { MessageBox.Show(ex.Message, "Snapline 贴图"); }
            });
            var configurePin = menu.Items.Add("贴图快捷键…", null, delegate { ConfigureHotkey(pinHotkey, true); }); configurePin.Enabled = !demo;
            var configureToggle = menu.Items.Add("隐藏贴图快捷键…", null, delegate { ConfigureHotkey(togglePinsHotkey, false, true); }); configureToggle.Enabled = !demo;
            menu.Opening += delegate
            {
                capture.Text = "截图    " + store.Settings.CaptureHotkey + (captureHotkey != null && !captureHotkey.Registered ? "（快捷键未启用）" : "");
                paste.Text = "贴到屏幕    " + store.Settings.PinHotkey + (pinHotkey != null && !pinHotkey.Registered ? "（快捷键未启用）" : "");
                togglePins.Text = "隐藏 / 显示全部贴图    " + store.Settings.TogglePinsHotkey + (togglePinsHotkey != null && !togglePinsHotkey.Registered ? "（快捷键未启用）" : "");
            };
            menu.Items.Add("显示 / 隐藏    Ctrl+Alt+T", null, delegate { shelf.Toggle(); });
            menu.Items.Add("导入图片…", null, delegate { shelf.ImportDialog(); });
            var layers = new Forms.ToolStripMenuItem("窗口层级");
            foreach (WindowLayer layer in Enum.GetValues(typeof(WindowLayer)))
            {
                WindowLayer selectedLayer = layer;
                var item = new Forms.ToolStripMenuItem(Native.LayerName(layer)) { Tag = layer };
                item.Click += delegate { store.SetLayer(selectedLayer); }; layers.DropDownItems.Add(item);
            }
            layers.DropDownOpening += delegate
            {
                foreach (Forms.ToolStripMenuItem item in layers.DropDownItems) item.Checked = (WindowLayer)item.Tag == store.Settings.Layer;
            };
            menu.Items.Add(layers);
            menu.Items.Add(new Forms.ToolStripSeparator());
            var startupRegistration = new StartupRegistration(Forms.Application.ExecutablePath);
            var startWithWindows = new Forms.ToolStripMenuItem("开机自启（登录后）") { Enabled = !demo };
            startWithWindows.ToolTipText = "登录 Windows 后自动驻留托盘；启用后请保留程序在当前位置。";
            startWithWindows.Click += delegate
            {
                try
                {
                    bool enabled = !startupRegistration.Enabled;
                    startupRegistration.SetEnabled(enabled);
                    startWithWindows.Checked = enabled;
                }
                catch (Exception ex)
                {
                    if (!IsStartupSettingError(ex)) throw;
                    MessageBox.Show("无法修改开机自启设置：" + ex.Message, "Snapline 开机自启", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            menu.Opening += delegate
            {
                if (demo) return;
                try
                {
                    startWithWindows.Checked = startupRegistration.Enabled;
                    startWithWindows.Enabled = true;
                    startWithWindows.Text = "开机自启（登录后）";
                }
                catch (Exception ex)
                {
                    if (!IsStartupSettingError(ex)) throw;
                    startWithWindows.Checked = false; startWithWindows.Enabled = false;
                    startWithWindows.Text = "开机自启（无法读取设置）";
                }
            };
            menu.Items.Add(startWithWindows);
            var clipboard = new Forms.ToolStripMenuItem("收集剪贴板图片（含 Win+Shift+S）");
            clipboard.CheckOnClick = true; clipboard.Checked = store.Settings.ClipboardEnabled && !demo; clipboard.Enabled = !demo;
            clipboard.CheckedChanged += delegate { store.Settings.ClipboardEnabled = clipboard.Checked; clipboardSequence = Native.GetClipboardSequenceNumber(); store.SaveSettings(); };
            menu.Items.Add(clipboard);
            var watch = new Forms.ToolStripMenuItem("收集截图文件夹的新图片");
            watch.CheckOnClick = true; watch.Checked = store.Settings.WatchEnabled && !demo; watch.Enabled = !demo;
            watch.CheckedChanged += delegate { store.Settings.WatchEnabled = watch.Checked; store.SaveSettings(); SetupCollector(); };
            menu.Items.Add(watch);
            var choose = menu.Items.Add("选择截图文件夹…", null, delegate
            {
                using (var dialog = new Forms.FolderBrowserDialog())
                {
                    dialog.Description = "仅收集此文件夹启动监听后新增的图片；不会删除或移动原文件。";
                    if (Directory.Exists(store.Settings.WatchFolder)) dialog.SelectedPath = store.Settings.WatchFolder;
                    if (dialog.ShowDialog() == Forms.DialogResult.OK)
                    {
                        store.Settings.WatchFolder = dialog.SelectedPath; store.Settings.WatchEnabled = true;
                        watch.Checked = true; store.SaveSettings(); SetupCollector();
                    }
                }
            });
            choose.Enabled = !demo;
            menu.Items.Add("打开收集文件夹", null, delegate { System.Diagnostics.Process.Start("explorer.exe", store.Inbox); });
            menu.Items.Add("使用说明", null, delegate
            {
                MessageBox.Show(
                    "截图晾衣绳 · Snapline\n分辨率网络 出品 · 把截图留在手边\n\n" +
                    "【常用快捷键】\n" +
                    store.Settings.CaptureHotkey + " 截图；" + store.Settings.PinHotkey + " 将剪贴板图片、文字或颜色贴到屏幕。\n" +
                    store.Settings.TogglePinsHotkey + " 隐藏 / 显示全部贴图；Ctrl+Alt+T 展开 / 收起晾衣绳。\n" +
                    "截图与贴图快捷键可在托盘修改；若被占用，请换一组或使用托盘入口。\n\n" +
                    "【截图与标注】\n" +
                    "拖动框选，或单击选中识别到的控件 / 窗口；滚轮切换父子范围。\n" +
                    "支持画笔、形状、箭头、文字和马赛克；Ctrl+Z 撤销，Ctrl+Y 重做。\n" +
                    "Enter / Ctrl+C 复制并收进晾衣绳；Ctrl+T 直接贴图；Ctrl+S 另存 PNG。\n" +
                    "选好区域后点击“取色器”，点击或按 C 复制颜色；Shift 切换 HEX / RGB。\n" +
                    "Esc 退出取色；未取色时按 Esc 取消截图。工具名称与快捷键可悬停查看。\n\n" +
                    "【桌面贴图】\n" +
                    "拖动移动，滚轮缩放，Ctrl+滚轮调整透明度；1 / 2 旋转，3 / 4 翻转。\n" +
                    "空格展开标注；Esc 先收起工具，再关闭贴图；Shift+Esc 彻底关闭。\n" +
                    "托盘可恢复上一张关闭的贴图；彻底关闭或退出软件后无法恢复。\n\n" +
                    "【晾衣绳与保存】\n" +
                    "单击图片复制，双击预览，右键标注或贴图；滚轮浏览，顶部 ＋ 导入。\n" +
                    "托盘可开关剪贴板 / 文件夹收集；开启剪贴板收集后也可接收 Win+Shift+S 截图。\n" +
                    "图片副本与设置保存在 %LOCALAPPDATA%\\Snapline，可从托盘打开收集文件夹。\n" +
                    "贴图仅在本次运行期间保留，需要长期保存请按 Ctrl+S 另存 PNG。\n" +
                    "软件在本地运行，不联网；自动收集仅读取图片，不读取文字或剪贴板历史。\n\n" +
                    "【特别致谢】\n" +
                    "感谢 Tendedero 带来的晾衣绳收纳灵感，\n" +
                    "感谢 Snipaste 在截图、贴图与取色交互上的启发。\n" +
                    "向两款产品的创作者致敬，感谢你们的用心。",
                    "使用说明 · 截图晾衣绳");
            });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出", null, async delegate
            {
                if (exiting) return;
                exiting = true;
                await store.WhenIdleAsync();
                if (store.HasPendingClipboard && MessageBox.Show("还有一张剪贴板图片因写盘失败尚未保存。恢复磁盘空间或目录权限后会自动重试。\n\n现在退出会丢失这张未保存图片，仍要退出吗？", "截图晾衣绳", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                { exiting = false; return; }
                if (clipboardTimer != null) clipboardTimer.Stop();
                if (captureWindow != null) captureWindow.Close();
                if (collector != null) collector.Dispose();
                shelf.IsEnabled = false; tray.Visible = false;
                await store.StopAsync(); Shutdown();
            });
            menu.Opening += delegate { shelf.Busy = true; };
            menu.Closed += delegate { shelf.Busy = false; };
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { shelf.Toggle(); };
            tray.Visible = true;
            if (!demo && !startup) { tray.BalloonTipTitle = "截图晾衣绳已就绪"; tray.BalloonTipText = store.Settings.CaptureHotkey + " 截图取色；" + store.Settings.PinHotkey + " 贴图；Ctrl+Alt+T 展开晾衣绳。"; tray.ShowBalloonTip(4000); }
        }
        private static bool IsStartupSettingError(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is NotSupportedException;
        }
        internal static BitmapSource DemoImage(int index)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var colors = new[] { Color.FromRgb(227, 238, 251), Color.FromRgb(248, 235, 218), Color.FromRgb(220, 240, 234) };
                dc.DrawRectangle(new SolidColorBrush(colors[index % 3]), null, new Rect(0, 0, 640, 400));
                var title = new System.Windows.Media.FormattedText(new[] { "周末的灵感", "一个好点子", "留住这一刻" }[index % 3], System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 30, new SolidColorBrush(Color.FromRgb(45, 62, 76)), 1.0);
                dc.DrawText(title, new Point(40, 42));
                dc.DrawRoundedRectangle(Brushes.White, null, new Rect(40, 108, 560, 242), 16, 16);
                for (int i = 0; i < 5; i++)
                {
                    dc.DrawRoundedRectangle(new SolidColorBrush(colors[index % 3]), null, new Rect(66, 137 + i * 36, 230 + i * 30, 12), 6, 6);
                    dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(94, 131, 167)), null, new Point(538, 156 + i * 34), 5 + i, 5 + i);
                }
            }
            var bitmap = new RenderTargetBitmap(640, 400, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual); bitmap.Freeze(); return bitmap;
        }
        protected override void OnExit(ExitEventArgs e)
        {
            if (clipboardTimer != null) clipboardTimer.Stop();
            if (collector != null) collector.Dispose();
            if (captureHotkey != null) captureHotkey.Dispose();
            if (pinHotkey != null) pinHotkey.Dispose();
            if (togglePinsHotkey != null) togglePinsHotkey.Dispose();
            if (pins != null) pins.Dispose();
            if (pinWorker != null) pinWorker.Dispose();
            if (shelf != null) shelf.Dispose();
            if (tray != null) { tray.Visible = false; var icon = tray.Icon; tray.Dispose(); if (icon != null) icon.Dispose(); }
            if (demoRoot != null && Path.GetDirectoryName(Path.GetFullPath(demoRoot)).TrimEnd(Path.DirectorySeparatorChar) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) && Path.GetFileName(demoRoot).StartsWith("Snapline-demo-", StringComparison.Ordinal))
            {
                try { Directory.Delete(demoRoot, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            base.OnExit(e);
        }
    }
}
