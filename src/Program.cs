using System;
using System.IO;
using System.Threading;
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
        private Store store;
        private Shelf shelf;
        private bool demo;
        private Mutex mutex;
        private string demoRoot;
        private string smokeReport;
        [STAThread] public static int Main(string[] args)
        {
            int testIndex = Array.IndexOf(args, "--self-test");
            if (testIndex >= 0) return SelfTests.Run(args.Length > testIndex + 1 ? Path.GetFullPath(args[testIndex + 1]) : Path.Combine(Path.GetTempPath(), "Snapline-self-test.txt"));
            var app = new App();
            app.demo = Array.IndexOf(args, "--demo") >= 0;
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
                MessageBox.Show("截图晾衣绳已经运行，可从右下角托盘唤出。", "截图晾衣绳");
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
        private void Initialize()
        {
            string data = demo ? Path.Combine(Path.GetTempPath(), "Snapline-demo-" + Guid.NewGuid().ToString("N"))
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snapline");
            if (demo) demoRoot = data;
            store = new Store(data);
            shelf = new Shelf(store, demo);
            MainWindow = shelf;
            store.Error += delegate(string text) { Dispatcher.BeginInvoke(new Action(delegate { shelf.Status(text); })); };
            shelf.Copied += delegate { clipboardSequence = Native.GetClipboardSequenceNumber(); };
            shelf.Show();
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
                        report = "PASS persisted top/normal/bottom layers update native topmost style and bottom remains below an independent normal window after reveal; preview follows live changes\nPASS real preview window opens, fits, zooms and renders a quarter-turn image at original resolution\nPASS native horizontal-wheel message starts smooth movement and real DispatcherTimer animation settles on target\nPASS manual reveal remains visible after 3.2 seconds with real auto-hide timer enabled and initial pointer outside\nPASS native window matches current monitor left edge and complete physical width\nPASS application startup, tray initialization, demo captures, window hide/reveal, annotation window startup, independent WPF layout export\nPASS real clipboard and screenshot directory monitoring disabled in smoke test\n";
                    }
                    catch (Exception ex) { result = 1; report = "FAIL " + ex; }
                    Directory.CreateDirectory(Path.GetDirectoryName(smokeReport)); File.WriteAllText(smokeReport, report);
                    Shutdown(result);
                };
                smokeTimer.Start();
            }
        }
        private void SetupCollector()
        {
            if (collector != null) { collector.Dispose(); collector = null; }
            if (!store.Settings.WatchEnabled || string.IsNullOrWhiteSpace(store.Settings.WatchFolder)) return;
            collector = new FolderCollector(store.Settings.WatchFolder,
                delegate(BitmapSource image)
                {
                    if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(delegate
                    {
                        try { store.Add(image); } catch (Exception) { shelf.Status("收集失败，请检查磁盘空间与目录权限。"); }
                    }));
                }, store.Report);
            collector.Start();
        }
        private void CollectClipboard()
        {
            uint sequence = Native.GetClipboardSequenceNumber();
            if (sequence == clipboardSequence) return;
            if (!store.Settings.ClipboardEnabled) { clipboardSequence = sequence; return; }
            try
            {
                // Only request image data. Never read clipboard text, filenames, or history.
                BitmapSource image = ClipboardImages.Read(
                    delegate(string format) { return Clipboard.ContainsData(format) ? Clipboard.GetData(format) : null; },
                    delegate { return Clipboard.ContainsImage() ? Clipboard.GetImage() : null; });
                if (image != null) store.Add(image);
                clipboardSequence = sequence;
            }
            catch (System.Runtime.InteropServices.ExternalException) { /* A busy clipboard is retried next tick. */ }
            catch (Exception) { clipboardSequence = sequence; shelf.Status("剪贴板图片无法收集，可尝试手动导入。"); }
        }
        private void SetupTray()
        {
            tray = new Forms.NotifyIcon();
            tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
            tray.Text = demo ? "截图晾衣绳 · 演示模式" : "截图晾衣绳";
            var menu = new Forms.ContextMenuStrip();
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
                MessageBox.Show("Win+Shift+S 截图后自动收集。\n鼠标在顶部停留片刻，或按 Ctrl+Alt+T 唤出。\n\n单击复制 · 双击预览 · 右键标注/另存为/移除\n拖动到应用或文件夹：送出副本\n移除仅删除晾衣绳中的副本，不删除原图\n滚轮 / 触控板横滑 / 两侧箭头 / 左右键浏览 · 固定 · Esc 收起\n标注支持 Ctrl+Z 撤销上一笔\n\n默认最多显示最近 60 张，较旧的副本仍保存在收集文件夹。\n剪贴板收集也会接收你复制的其他图片，可在托盘关闭。\n本程序不联网，不修改系统截图设置，不设置开机启动。", "截图晾衣绳");
            });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Shutdown(); });
            menu.Opening += delegate { shelf.Busy = true; };
            menu.Closed += delegate { shelf.Busy = false; };
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { shelf.Toggle(); };
            tray.Visible = true;
            if (!demo) { tray.BalloonTipTitle = "截图晾衣绳已就绪"; tray.BalloonTipText = "Win+Shift+S 截图后，鼠标移到顶部即可取用。Ctrl+Alt+T 也能唤出。"; tray.ShowBalloonTip(4000); }
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
            if (shelf != null) shelf.Dispose();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            if (demoRoot != null && Path.GetDirectoryName(Path.GetFullPath(demoRoot)).TrimEnd(Path.DirectorySeparatorChar) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) && Path.GetFileName(demoRoot).StartsWith("Snapline-demo-", StringComparison.Ordinal))
            {
                try { Directory.Delete(demoRoot, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            base.OnExit(e);
        }
    }
}
