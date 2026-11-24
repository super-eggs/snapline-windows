using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Snapline
{
    internal sealed class CaptureWindow : Window
    {
        private readonly ScreenCapture screen;
        private readonly Canvas stage;
        private readonly Canvas selectionLayer = new Canvas();
        private readonly Border selectionPreview;
        private readonly ImageBrush selectionBrush;
        private readonly System.Windows.Shapes.Rectangle outline = new System.Windows.Shapes.Rectangle { Stroke = Ui.Accent, StrokeThickness = 1, IsHitTestVisible = false };
        private readonly Border toolbar;
        private readonly Button colorPickerButton;
        private readonly TextBlock hint;
        private InkCanvas ink;
        private AnnotationTools annotations;
        private readonly WrapPanel annotationHost = new WrapPanel();
        private readonly Func<BitmapSource, Task<bool>> saveAndCopy;
        private readonly Action<string> copyColor;
        private readonly Action<BitmapSource> pinImage;
        private readonly PixelLens lens = new PixelLens { Visibility = Visibility.Collapsed };
        private Point pointer;
        private bool pickingColor;
        private readonly DispatcherTimer hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        private CaptureTarget hoverTarget;
        private List<Rect> hoverCandidates = new List<Rect>();
        private Point hoverPoint;
        private int hoverLevel, hoverVersion;
        private bool detecting, closed;
        internal PixelLens Lens { get { return lens; } }
        internal Rect Hover { get { return hover; } }
        internal int HoverLevels { get { return hoverCandidates.Count; } }
        private Rect selection = Rect.Empty, hover = Rect.Empty, before;
        private Point origin;
        private bool dragging, selected, finishing;
        private int hit;
        private readonly System.Windows.Shapes.Rectangle[] handles = new System.Windows.Shapes.Rectangle[8];
        internal Rect Selection { get { return selection; } }
        internal AnnotationTools Annotations { get { return annotations; } }
        internal CaptureWindow(ScreenCapture screen, Func<BitmapSource, Task<bool>> saveAndCopy, Action<string> copyColor = null, Action<BitmapSource> pinImage = null)
        {
            this.screen = screen; this.saveAndCopy = saveAndCopy;
            this.copyColor = copyColor; this.pinImage = pinImage;
            Title = "Snapline 截图"; Icon = AppIcon.WindowImage();
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            Topmost = true; Background = Brushes.Black; FontFamily = Ui.Font; Ui.StyleToolTips(this, false);
            Left = screen.Bounds.Left; Top = screen.Bounds.Top; Width = screen.Bounds.Width; Height = screen.Bounds.Height;
            stage = new Canvas { Width = screen.Image.PixelWidth, Height = screen.Image.PixelHeight, ClipToBounds = true, Background = Brushes.Transparent };
            var desktop = new Image { Source = DimDesktop(screen.Image), Width = stage.Width, Height = stage.Height, Stretch = Stretch.Fill, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(desktop, BitmapScalingMode.NearestNeighbor);
            stage.Children.Add(desktop);
            selectionBrush = new ImageBrush(screen.Image) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox };
            selectionPreview = new Border { Background = selectionBrush, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(selectionPreview, BitmapScalingMode.NearestNeighbor);
            stage.Children.Add(selectionPreview);
            stage.Children.Add(selectionLayer); stage.Children.Add(outline);
            for (int i = 0; i < handles.Length; i++)
            {
                handles[i] = new System.Windows.Shapes.Rectangle { Width = 7, Height = 7, Fill = Brushes.White, Stroke = Ui.Accent, StrokeThickness = 1, IsHitTestVisible = false };
                stage.Children.Add(handles[i]);
            }
            hint = new TextBlock { Foreground = Ui.Text, Background = Ui.Surface, Padding = new Thickness(10, 6, 10, 6), FontSize = 12, TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false };
            stage.Children.Add(hint);
            var tools = new WrapPanel(); tools.Children.Add(annotationHost);
            annotationHost.AddHandler(Button.ClickEvent, new RoutedEventHandler(delegate { SetColorPicking(false); }));
            var actions = new WrapPanel(); actions.Children.Add(Ui.CompactDivider());
            colorPickerButton = AddAction(actions, "picker", "取色器：点击选区或按 C 复制原图颜色，Esc 退出取色", ToggleColorPicker);
            colorPickerButton.IsEnabled = false;
            AddAction(actions, "reselect", "重新框选：重新选择截图区域", Reset);
            AddAction(actions, "save", "另存为 PNG（Ctrl+S）", SaveAs);
            if (pinImage != null) AddAction(actions, "pin", "贴到屏幕（Ctrl+T）", PinSelection);
            AddAction(actions, "close", "取消截图（Esc / 右键）", Close);
            AddAction(actions, "check", "完成：复制并收集（Enter / Ctrl+C / 双击选区）", Finish, true);
            tools.Children.Add(actions);
            toolbar = new Border { Child = tools, Background = Ui.Surface, BorderBrush = Ui.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(4), Effect = Ui.Shadow(2, 8, .18), Visibility = Visibility.Collapsed };
            stage.Children.Add(toolbar); stage.Children.Add(lens); Content = stage;
            SourceInitialized += delegate
            {
                HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(Hook); Position();
            };
            Loaded += delegate
            {
                Position(); Activate(); Keyboard.Focus(this);
                Native.Point cursor;
                if (Native.GetCursorPos(out cursor))
                {
                    Point point = Clamp(stage.PointFromScreen(new Point(cursor.X, cursor.Y)));
                    UpdateLens(point); UpdateHover(point);
                }
            };
            stage.PreviewMouseLeftButtonDown += Down;
            stage.PreviewMouseMove += Move;
            stage.PreviewMouseLeftButtonUp += Up;
            stage.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                if (!selected && !dragging && e.Delta != 0) { ChangeHoverLevel(e.Delta > 0 ? 1 : -1); e.Handled = true; }
            };
            stage.LostMouseCapture += delegate { if (dragging) { dragging = false; if (selected) SetSelection(before); else Reset(); } };
            stage.PreviewMouseRightButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (pickingColor) SetColorPicking(false);
                else if (annotations != null && annotations.IsTextEditing) annotations.DiscardText();
                else if (selected) Reset(); else Close(); e.Handled = true;
            };
            PreviewKeyDown += KeyDownCapture;
            Deactivated += delegate { SetColorPicking(false); };
            hoverTimer.Tick += DetectHover;
            Closed += delegate { closed = true; hoverVersion++; hoverTimer.Stop(); SetColorPicking(false); };
            Redraw();
        }
        private static BitmapSource DimDesktop(BitmapSource image)
        {
            BitmapSource pixels = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int stride = image.PixelWidth * 4, rows = Math.Min(32, image.PixelHeight);
            var buffer = new byte[stride * rows];
            var result = new WriteableBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Bgra32, null);
            for (int y = 0; y < image.PixelHeight; y += rows)
            {
                var rect = new Int32Rect(0, y, image.PixelWidth, Math.Min(rows, image.PixelHeight - y));
                pixels.CopyPixels(rect, buffer, stride, 0);
                for (int i = 0; i < rect.Height * stride; i += 4)
                {
                    int factor = buffer[i + 3] * 125;
                    buffer[i] = (byte)((buffer[i] * factor + 32512) / 65025);
                    buffer[i + 1] = (byte)((buffer[i + 1] * factor + 32512) / 65025);
                    buffer[i + 2] = (byte)((buffer[i + 2] * factor + 32512) / 65025);
                    buffer[i + 3] = 255;
                }
                result.WritePixels(rect, buffer, stride, 0);
            }
            result.Freeze(); return result;
        }
        private static Button AddAction(Panel panel, string icon, string tip, Action action, bool primary = false)
        {
            var button = Ui.CompactIconButton(icon, tip, action); panel.Children.Add(button);
            if (primary) Ui.Select(button, true);
            else button.Background = button.BorderBrush = Brushes.Transparent;
            return button;
        }
        private void ToggleColorPicker()
        {
            if (!selected || dragging) return;
            SetColorPicking(!pickingColor);
        }
        private void SetColorPicking(bool enabled)
        {
            if (pickingColor == enabled) return;
            if (enabled)
            {
                annotations.Select(AnnotationTool.Hand);
                lens.SetImage(screen.Image);
            }
            else { lens.Visibility = Visibility.Collapsed; lens.Clear(); }
            pickingColor = enabled; Ui.Select(colorPickerButton, enabled);
            if (!closed) Redraw();
            UpdateLens(pointer);
        }
        private void CopyPickedColor()
        {
            try { if (copyColor != null) copyColor(lens.ColorText); hint.Text = "已复制 " + lens.ColorText + " · 可继续取色，Esc 退出取色器"; }
            catch (System.Runtime.InteropServices.ExternalException) { hint.Text = "剪贴板正被占用，请重试取色"; }
        }
        private IntPtr Hook(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if (message == 0x02E0) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Position));
            return IntPtr.Zero;
        }
        private void Position()
        {
            var source = PresentationSource.FromVisual(this);
            if (source == null) return;
            Matrix dpi = source.CompositionTarget.TransformToDevice;
            stage.LayoutTransform = new ScaleTransform(1 / dpi.M11, 1 / dpi.M22);
            toolbar.LayoutTransform = new ScaleTransform(dpi.M11, dpi.M22);
            hint.LayoutTransform = new ScaleTransform(dpi.M11, dpi.M22);
            lens.LayoutTransform = new ScaleTransform(dpi.M11, dpi.M22); UpdateLens(pointer);
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), screen.Bounds.Left, screen.Bounds.Top, screen.Bounds.Width, screen.Bounds.Height, 0x0010);
        }
        private Point Clamp(Point point) { return new Point(Math.Max(0, Math.Min(stage.Width, point.X)), Math.Max(0, Math.Min(stage.Height, point.Y))); }
        private int Hit(Point point)
        {
            if (!selected) return 0;
            Point[] points = HandlePoints();
            for (int i = 0; i < points.Length; i++) if ((points[i] - point).Length < 9) return i + 1;
            return selection.Contains(point) ? 9 : 0;
        }
        internal void UpdateHover(Point point)
        {
            if (selected || dragging || closed) return;
            if (hoverCandidates.Count > 0 && (point == hoverPoint || hoverCandidates.Count > 1 && hoverCandidates[0].Contains(point)))
            {
                hoverPoint = point; return;
            }
            hoverPoint = point; hoverVersion++; hoverLevel = 0; hoverTarget = null;
            hoverTimer.Stop();
            foreach (CaptureTarget window in screen.Windows) if (window.Bounds.Contains(point)) { hoverTarget = window; break; }
            Rect next = hoverTarget == null ? new Rect(0, 0, stage.Width, stage.Height) : hoverTarget.Bounds;
            bool redraw = hover != next || hoverCandidates.Count != 1;
            hover = next;
            hoverCandidates = new List<Rect> { hover };
            if (hoverTarget != null && hoverTarget.Handle != IntPtr.Zero) hoverTimer.Start();
            if (redraw) Redraw();
        }
        private async void DetectHover(object sender, EventArgs e)
        {
            if (detecting) return;
            hoverTimer.Stop();
            if (selected || dragging || closed || hoverTarget == null) return;
            int version = hoverVersion;
            CaptureTarget target = hoverTarget; Point point = hoverPoint;
            detecting = true;
            try
            {
                List<Rect> candidates = await Task.Run(delegate { return ControlDetector.Find(target, point, screen.Bounds); });
                if (closed || selected || dragging || version != hoverVersion) return;
                // Keep an explicit parent choice if the user scrolled before the lookup completed.
                hoverLevel = hoverLevel > 0 ? candidates.Count - 1 : 0;
                hoverCandidates = candidates; hover = candidates[hoverLevel]; Redraw();
            }
            finally { detecting = false; }
        }
        internal void ChangeHoverLevel(int direction)
        {
            if (selected || dragging || hoverCandidates.Count == 0) return;
            if (detecting || hoverTimer.IsEnabled) hoverLevel = direction > 0 ? 1 : 0;
            else hoverLevel = Math.Max(0, Math.Min(hoverCandidates.Count - 1, hoverLevel + direction));
            hover = hoverCandidates[Math.Min(hoverLevel, hoverCandidates.Count - 1)]; Redraw();
        }
        private void Down(object sender, MouseButtonEventArgs e)
        {
            if (finishing || toolbar.IsMouseOver || annotations != null && annotations.IsTextEditing) return;
            Point point = Clamp(e.GetPosition(stage));
            if (pickingColor)
            {
                if (selection.Contains(point)) { UpdateLens(point); CopyPickedColor(); }
                e.Handled = true; return;
            }
            if (!selected) UpdateHover(point);
            if (selected && selection.Contains(point) && (annotations.Tool != AnnotationTool.Hand || annotations.HasTextAt(e.GetPosition(ink)))) return;
            if (selected && e.ClickCount == 2 && selection.Contains(point)) { Finish(); e.Handled = true; return; }
            hit = Hit(point);
            if (selected && annotations.HasAnnotations) return;
            if (selected && hit == 0) { Reset(); }
            hoverVersion++; hoverTimer.Stop();
            origin = point; before = selection; dragging = stage.CaptureMouse();
            toolbar.Visibility = Visibility.Collapsed; e.Handled = true;
        }
        private void Move(object sender, MouseEventArgs e)
        {
            Point point = Clamp(e.GetPosition(stage));
            UpdateLens(point);
            if (!dragging)
            {
                if (pickingColor) { stage.Cursor = Cursors.Cross; return; }
                if (!selected)
                {
                    UpdateHover(point);
                }
                int target = Hit(point);
                stage.Cursor = selected && annotations.Tool != AnnotationTool.Hand && selection.Contains(point) ? Cursors.Cross : target == 9 ? Cursors.SizeAll : target == 2 || target == 6 ? Cursors.SizeNS : target == 4 || target == 8 ? Cursors.SizeWE : target == 1 || target == 5 ? Cursors.SizeNWSE : target == 3 || target == 7 ? Cursors.SizeNESW : Cursors.Cross;
                return;
            }
            if (hit == 9)
            {
                selection = new Rect(Math.Max(0, Math.Min(stage.Width - before.Width, before.X + point.X - origin.X)), Math.Max(0, Math.Min(stage.Height - before.Height, before.Y + point.Y - origin.Y)), before.Width, before.Height);
            }
            else if (hit > 0)
            {
                double left = before.Left, top = before.Top, right = before.Right, bottom = before.Bottom;
                if (hit == 1 || hit == 7 || hit == 8) left = Math.Min(point.X, right - 1);
                if (hit == 1 || hit == 2 || hit == 3) top = Math.Min(point.Y, bottom - 1);
                if (hit == 3 || hit == 4 || hit == 5) right = Math.Max(point.X, left + 1);
                if (hit == 5 || hit == 6 || hit == 7) bottom = Math.Max(point.Y, top + 1);
                selection = new Rect(new Point(left, top), new Point(right, bottom));
            }
            else selection = new Rect(origin, point);
            Redraw(); e.Handled = true;
        }
        private void Up(object sender, MouseButtonEventArgs e)
        {
            if (!dragging) return;
            Move(sender, e); dragging = false; stage.ReleaseMouseCapture();
            if (!selected && (selection.Width < 3 || selection.Height < 3)) selection = hover;
            SetSelection(selection); e.Handled = true;
        }
        internal void UpdateLens(Point point)
        {
            pointer = point;
            lens.Visibility = pickingColor && selected && !dragging && selection.Contains(point) && !toolbar.IsMouseOver
                && !annotations.IsTextEditing ? Visibility.Visible : Visibility.Collapsed;
            if (lens.Visibility != Visibility.Visible) return;
            lens.Sample(point);
            lens.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var monitor = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(screen.Bounds.Left + (int)point.X, screen.Bounds.Top + (int)point.Y)).Bounds;
            double left = Math.Max(0, monitor.Left - screen.Bounds.Left), top = Math.Max(0, monitor.Top - screen.Bounds.Top);
            double right = Math.Min(stage.Width, monitor.Right - screen.Bounds.Left), bottom = Math.Min(stage.Height, monitor.Bottom - screen.Bounds.Top);
            if (right - left < lens.DesiredSize.Width || bottom - top < lens.DesiredSize.Height) { left = top = 0; right = stage.Width; bottom = stage.Height; }
            double x = point.X + 24, y = point.Y + 24;
            if (x + lens.DesiredSize.Width > right) x = point.X - lens.DesiredSize.Width - 24;
            if (y + lens.DesiredSize.Height > bottom) y = point.Y - lens.DesiredSize.Height - 24;
            Canvas.SetLeft(lens, Math.Max(left, Math.Min(right - lens.DesiredSize.Width, x)));
            Canvas.SetTop(lens, Math.Max(top, Math.Min(bottom - lens.DesiredSize.Height, y)));
        }
        internal void SetSelection(Rect value)
        {
            SetColorPicking(false);
            hoverVersion++; hoverTimer.Stop();
            var rect = ScreenCapture.PixelRect(value, screen.Image.PixelWidth, screen.Image.PixelHeight);
            if (rect.Width < 1 || rect.Height < 1) { Reset(); return; }
            if ((long)rect.Width * rect.Height > 40000000) { MessageBox.Show(this, "选区超过 4000 万像素，请缩小选区。", "Snapline 截图"); Reset(); return; }
            bool rebuild = ink == null || selection.Width != rect.Width || selection.Height != rect.Height || !annotations.HasAnnotations;
            selection = new Rect(rect.X, rect.Y, rect.Width, rect.Height); selected = true;
            colorPickerButton.IsEnabled = true;
            if (rebuild)
            {
                var crop = new CroppedBitmap(screen.Image, rect); crop.Freeze();
                ink = new InkCanvas { Width = rect.Width, Height = rect.Height, Background = new ImageBrush(crop) { Stretch = Stretch.Fill } };
                RenderOptions.SetBitmapScalingMode(ink, BitmapScalingMode.NearestNeighbor);
                annotations = new AnnotationTools(ink, crop);
                annotations.Changed += Redraw;
                selectionLayer.Children.Clear(); selectionLayer.Children.Add(ink);
                annotationHost.Children.Clear(); annotationHost.Children.Add(annotations.Toolbar);
            }
            Redraw();
            UpdateLens(pointer);
        }
        private Point[] HandlePoints()
        {
            double l = selection.Left, r = selection.Right, t = selection.Top, b = selection.Bottom, x = (l + r) / 2, y = (t + b) / 2;
            return new[] { new Point(l, t), new Point(x, t), new Point(r, t), new Point(r, y), new Point(r, b), new Point(x, b), new Point(l, b), new Point(l, y) };
        }
        private void Redraw()
        {
            Rect shown = selected || dragging ? selection : hover;
            // Keep the dimmed desktop static. Only the small revealed region changes while dragging.
            selectionPreview.Visibility = shown.IsEmpty || selected && !dragging ? Visibility.Collapsed : Visibility.Visible;
            if (!shown.IsEmpty)
            {
                Canvas.SetLeft(selectionPreview, shown.Left); Canvas.SetTop(selectionPreview, shown.Top);
                selectionPreview.Width = shown.Width; selectionPreview.Height = shown.Height;
                selectionBrush.Viewbox = new Rect(shown.X / stage.Width, shown.Y / stage.Height, shown.Width / stage.Width, shown.Height / stage.Height);
            }
            outline.Visibility = shown.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
            if (!shown.IsEmpty)
            {
                Canvas.SetLeft(outline, shown.Left); Canvas.SetTop(outline, shown.Top); outline.Width = shown.Width; outline.Height = shown.Height;
            }
            bool showHandles = selected && !pickingColor && !annotations.HasAnnotations;
            Point[] handlePoints = showHandles ? HandlePoints() : null;
            for (int i = 0; i < handles.Length; i++)
            {
                handles[i].Visibility = showHandles ? Visibility.Visible : Visibility.Collapsed;
                if (showHandles) { Point p = handlePoints[i]; Canvas.SetLeft(handles[i], p.X - 3); Canvas.SetTop(handles[i], p.Y - 3); }
            }
            if (ink != null) { Canvas.SetLeft(ink, selection.Left); Canvas.SetTop(ink, selection.Top); }
            selectionLayer.Visibility = dragging ? Visibility.Collapsed : Visibility.Visible;
            string hoverHint = hoverCandidates.Count > 1 ? string.Format("层级 {0}/{1} · 滚轮或 1/2 切换 · 单击选择 · 拖动框选", hoverLevel + 1, hoverCandidates.Count) : "单击选择窗口 · 滚轮或 1/2 切换层级 · 拖动框选";
            hint.Text = pickingColor ? "点击选区或按 C 复制颜色 · Shift 切换 HEX/RGB · Esc 退出取色器" : shown.IsEmpty ? "悬停识别控件 · 拖动框选 · Esc 取消" : string.Format("{0:0} × {1:0} px   {2}", shown.Width, shown.Height, selected ? annotations.HasAnnotations ? "选区已锁定 · 撤销全部标注可调整 · Enter 完成" : "拖动调整 · Shift 等比例标注 · Enter 完成 · Esc 取消" : hoverHint);
            hint.Measure(new Size(Math.Max(1, stage.Width - 8), double.PositiveInfinity));
            Canvas.SetLeft(hint, Math.Max(4, Math.Min(stage.Width - hint.DesiredSize.Width - 4, shown.IsEmpty ? 20 : shown.Left)));
            double hintTop = shown.IsEmpty ? 20 : shown.Top >= hint.DesiredSize.Height + 8 ? shown.Top - hint.DesiredSize.Height - 8 : shown.Bottom + 8;
            Canvas.SetTop(hint, Math.Max(4, Math.Min(stage.Height - hint.DesiredSize.Height - 4, hintTop)));
            if (selected && !dragging)
            {
                var monitor = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(screen.Bounds.Left + (int)selection.Right - 1, screen.Bounds.Top + (int)selection.Bottom - 1)).Bounds;
                var area = new Rect(monitor.Left - screen.Bounds.Left, monitor.Top - screen.Bounds.Top, monitor.Width, monitor.Height);
                area.Intersect(new Rect(0, 0, stage.Width, stage.Height));
                if (area.IsEmpty || area.Width < 32 || area.Height < 32) area = new Rect(0, 0, stage.Width, stage.Height);
                toolbar.Visibility = Visibility.Visible; toolbar.MaxWidth = Math.Min(680, area.Width - 16);
                toolbar.Measure(new Size(toolbar.MaxWidth, stage.Height));
                Canvas.SetLeft(toolbar, Math.Max(area.Left + 8, Math.Min(area.Right - toolbar.DesiredSize.Width - 8, selection.Right - toolbar.DesiredSize.Width)));
                double below = selection.Bottom + 40;
                Canvas.SetTop(toolbar, below + toolbar.DesiredSize.Height < area.Bottom ? below : selection.Top - area.Top > toolbar.DesiredSize.Height + 42 ? selection.Top - toolbar.DesiredSize.Height - 40 : Math.Max(area.Top + 8, area.Bottom - toolbar.DesiredSize.Height - 8));
            }
        }
        private void Reset()
        {
            if (finishing) return;
            SetColorPicking(false); colorPickerButton.IsEnabled = false;
            selected = false; selection = Rect.Empty; annotations = null; ink = null; selectionLayer.Children.Clear(); annotationHost.Children.Clear();
            toolbar.Visibility = Visibility.Collapsed; hoverCandidates.Clear(); UpdateHover(pointer);
            UpdateLens(pointer);
        }
        internal BitmapSource RenderSelection()
        {
            return annotations.Render();
        }
        private void KeyDownCapture(object sender, KeyEventArgs e)
        {
            if (finishing) { e.Handled = true; return; }
            if (annotations != null && annotations.IsTextEditing) return;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (!selected && !dragging && Keyboard.Modifiers == ModifierKeys.None && (key == Key.D1 || key == Key.NumPad1 || key == Key.D2 || key == Key.NumPad2))
            {
                ChangeHoverLevel(key == Key.D1 || key == Key.NumPad1 ? 1 : -1); e.Handled = true;
            }
            else if (lens.Visibility == Visibility.Visible && (key == Key.LeftShift || key == Key.RightShift) && !e.IsRepeat) { lens.ToggleFormat(); e.Handled = true; }
            else if (!control && key == Key.C && lens.Visibility == Visibility.Visible)
            {
                CopyPickedColor();
                e.Handled = true;
            }
            else if (!control && (key == Key.W || key == Key.A || key == Key.S || key == Key.D))
            {
                Point next = Clamp(pointer + new Vector(key == Key.A ? -1 : key == Key.D ? 1 : 0, key == Key.W ? -1 : key == Key.S ? 1 : 0));
                Native.SetCursorPos(screen.Bounds.Left + (int)next.X, screen.Bounds.Top + (int)next.Y); UpdateLens(next); UpdateHover(next); e.Handled = true;
            }
            else if (e.Key == Key.Escape) { if (pickingColor) SetColorPicking(false); else Close(); e.Handled = true; }
            else if (selected && annotations.HandleKey(e)) e.Handled = true;
            else if (selected && control && key == Key.T) { PinSelection(); e.Handled = true; }
            else if (control && key == Key.A) { SetSelection(new Rect(0, 0, stage.Width, stage.Height)); e.Handled = true; }
            else if (selected && (e.Key == Key.Enter || control && e.Key == Key.C)) { Finish(); e.Handled = true; }
            else if (selected && control && e.Key == Key.S) { SaveAs(); e.Handled = true; }
            else if (selected && !pickingColor && annotations.Tool == AnnotationTool.Hand && !annotations.HasAnnotations && (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down))
            {
                double x = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0, y = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
                if (control || (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                {
                    double change = control ? 1 : -1;
                    double l = selection.Left, r = selection.Right, t = selection.Top, b = selection.Bottom;
                    if (x < 0) l = Math.Max(0, Math.Min(r - 1, l - change));
                    if (x > 0) r = Math.Min(stage.Width, Math.Max(l + 1, r + change));
                    if (y < 0) t = Math.Max(0, Math.Min(b - 1, t - change));
                    if (y > 0) b = Math.Min(stage.Height, Math.Max(t + 1, b + change));
                    SetSelection(new Rect(new Point(l, t), new Point(r, b)));
                }
                else SetSelection(new Rect(Math.Max(0, Math.Min(stage.Width - selection.Width, selection.X + x)), Math.Max(0, Math.Min(stage.Height - selection.Height, selection.Y + y)), selection.Width, selection.Height));
                e.Handled = true;
            }
        }
        internal void PinSelection()
        {
            if (!selected || finishing || pinImage == null) return;
            try { pinImage(RenderSelection()); Close(); }
            catch (NotSupportedException ex) { MessageBox.Show(this, ex.Message, "Snapline 贴图"); }
        }
        private async void Finish()
        {
            if (!selected || finishing) return;
            finishing = true; IsEnabled = false;
            try
            {
                bool copied = await saveAndCopy(RenderSelection());
                if (!copied && !closed)
                    MessageBox.Show(this, "截图已保存到晾衣绳，但复制到剪贴板失败。可在晾衣绳点击这张图重新复制。", "Snapline 截图");
                if (!closed) Close();
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is System.Runtime.InteropServices.ExternalException)) throw;
                if (!closed) MessageBox.Show(this, "截图未能保存，也未复制。请检查磁盘空间和截图文件夹权限后重试。", "Snapline 截图");
            }
            finally { finishing = false; if (!closed) IsEnabled = true; }
        }
        private async void SaveAs()
        {
            if (!selected || finishing) return;
            annotations.CommitText();
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG 图片|*.png", FileName = "截图-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png" };
            if (dialog.ShowDialog(this) != true) return;
            finishing = true; IsEnabled = false;
            try
            {
                BitmapSource image = RenderSelection();
                await Task.Run(delegate { File.WriteAllBytes(dialog.FileName, Store.Encode(image)); });
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException)) throw;
                MessageBox.Show(this, "保存失败，请检查文件权限和磁盘空间。", "Snapline 截图");
            }
            finally { finishing = false; IsEnabled = true; }
        }
    }
}
