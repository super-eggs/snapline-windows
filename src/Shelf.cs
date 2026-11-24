using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Snapline
{
    internal sealed class Shelf : Window, IDisposable
    {
        private readonly Store store;
        private readonly Canvas cards = new Canvas { ClipToBounds = true, IsManipulationEnabled = true };
        private readonly TextBlock status = new TextBlock { Foreground = Ui.LightText, FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        private readonly Border statusToast = new Border { Visibility = Visibility.Collapsed, Background = Ui.LightSurface, BorderBrush = Ui.LightEdge,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 8, 14, 8), HorizontalAlignment = HorizontalAlignment.Center, Effect = Ui.Shadow(2, 10, .12) };
        private readonly StackPanel tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        internal readonly Button ScrollLeftButton, ScrollRightButton;
        internal readonly Button PinButton;
        private readonly List<CardSlot> slots = new List<CardSlot>();
        private readonly Grid clothesline;
        private readonly TranslateTransform slide = new TranslateTransform();
        private readonly DispatcherTimer scrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        private DateTime lastScrollFrame;
        private double scrollOffset, scrollTarget, contentWidth;
        private bool leftHover, rightHover;
        private double maskWidth;
        private bool maskLeft, maskRight;
        private Border emptyMessage;
        private TextBlock emptyHint;
        private sealed class CardSlot
        {
            internal Shot Shot;
            internal Border View;
            internal ImageSource Image;
            internal readonly TranslateTransform Position = new TranslateTransform();
            internal double X, Width;
        }
        internal double ScrollOffset { get { return scrollOffset; } }
        internal double ScrollTarget { get { return scrollTarget; } }
        internal double MaxScroll { get { return Math.Max(0, contentWidth + 32 - Width); } }
        private DateTime statusUntil;
        private static readonly TimeSpan HoverInterval = TimeSpan.FromMilliseconds(120);
        private static readonly TimeSpan ClosingHoverInterval = TimeSpan.FromMilliseconds(16);
        private readonly DispatcherTimer timer = new DispatcherTimer { Interval = HoverInterval };
        private HwndSource source;
        private IntPtr handle;
        private DateTime leaveSince = DateTime.MinValue;
        private DateTime revealedAt;
        private bool pinned, disposed, manualReveal, busy;
        private bool autoHiding;
        internal bool Busy
        {
            get { return busy; }
            set
            {
                if (busy == value) return;
                busy = value; leaveSince = DateTime.MinValue;
                // Give the pointer time to return after a menu/dialog/drag operation.
                if (!busy) revealedAt = DateTime.UtcNow;
            }
        }
        internal event Action Copied;
        internal event Action<System.Windows.Media.Imaging.BitmapSource> PinRequested;

        internal Shelf(Store store)
        {
            this.store = store;
            Title = "截图晾衣绳"; Width = 1100; Height = 350;
            Icon = AppIcon.WindowImage();
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; Topmost = store.Settings.Layer == WindowLayer.Top; ShowActivated = false;
            FontFamily = Ui.Font; Ui.StyleToolTips(this, true);
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            var panel = new Grid { RenderTransform = slide };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            tools.Children.Add(Ui.IconButton("import", "导入图片", ImportDialog, true));
            tools.Children.Add(Ui.Divider(true));
            PinButton = Ui.IconButton("pin", "固定展开 / 自动收起", null, true);
            PinButton.Click += delegate
            {
                pinned = !pinned; leaveSince = DateTime.MinValue; Ui.Select(PinButton, pinned, true);
                PinButton.ToolTip = pinned ? "已固定，点击恢复自动收起" : "固定展开 / 自动收起";
            };
            tools.Children.Add(PinButton);
            tools.Children.Add(Ui.IconButton("collapse", "收起（Esc）", delegate { Hide(); }, true));
            var toolSurface = new Border { Child = tools, Background = new LinearGradientBrush(Color.FromRgb(255, 254, 251), Color.FromRgb(245, 243, 237), 90),
                BorderBrush = Ui.LightEdge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(5),
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 16, 0), Effect = Ui.Shadow(3, 12, .12) };
            panel.Children.Add(toolSurface);
            clothesline = new Grid { Height = 200, Margin = new Thickness(0, 10, 0, 8), VerticalAlignment = VerticalAlignment.Center };
            clothesline.Children.Add(new Clothesline { IsHitTestVisible = false });
            clothesline.Children.Add(cards);
            ScrollLeftButton = Ui.IconButton("left", "向左滚动", delegate { ScrollBy(-Width * .65); }, true);
            ScrollRightButton = Ui.IconButton("right", "向右滚动", delegate { ScrollBy(Width * .65); }, true);
            ScrollLeftButton.Background = ScrollRightButton.Background = Ui.LightSurface;
            ScrollLeftButton.BorderBrush = ScrollRightButton.BorderBrush = Ui.LightEdge;
            ScrollLeftButton.Effect = Ui.Shadow(2, 10, .16); ScrollRightButton.Effect = Ui.Shadow(2, 10, .16);
            ScrollLeftButton.Width = ScrollRightButton.Width = 40;
            ScrollLeftButton.Height = ScrollRightButton.Height = 40;
            ScrollLeftButton.HorizontalAlignment = HorizontalAlignment.Left;
            ScrollRightButton.HorizontalAlignment = HorizontalAlignment.Right;
            ScrollLeftButton.VerticalAlignment = ScrollRightButton.VerticalAlignment = VerticalAlignment.Center;
            ScrollLeftButton.Margin = ScrollRightButton.Margin = new Thickness(6, 0, 6, 0);
            clothesline.Children.Add(ScrollLeftButton); clothesline.Children.Add(ScrollRightButton);
            clothesline.MouseMove += delegate(object sender, MouseEventArgs e) { UpdateEdgeHover(e.GetPosition(clothesline)); };
            clothesline.MouseLeave += delegate { leftHover = rightHover = false; UpdateEdges(); };
            cards.ManipulationStarting += delegate(object sender, ManipulationStartingEventArgs e)
            {
                e.ManipulationContainer = clothesline; e.Mode = ManipulationModes.TranslateX; e.Handled = true;
            };
            cards.ManipulationDelta += delegate(object sender, ManipulationDeltaEventArgs e)
            {
                ScrollBy(-e.DeltaManipulation.Translation.X); e.Handled = true;
            };
            Grid.SetRow(clothesline, 1); panel.Children.Add(clothesline);
            statusToast.Child = status; statusToast.Margin = new Thickness(16, 0, 16, 8); statusToast.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetRowSpan(statusToast, 3); Panel.SetZIndex(statusToast, 2); panel.Children.Add(statusToast);
            // Clip the sliding panel at the monitor's working-area edge while keeping the hover bounds fixed.
            Content = new Border { Background = Brushes.Transparent, Child = panel, ClipToBounds = true };
            IsVisibleChanged += delegate { if (!IsVisible) ResetSlide(); };
            AllowDrop = true;
            DragOver += delegate(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            Drop += delegate(object sender, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) ImportPaths((string[])e.Data.GetData(DataFormats.FileDrop));
                e.Handled = true;
            };
            PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e) { ScrollBy(-e.Delta * .9); e.Handled = true; };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) Hide();
                if (e.Key == Key.Left || e.Key == Key.Right) { ScrollBy(e.Key == Key.Left ? -150 : 150); e.Handled = true; }
            };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!disposed) { e.Cancel = true; Hide(); } };
            SourceInitialized += delegate
            {
                handle = new WindowInteropHelper(this).Handle;
                source = HwndSource.FromHwnd(handle); source.AddHook(Hook);
                ApplyLayer();
            };
            store.Changed += Changed;
            store.LayerChanged += ApplyLayer;
            Activated += delegate { if (store.Settings.Layer == WindowLayer.Bottom) ApplyLayer(); };
            timer.Tick += delegate { Tick(); };
            IsVisibleChanged += delegate
            {
                if (IsVisible) timer.Start();
                else timer.Stop();
            };
            scrollTimer.Tick += delegate
            {
                DateTime now = DateTime.UtcNow;
                StepScroll(Math.Max(1, Math.Min(100, (now - lastScrollFrame).TotalMilliseconds)));
                lastScrollFrame = now;
            };
            ApplyLayout(1100);
        }
        private static Button MakeButton(string text, string tip, Action action)
        {
            var button = Ui.Button(text, tip, action, true); button.Margin = new Thickness(5, 0, 0, 0);
            return button;
        }
        private IntPtr Hook(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if (message == 0x020E && IsVisible)
            {
                ScrollBy(unchecked((short)(wparam.ToInt64() >> 16)) * .9); handled = true;
            }
            if (message == 0x02E0 && IsVisible) Dispatcher.BeginInvoke(new Action(delegate { Position(Forms.Screen.FromHandle(handle)); }));
            return IntPtr.Zero;
        }
        private void Changed() { RenderCards(); if (IsVisible) leaveSince = DateTime.MinValue; }
        private void ApplyLayer() { Native.ApplyLayer(this, store.Settings.Layer); }
        internal void ScrollBy(double distance)
        {
            scrollTarget = Math.Max(0, Math.Min(MaxScroll, scrollTarget + distance));
            lastScrollFrame = DateTime.UtcNow; scrollTimer.Start();
            leaveSince = DateTime.MinValue;
        }
        internal void StepScroll(double milliseconds)
        {
            scrollOffset += (scrollTarget - scrollOffset) * (1 - Math.Exp(-milliseconds / 45.0));
            if (Math.Abs(scrollTarget - scrollOffset) < .3) { scrollOffset = scrollTarget; scrollTimer.Stop(); }
            PositionCards();
        }
        private void Position(Forms.Screen screen)
        {
            double scale = handle == IntPtr.Zero ? 1 : Native.GetDpiForWindow(handle) / 96.0;
            if (scale <= 0) scale = 1;
            ApplyLayout(screen.Bounds.Width / scale);
            int width = (int)(Width * scale), height = (int)(Height * scale);
            Native.SetWindowPos(handle, new IntPtr(store.Settings.Layer == WindowLayer.Top ? -1 : store.Settings.Layer == WindowLayer.Bottom ? 1 : -2), screen.Bounds.Left, screen.WorkingArea.Top, width, height, 0x0010);
        }
        internal void ApplyLayout(double availableWidth)
        {
            double width = Math.Max(280, availableWidth);
            if (Width == width && Height == 280) return;
            Width = width; Height = 280;
            RenderCards();
        }
        internal Size ImageSize(Shot shot)
        {
            double scale = Math.Min(Math.Min(180, Width - 40) / shot.Image.PixelWidth, 120.0 / shot.Image.PixelHeight);
            return new Size(shot.Image.PixelWidth * scale, shot.Image.PixelHeight * scale);
        }
        private void UpdateEdgeHover(Point point)
        {
            bool inside = point.Y >= 0 && point.Y <= clothesline.ActualHeight;
            leftHover = inside && point.X >= 0 && point.X < 56;
            rightHover = inside && point.X <= Width && point.X > Width - 56;
            UpdateEdges();
        }
        private void UpdateEdges()
        {
            bool left = scrollOffset > .5, right = scrollOffset < MaxScroll - .5;
            ScrollLeftButton.Visibility = left && leftHover ? Visibility.Visible : Visibility.Collapsed;
            ScrollRightButton.Visibility = right && rightHover ? Visibility.Visible : Visibility.Collapsed;
            ScrollLeftButton.IsEnabled = scrollTarget > .5;
            ScrollRightButton.IsEnabled = scrollTarget < MaxScroll - .5;
            if (cards.OpacityMask != null && maskWidth == Width && maskLeft == left && maskRight == right) return;
            maskWidth = Width; maskLeft = left; maskRight = right;
            double fade = Math.Min(48 / Width, .2);
            var mask = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            mask.GradientStops.Add(new GradientStop(left ? Colors.Transparent : Colors.White, 0));
            mask.GradientStops.Add(new GradientStop(Colors.White, fade));
            mask.GradientStops.Add(new GradientStop(Colors.White, 1 - fade));
            mask.GradientStops.Add(new GradientStop(right ? Colors.Transparent : Colors.White, 1));
            mask.Freeze(); cards.OpacityMask = mask;
        }
        internal void Reveal(bool activate)
        {
            Native.Point point; Native.GetCursorPos(out point);
            var screen = Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y));
            BeginReveal(activate, DateTime.UtcNow);
            bool slideIn = !activate && SystemParameters.ClientAreaAnimation && (!IsVisible || autoHiding);
            if (!slideIn) ResetSlide();
            else if (!IsVisible) slide.Y = -Height;
            Show(); Position(screen);
            if (slideIn) AnimateSlide(false);
            if (activate && store.Settings.Layer != WindowLayer.Bottom) Activate();
        }
        private void ResetSlide()
        {
            autoHiding = false;
            timer.Interval = HoverInterval;
            slide.Y = 0;
            slide.BeginAnimation(TranslateTransform.YProperty, null);
        }
        private void AnimateSlide(bool closing)
        {
            autoHiding = closing;
            timer.Interval = closing ? ClosingHoverInterval : HoverInterval;
            double start = slide.Y;
            double destination = closing ? -Height : 0;
            // A short reversal should not take as long as the full trip.
            double distanceRatio = Math.Min(1, Math.Abs(destination - start) / Height);
            double milliseconds = Math.Max(90, (closing ? 260 : 300) * Math.Sqrt(distanceRatio));
            var animation = new DoubleAnimation(start, destination, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            if (closing) animation.Completed += FinishAutoHide;
            else animation.Completed += delegate { ResetSlide(); };
            // Keep the current offset in place while WPF schedules the next animation frame.
            slide.Y = start;
            slide.BeginAnimation(TranslateTransform.YProperty, animation);
        }
        private void CancelAutoHide()
        {
            BeginReveal(false, DateTime.UtcNow);
            AnimateSlide(false);
        }
        private void FinishAutoHide(object sender, EventArgs e)
        {
            if (!autoHiding || disposed) return;
            Native.Point cursor;
            if (Busy || pinned || (Native.GetCursorPos(out cursor) && ContainsCursor(cursor))) CancelAutoHide();
            else Hide();
        }
        internal void Toggle() { if (IsVisible) Hide(); else Reveal(true); }
        internal void Status(string text)
        {
            status.Text = text; statusToast.Visibility = Visibility.Visible;
            statusUntil = DateTime.UtcNow.AddSeconds(3);
        }
        internal void BeginReveal(bool manual, DateTime now)
        {
            manualReveal = manual; revealedAt = now;
            leaveSince = DateTime.MinValue;
        }
        internal bool AutoHideDue(bool inside, DateTime now)
        {
            // Explicit tray/hotkey reveals stay open until explicitly dismissed.
            // Hover reveals retain automatic hiding after a short opening grace period.
            if (manualReveal || pinned || Busy || inside || (now - revealedAt).TotalSeconds < 2)
            {
                leaveSince = DateTime.MinValue; return false;
            }
            if (leaveSince == DateTime.MinValue) { leaveSince = now; return false; }
            return (now - leaveSince).TotalMilliseconds > 1000;
        }
        private void Tick()
        {
            if (statusToast.Visibility == Visibility.Visible && DateTime.UtcNow >= statusUntil) statusToast.Visibility = Visibility.Collapsed;
            if (Busy || disposed || !IsVisible) return;
            Native.Point cursor; if (!Native.GetCursorPos(out cursor)) return;
            bool inside = ContainsCursor(cursor);
            if (autoHiding)
            {
                if (inside) CancelAutoHide();
                return;
            }
            UpdateEdgeHover(clothesline.PointFromScreen(new Point(cursor.X, cursor.Y)));
            if (!AutoHideDue(inside, DateTime.UtcNow)) return;
            if (SystemParameters.ClientAreaAnimation) AnimateSlide(true);
            else Hide();
        }
        private bool ContainsCursor(Native.Point cursor)
        {
            Native.Rect bounds;
            return Native.GetWindowRect(handle, out bounds)
                && cursor.X >= bounds.Left && cursor.X <= bounds.Right && cursor.Y >= bounds.Top && cursor.Y <= bounds.Bottom;
        }
        internal void RenderCards()
        {
            var images = store.Items.Where(shot => shot.Image != null).ToList();
            CardSlot anchor = scrollOffset > .5 || scrollTarget > .5
                ? slots.FirstOrDefault(slot => slot.X + slot.Width > scrollOffset + 16 && images.Contains(slot.Shot)) : null;
            double anchorX = anchor == null ? 0 : anchor.X - scrollOffset;
            double pending = scrollTarget - scrollOffset;
            var previous = slots.ToDictionary(slot => slot.Shot);
            foreach (CardSlot slot in slots)
                if (!images.Contains(slot.Shot)) cards.Children.Remove(slot.View);
            slots.Clear();
            contentWidth = images.Sum(shot => ImageSize(shot).Width + 8 + 24);
            double left = MaxScroll > 0 ? 16 : (Width - contentWidth) / 2;
            foreach (Shot shot in images)
            {
                double cardWidth = ImageSize(shot).Width + 8;
                CardSlot slot;
                if (!previous.TryGetValue(shot, out slot) || slot.Width != cardWidth || !ReferenceEquals(slot.Image, shot.Image))
                {
                    if (slot != null) cards.Children.Remove(slot.View);
                    slot = new CardSlot { Shot = shot, View = (Border)Card(shot), Image = shot.Image, Width = cardWidth };
                    slot.View.RenderTransform = slot.Position;
                    cards.Children.Add(slot.View);
                }
                slot.X = left + 12; slots.Add(slot);
                left += cardWidth + 24;
            }
            if (anchor != null)
            {
                var replacement = slots.FirstOrDefault(slot => ReferenceEquals(slot.Shot, anchor.Shot));
                if (replacement != null) { scrollOffset = replacement.X - anchorX; scrollTarget = scrollOffset + pending; }
            }
            scrollOffset = Math.Max(0, Math.Min(MaxScroll, scrollOffset));
            scrollTarget = Math.Max(0, Math.Min(MaxScroll, scrollTarget));
            if (images.Count != 0 && emptyMessage != null)
            {
                cards.Children.Remove(emptyMessage); emptyMessage = null; emptyHint = null;
            }
            if (images.Count == 0 && emptyMessage == null)
            {
                emptyHint = new TextBlock { Foreground = Ui.LightText, FontSize = 14, FontWeight = FontWeights.Medium,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 5) };
                var shortcut = new TextBlock { Text = "Win + Shift + S  截图后自动收集", Foreground = Ui.LightMuted, FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
                var empty = MakeButton("导入图片", "也可以使用 Win+Shift+S 截图", ImportDialog);
                empty.HorizontalAlignment = HorizontalAlignment.Center; empty.Margin = new Thickness(0);
                var message = new StackPanel(); message.Children.Add(emptyHint); message.Children.Add(shortcut); message.Children.Add(empty);
                emptyMessage = new Border { Width = 240, Background = Ui.LightSurface, BorderBrush = Ui.LightEdge, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 16, 18, 16), Child = message, Effect = Ui.Shadow(4, 16, .14) };
                Canvas.SetTop(emptyMessage, 44); cards.Children.Add(emptyMessage);
            }
            if (emptyMessage != null)
            {
                emptyHint.Text = store.ThumbnailsPending ? "正在加载截图…" : "让截图，随手可取";
                Canvas.SetLeft(emptyMessage, (Width - emptyMessage.Width) / 2);
            }
            PositionCards();
        }
        private void PositionCards()
        {
            foreach (CardSlot slot in slots)
            {
                double x = slot.X - scrollOffset;
                // Move only the rendered cards; scrolling must not arrange the whole row each frame.
                bool visible = x + slot.Width >= -16 && x <= Width + 16;
                slot.View.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
                if (!visible) continue;
                slot.Position.X = x;
                slot.Position.Y = Clothesline.DropAt((x + slot.Width / 2) / Width, Width);
            }
            UpdateEdges();
        }
        private UIElement Card(Shot shot)
        {
            var body = new StackPanel();
            var clip = new Clothespin { Margin = new Thickness(0, 0, 0, -4) };
            body.Children.Add(clip); Panel.SetZIndex(clip, 1);
            Size imageSize = ImageSize(shot);
            var image = new Image { Source = shot.Image, Width = imageSize.Width, Height = imageSize.Height, Stretch = Stretch.Uniform, Cursor = Cursors.Hand, ToolTip = "单击复制 · 双击预览 · 右键更多操作" };
            var frame = new Border { Background = Ui.LightSurface, Padding = new Thickness(3), BorderBrush = Ui.LightSurface, BorderThickness = new Thickness(1),
                Child = image, CornerRadius = new CornerRadius(3), Effect = Ui.Shadow(3, 10, .21) };
            body.Children.Add(frame);
            var outer = new Border { Width = imageSize.Width + 8, VerticalAlignment = VerticalAlignment.Top, Child = body };
            outer.MouseEnter += delegate { frame.BorderBrush = Ui.Accent; frame.Effect = Ui.Shadow(4, 12, .26); };
            outer.MouseLeave += delegate { frame.BorderBrush = Ui.LightSurface; frame.Effect = Ui.Shadow(3, 10, .21); };
            Point start = new Point(); bool dragging = false;
            var click = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Forms.SystemInformation.DoubleClickTime) };
            click.Tick += delegate { click.Stop(); Copy(shot); };
            image.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                start = e.GetPosition(image); dragging = false;
                if (e.ClickCount == 2) { click.Stop(); OpenPreview(shot, false); e.Handled = true; }
            };
            image.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                Point current = e.GetPosition(image);
                if (dragging || e.LeftButton != MouseButtonState.Pressed || (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)) return;
                dragging = true; click.Stop(); Busy = true;
                try { DragDrop.DoDragDrop(image, FileData(shot), DragDropEffects.Copy); }
                finally { Busy = false; leaveSince = DateTime.MinValue; }
            };
            image.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { if (!dragging && e.ClickCount == 1) click.Start(); };
            var context = Ui.Menu(true);
            AddMenu(context, "复制图片", delegate { Copy(shot); });
            AddMenu(context, "预览", delegate { OpenPreview(shot, false); });
            AddMenu(context, "标注", delegate { OpenPreview(shot, true); });
            AddMenu(context, "贴到屏幕", delegate { if (PinRequested != null) PinRequested(Store.ReadImage(shot.Path)); });
            AddMenu(context, "另存为…", delegate { SaveAs(shot); });
            AddMenu(context, "修复空白图片", delegate
            {
                var original = Store.ReadImage(shot.Path);
                var repaired = ClipboardImages.NormalizeBitmap(original);
                if (ReferenceEquals(original, repaired)) { Status("没有可恢复的透明像素内容，请重新截图或导入原图。"); return; }
                store.Replace(shot, repaired); Status("已恢复图片显示，仅修改应用副本。");
            });
            AddMenu(context, "移除副本", delegate { store.Remove(shot); });
            context.Opened += delegate { Busy = true; }; context.Closed += delegate { Busy = false; };
            outer.ContextMenu = context;
            return outer;
        }
        private static void AddMenu(ContextMenu menu, string title, Action action)
        {
            var item = new MenuItem { Header = title }; item.Click += delegate { action(); }; menu.Items.Add(item);
        }
        internal static DataObject FileData(Shot shot)
        {
            var data = new DataObject(); data.SetData(DataFormats.FileDrop, new[] { shot.Path }); data.SetData(DataFormats.Bitmap, Store.ReadImage(shot.Path)); return data;
        }
        private void Copy(Shot shot)
        {
            try { Clipboard.SetImage(Store.ReadImage(shot.Path)); if (Copied != null) Copied(); Status("已复制图片，可 Ctrl+V 粘贴"); }
            catch (Exception) { Status("复制失败，请稍后重试。"); }
        }
        private void OpenPreview(Shot shot, bool edit)
        {
            Busy = true;
            try { new Preview(store, shot, edit, delegate(System.Windows.Media.Imaging.BitmapSource image) { if (PinRequested != null) PinRequested(image); }) { Owner = this }.ShowDialog(); }
            finally { Busy = false; leaveSince = DateTime.MinValue; }
        }
        private void SaveAs(Shot shot)
        {
            Busy = true;
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG 图片|*.png", FileName = Path.GetFileName(shot.Path) };
                if (dialog.ShowDialog() == true) { File.Copy(shot.Path, dialog.FileName, true); Status("图片已另存为"); }
            }
            finally { Busy = false; }
        }
        internal async void ImportDialog()
        {
            Busy = true;
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff", Multiselect = true };
                if (dialog.ShowDialog() == true) { await ImportPathsAsync(dialog.FileNames); Reveal(true); }
            }
            finally { Busy = false; }
        }
        internal async void ImportPaths(string[] paths) { await ImportPathsAsync(paths); }
        internal async Task ImportPathsAsync(string[] paths)
        {
            int added = 0, failed = 0;
            // Queue paths up front so exit can drain the whole accepted batch, without decoding it up front.
            var imports = paths.Where(FolderCollector.IsImage).Select(path => store.ImportAsync(path)).ToArray();
            foreach (Task<bool> import in imports)
            {
                try { if (await import) added++; }
                catch (Exception) { failed++; }
            }
            Status(string.Format("已收集 {0} 张{1}，重复图片自动略过", added, failed == 0 ? "" : "，" + failed + " 张无法读取"));
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; timer.Stop(); scrollTimer.Stop(); store.Changed -= Changed; store.LayerChanged -= ApplyLayer;
            if (source != null) source.RemoveHook(Hook);
        }
    }
}
