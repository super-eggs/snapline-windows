using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Snapline
{
    internal sealed class PinnedImageWindow : Window
    {
        private readonly InkCanvas canvas;
        private readonly AnnotationTools annotations;
        private readonly Action<BitmapSource> copyImage;
        private readonly Action<string> copyColor;
        private readonly Popup tools, magnifier;
        private readonly PixelLens lens = new PixelLens();
        private readonly Grid root;
        private double zoom = 1;
        private int rotation;
        private bool flipX, flipY, destroying, fitting = true, altDown, saving;
        private BitmapSource lensImage;
        private HwndSource source;
        internal uint? ClipboardSequence;
        internal event Action<PinnedImageWindow, bool> RequestClose;
        internal AnnotationTools Annotations { get { return annotations; } }
        internal double Zoom { get { return zoom; } }
        internal int Rotation { get { return rotation; } }
        internal PixelLens Lens { get { return lens; } }
        internal PinnedImageWindow(BitmapSource image, Action<BitmapSource> copyImage, Action<string> copyColor)
        {
            if ((long)image.PixelWidth * image.PixelHeight > 40000000) throw new NotSupportedException("贴图超过 4000 万像素。");
            if (!image.IsFrozen) image.Freeze();
            this.copyImage = copyImage; this.copyColor = copyColor;
            Title = "Snapline 贴图"; Icon = AppIcon.WindowImage();
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; Topmost = true;
            Background = Ui.Surface; FontFamily = Ui.Font; Ui.StyleToolTips(this, false);
            canvas = new InkCanvas { Width = image.PixelWidth, Height = image.PixelHeight, Background = new ImageBrush(image) { Stretch = Stretch.Fill } };
            annotations = new AnnotationTools(canvas, image);
            annotations.Changed += delegate { lensImage = null; if (altDown) UpdateLens(Mouse.GetPosition(canvas), Mouse.GetPosition(root)); };
            root = new Grid { ClipToBounds = true }; root.Children.Add(canvas);
            Content = new Border { Child = root, BorderBrush = Ui.Accent, BorderThickness = new Thickness(1) };
            var toolsPanel = new StackPanel(); toolsPanel.Children.Add(annotations.Toolbar);
            var commands = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            AddButton(commands, "复制", "复制贴图（Ctrl+C）", Copy);
            AddButton(commands, "保存", "另存为 PNG（Ctrl+S）", SaveAs);
            AddButton(commands, "关闭工具", "收起工具条（空格 / Esc）", HideTools);
            toolsPanel.Children.Add(commands);
            var surface = new Border { Child = toolsPanel, Background = Ui.Surface, BorderBrush = Ui.Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(8), Width = 780 };
            tools = new Popup { Child = surface, PlacementTarget = this, Placement = PlacementMode.Bottom, StaysOpen = true, AllowsTransparency = true };
            toolsPanel.PreviewKeyDown += KeyDownPin;
            toolsPanel.PreviewKeyUp += KeyUpPin;
            magnifier = new Popup { Child = lens, PlacementTarget = root, Placement = PlacementMode.RelativePoint, StaysOpen = true, AllowsTransparency = true, IsHitTestVisible = false };
            canvas.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (annotations.Tool != AnnotationTool.Hand) return;
                if (e.ClickCount == 2) RequestCloseImage(false);
                else { HideTools(); DragMove(); }
                e.Handled = true;
            };
            canvas.PreviewMouseMove += delegate(object sender, MouseEventArgs e) { if (altDown) UpdateLens(e.GetPosition(canvas), e.GetPosition(root)); };
            canvas.MouseLeave += delegate { magnifier.IsOpen = false; };
            PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                if (annotations.IsTextEditing) return;
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) ChangeOpacity(Opacity + (e.Delta > 0 ? .05 : -.05));
                else ChangeZoom(zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1));
                e.Handled = true;
            };
            PreviewKeyDown += KeyDownPin;
            PreviewKeyUp += KeyUpPin;
            Deactivated += delegate { ClearLens(); };
            IsVisibleChanged += delegate { if (!IsVisible) HideTools(); };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (!destroying) { e.Cancel = true; RequestCloseImage(false); }
            };
            Closed += delegate { HideTools(); if (source != null) source.RemoveHook(Hook); };
            SourceInitialized += delegate
            {
                source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); source.AddHook(Hook);
                Native.Point cursor; Native.GetCursorPos(out cursor);
                var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y)).WorkingArea;
                Native.SetWindowPos(source.Handle, new IntPtr(-1), area.Left + 40, area.Top + 40, 0, 0, 0x0011); FitImage();
            };
            Loaded += delegate
            {
                var area = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
                Matrix dpi = source.CompositionTarget.TransformToDevice;
                Native.SetWindowPos(source.Handle, new IntPtr(-1), area.Left + (area.Width - (int)Math.Ceiling(Width * dpi.M11)) / 2,
                    area.Top + (area.Height - (int)Math.Ceiling(Height * dpi.M22)) / 2, 0, 0, 0x0011);
            };
            Native.Point pointer; Native.GetCursorPos(out pointer);
            Left = pointer.X; Top = pointer.Y; Width = Math.Max(8, Math.Min(800, image.PixelWidth)); Height = Math.Max(8, Math.Min(600, image.PixelHeight));
            root.ContextMenu = BuildMenu();
        }
        private static void AddButton(Panel panel, string text, string tip, Action action) { var button = Ui.Button(text, tip, action); button.Margin = new Thickness(3); panel.Children.Add(button); }
        private ContextMenu BuildMenu()
        {
            var menu = Ui.Menu();
            AddMenu(menu, "标注工具    空格", ToggleTools); AddMenu(menu, "复制图片    Ctrl+C", Copy); AddMenu(menu, "另存为…    Ctrl+S", SaveAs);
            AddMenu(menu, "原始大小    0", delegate { ChangeZoom(1); }); AddMenu(menu, "适应屏幕    Ctrl+0", FitImage);
            AddMenu(menu, "顺时针旋转    1", delegate { Rotate(90); }); AddMenu(menu, "逆时针旋转    2", delegate { Rotate(-90); });
            AddMenu(menu, "水平翻转    3", delegate { Flip(true); }); AddMenu(menu, "垂直翻转    4", delegate { Flip(false); });
            AddMenu(menu, "恢复不透明", delegate { ChangeOpacity(1); });
            AddMenu(menu, "关闭（可用 F3 恢复）    Esc", delegate { RequestCloseImage(false); });
            AddMenu(menu, "销毁（本次会话不再恢复）    Shift+Esc", delegate { RequestCloseImage(true); });
            return menu;
        }
        private static void AddMenu(ContextMenu menu, string label, Action action) { var item = new MenuItem { Header = label }; item.Click += delegate { action(); }; menu.Items.Add(item); }
        private IntPtr Hook(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
        {
            if (message == 0x02E0) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(UpdateSize));
            return IntPtr.Zero;
        }
        private void UpdateSize()
        {
            Matrix dpi = source == null ? Matrix.Identity : source.CompositionTarget.TransformToDevice;
            var transform = new TransformGroup(); transform.Children.Add(new ScaleTransform(flipX ? -1 : 1, flipY ? -1 : 1));
            transform.Children.Add(new RotateTransform(rotation)); transform.Children.Add(new ScaleTransform(zoom / dpi.M11, zoom / dpi.M22));
            canvas.LayoutTransform = transform;
            RenderOptions.SetBitmapScalingMode(canvas, zoom >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.Linear);
            Width = (rotation % 180 == 0 ? canvas.Width : canvas.Height) * zoom / dpi.M11 + 2;
            Height = (rotation % 180 == 0 ? canvas.Height : canvas.Width) * zoom / dpi.M22 + 2;
        }
        internal void FitImage()
        {
            var area = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
            zoom = Math.Min(1, Math.Min((area.Width - 80) / (rotation % 180 == 0 ? canvas.Width : canvas.Height), (area.Height - 80) / (rotation % 180 == 0 ? canvas.Height : canvas.Width)));
            fitting = true; UpdateSize();
        }
        internal void ChangeZoom(double value)
        {
            double old = zoom; zoom = Math.Max(.05, Math.Min(8, value)); fitting = false;
            Point anchor = IsVisible ? PointToScreen(Mouse.GetPosition(this)) : new Point(Left, Top);
            double ratio = zoom / old;
            Matrix dpi = source == null ? Matrix.Identity : source.CompositionTarget.TransformToDevice;
            Left = (anchor.X - (anchor.X - Left * dpi.M11) * ratio) / dpi.M11;
            Top = (anchor.Y - (anchor.Y - Top * dpi.M22) * ratio) / dpi.M22;
            UpdateSize(); KeepReachable();
        }
        private void KeepReachable()
        {
            var area = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
            Matrix dpi = source == null ? Matrix.Identity : source.CompositionTarget.TransformToDevice;
            Left = Math.Max(area.Left / dpi.M11 - Width + 32, Math.Min((area.Right - 32) / dpi.M11, Left));
            Top = Math.Max(area.Top / dpi.M22, Math.Min((area.Bottom - 32) / dpi.M22, Top));
        }
        internal void ChangeOpacity(double value) { Opacity = Math.Max(.1, Math.Min(1, value)); }
        internal void Rotate(int degrees) { annotations.CommitText(); rotation = (rotation + degrees + 360) % 360; if (fitting) FitImage(); else UpdateSize(); KeepReachable(); }
        internal void Flip(bool horizontal) { annotations.CommitText(); if (horizontal) flipX = !flipX; else flipY = !flipY; UpdateSize(); }
        internal BitmapSource RenderImage()
        {
            BitmapSource image = annotations.Render();
            var edit = new TransformGroup(); edit.Children.Add(new ScaleTransform(flipX ? -1 : 1, flipY ? -1 : 1)); edit.Children.Add(new RotateTransform(rotation));
            if (flipX || flipY || rotation != 0) { image = new TransformedBitmap(image, edit); image.Freeze(); }
            return image;
        }
        internal void ToggleTools()
        {
            tools.IsOpen = !tools.IsOpen; annotations.Select(tools.IsOpen ? AnnotationTool.Pen : AnnotationTool.Hand);
        }
        internal void HideTools() { tools.IsOpen = false; ClearLens(); annotations.Select(AnnotationTool.Hand); }
        private void ClearLens() { altDown = false; magnifier.IsOpen = false; lensImage = null; lens.Clear(); }
        private void RequestCloseImage(bool destroy) { if (RequestClose != null) RequestClose(this, destroy); else if (destroy) Destroy(); else Hide(); }
        internal void Destroy() { destroying = true; Close(); }
        private void Copy()
        {
            try { if (copyImage != null) copyImage(RenderImage()); }
            catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show(this, "剪贴板正被占用，请稍后重试。", "Snapline 贴图"); }
        }
        private async void SaveAs()
        {
            if (saving) return;
            annotations.CommitText(); var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG 图片|*.png", FileName = "贴图-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png" };
            if (dialog.ShowDialog(this) != true) return;
            saving = true;
            try { var image = RenderImage(); await Task.Run(delegate { File.WriteAllBytes(dialog.FileName, Store.Encode(image)); }); }
            catch (IOException) { MessageBox.Show(this, "保存失败，请检查磁盘空间和目录权限。", "Snapline 贴图"); }
            catch (UnauthorizedAccessException) { MessageBox.Show(this, "没有权限保存到该目录。", "Snapline 贴图"); }
            finally { saving = false; }
        }
        private void UpdateLens(Point pixel, Point cursor)
        {
            if (lensImage == null)
            {
                annotations.CommitText(); lensImage = Preview.RenderAnnotation(canvas);
                lens.SetImage(lensImage);
            }
            lens.Sample(pixel); magnifier.HorizontalOffset = cursor.X + 20; magnifier.VerticalOffset = cursor.Y + 20; magnifier.IsOpen = true;
        }
        private void KeyDownPin(object sender, KeyEventArgs e)
        {
            if (annotations.IsTextEditing) return;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (key == Key.LeftAlt || key == Key.RightAlt) { altDown = true; UpdateLens(Mouse.GetPosition(canvas), Mouse.GetPosition(root)); }
            else if (magnifier.IsOpen && (key == Key.LeftShift || key == Key.RightShift) && !e.IsRepeat) lens.ToggleFormat();
            else if (magnifier.IsOpen && !control && key == Key.C)
            {
                try { if (copyColor != null) copyColor(lens.ColorText); }
                catch (System.Runtime.InteropServices.ExternalException) { MessageBox.Show(this, "剪贴板正被占用，请再次按 C。", "Snapline 取色"); }
            }
            else if (key == Key.Escape) { if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) RequestCloseImage(true); else if (tools.IsOpen) HideTools(); else RequestCloseImage(false); }
            else if (control && key == Key.W) RequestCloseImage(false);
            else if (annotations.HandleKey(e)) { }
            else if (control && key == Key.C) Copy();
            else if (control && key == Key.S) SaveAs();
            else if (key == Key.Space) ToggleTools();
            else if (control && (key == Key.D0 || key == Key.NumPad0)) FitImage();
            else if (key == Key.D0 || key == Key.NumPad0) ChangeZoom(1);
            else if (key == Key.D1 || key == Key.NumPad1) Rotate(90);
            else if (key == Key.D2 || key == Key.NumPad2) Rotate(-90);
            else if (key == Key.D3 || key == Key.NumPad3) Flip(true);
            else if (key == Key.D4 || key == Key.NumPad4) Flip(false);
            else if (key == Key.Add || key == Key.OemPlus || key == Key.Subtract || key == Key.OemMinus)
            {
                bool plus = key == Key.Add || key == Key.OemPlus;
                if (control) ChangeOpacity(Opacity + (plus ? .05 : -.05)); else ChangeZoom(zoom * (plus ? 1.1 : 1 / 1.1));
            }
            else if (key == Key.Left || key == Key.Right || key == Key.Up || key == Key.Down)
            {
                Matrix dpi = source == null ? Matrix.Identity : source.CompositionTarget.TransformToDevice;
                Left += (key == Key.Left ? -1 : key == Key.Right ? 1 : 0) / dpi.M11; Top += (key == Key.Up ? -1 : key == Key.Down ? 1 : 0) / dpi.M22;
            }
            else return;
            e.Handled = true;
        }
        private void KeyUpPin(object sender, KeyEventArgs e)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.LeftAlt || key == Key.RightAlt) { ClearLens(); e.Handled = true; }
        }
    }
}
