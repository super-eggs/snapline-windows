using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapline
{
    internal enum AnnotationTool { Hand, Pen, Rectangle, Ellipse, Line, Arrow, Text, Mosaic, Eraser }

    // Both the capture overlay and the image preview edit in original image pixels.
    internal sealed class AnnotationTools
    {
        private readonly InkCanvas canvas;
        private readonly BitmapSource original;
        private readonly Dictionary<AnnotationTool, Button> buttons = new Dictionary<AnnotationTool, Button>();
        private readonly List<State> history = new List<State>();
        private int position;
        private bool restoring, drawing;
        private Point start;
        private UIElement draft;
        private TextBox text;
        private Color color = Colors.OrangeRed;
        private double thickness = 3;
        private Button undo, redo;
        private sealed class State { internal Stroke[] Strokes; internal UIElement[] Children; }
        internal AnnotationTool Tool { get; private set; }
        internal bool HasAnnotations { get { return canvas.Strokes.Count > 0 || canvas.Children.Count > 0; } }
        internal bool IsTextEditing { get { return text != null; } }
        internal WrapPanel Toolbar { get; private set; }
        internal event Action Changed;

        internal AnnotationTools(InkCanvas canvas, BitmapSource original)
        {
            this.canvas = canvas; this.original = original;
            canvas.ClipToBounds = true;
            canvas.DefaultDrawingAttributes = new DrawingAttributes { Color = color, Width = thickness, Height = thickness, FitToCurve = true };
            Toolbar = new WrapPanel();
            foreach (AnnotationTool tool in Enum.GetValues(typeof(AnnotationTool)))
            {
                AnnotationTool selected = tool;
                string[] icons = { "hand", "pen", "rectangle", "ellipse", "line", "arrow", "text", "mosaic", "eraser" };
                string[] names = { "拖动 / 调整选区", "画笔", "矩形", "椭圆", "直线", "箭头", "文字（点击输入，Ctrl+Enter 完成）", "马赛克（拖出矩形）", "橡皮（擦除笔迹，其他标注可撤销）" };
                var button = Ui.CompactIconButton(icons[(int)tool], names[(int)tool], delegate { Select(selected); });
                buttons.Add(tool, button); Toolbar.Children.Add(button);
            }
            Toolbar.Children.Add(Ui.CompactDivider());
            undo = Ui.CompactIconButton("undo", "撤销（Ctrl+Z）", Undo); Toolbar.Children.Add(undo);
            redo = Ui.CompactIconButton("redo", "重做（Ctrl+Y / Ctrl+Shift+Z）", Redo); Toolbar.Children.Add(redo);
            Toolbar.Children.Add(Ui.CompactDivider());
            var colors = Ui.CompactIconButton("color", "标注颜色", null); Toolbar.Children.Add(colors);
            var palette = new StackPanel { Orientation = Orientation.Horizontal };
            var palettePopup = new Popup { PlacementTarget = colors, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
                Child = new Border { Child = palette, Background = Ui.Surface, BorderBrush = Ui.Edge, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5), Padding = new Thickness(3) } };
            var colorButtons = new Dictionary<Color, Button>();
            Action refreshColors = delegate
            {
                colors.Foreground = new SolidColorBrush(color);
                colors.ToolTip = "标注颜色：" + PixelLens.Format(color, false);
                foreach (var item in colorButtons)
                {
                    item.Value.BorderBrush = item.Key == color ? Ui.Accent : Brushes.Transparent;
                    item.Value.Background = item.Key == color ? Ui.Selected : Brushes.Transparent;
                }
            };
            foreach (Color choice in new[] { Colors.OrangeRed, Colors.Gold, Colors.LimeGreen, Colors.DodgerBlue, Colors.White, Colors.Black })
            {
                Color selected = choice;
                string name = choice == Colors.OrangeRed ? "红色" : choice == Colors.Gold ? "黄色" : choice == Colors.LimeGreen ? "绿色" : choice == Colors.DodgerBlue ? "蓝色" : choice == Colors.White ? "白色" : "黑色";
                var button = Ui.CompactIconButton("color", "标注颜色：" + name, delegate
                {
                    CommitText(); color = selected; canvas.DefaultDrawingAttributes.Color = color; refreshColors(); palettePopup.IsOpen = false; canvas.Focus();
                });
                palette.Children.Add(button);
                button.Foreground = new SolidColorBrush(choice);
                var dot = (System.Windows.Shapes.Path)button.Content; dot.Width = dot.Height = 12; dot.Stroke = Ui.Muted;
                colorButtons.Add(choice, button);
            }
            refreshColors();
            colors.Click += delegate { palettePopup.IsOpen = !palettePopup.IsOpen; };
            palettePopup.Opened += delegate { colorButtons[color].Focus(); };
            palette.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { palettePopup.IsOpen = false; colors.Focus(); e.Handled = true; }
            };
            var widths = Ui.CompactIconButton("width", "标注线宽：3 px（原图像素）", null); Toolbar.Children.Add(widths);
            var widthMenu = Ui.Menu();
            foreach (double width in new[] { 2.0, 3, 5, 8 })
            {
                double selected = width;
                var item = new MenuItem { Header = width + " px", Tag = width, IsCheckable = true };
                item.Click += delegate
                {
                    thickness = selected; widths.ToolTip = "标注线宽：" + selected + " px（原图像素）";
                    canvas.DefaultDrawingAttributes.Width = canvas.DefaultDrawingAttributes.Height = thickness;
                };
                widthMenu.Items.Add(item);
            }
            widths.Click += delegate
            {
                palettePopup.IsOpen = false;
                foreach (MenuItem item in widthMenu.Items) item.IsChecked = (double)item.Tag == thickness;
                widthMenu.PlacementTarget = widths; widthMenu.IsOpen = true;
            };
            Toolbar.Unloaded += delegate { palettePopup.IsOpen = false; widthMenu.IsOpen = false; };
            canvas.PreviewMouseLeftButtonDown += Down;
            canvas.PreviewMouseMove += Move;
            canvas.PreviewMouseLeftButtonUp += Up;
            canvas.LostMouseCapture += delegate { if (drawing) CancelDraft(); };
            canvas.StrokeCollected += delegate { Record(); };
            canvas.StrokeErased += delegate { Record(); };
            history.Add(Snapshot()); Select(AnnotationTool.Hand);
        }
        internal void Select(AnnotationTool tool)
        {
            CommitText(); CancelDraft(); Tool = tool;
            canvas.EditingMode = tool == AnnotationTool.Pen ? InkCanvasEditingMode.Ink : tool == AnnotationTool.Eraser ? InkCanvasEditingMode.EraseByStroke : InkCanvasEditingMode.None;
            canvas.Cursor = tool == AnnotationTool.Hand ? Cursors.SizeAll : tool == AnnotationTool.Text ? Cursors.IBeam : Cursors.Cross;
            foreach (var button in buttons) Ui.Select(button.Value, button.Key == tool);
            RefreshHistory();
        }
        private Point Clamp(Point p) { return new Point(Math.Max(0, Math.Min(canvas.Width, p.X)), Math.Max(0, Math.Min(canvas.Height, p.Y))); }
        private void Down(object sender, MouseButtonEventArgs e)
        {
            if (Tool == AnnotationTool.Hand || Tool == AnnotationTool.Pen || Tool == AnnotationTool.Eraser || text != null && text.IsMouseOver) return;
            CommitText(); start = Clamp(e.GetPosition(canvas)); e.Handled = true;
            if (Tool == AnnotationTool.Text) { BeginText(start); return; }
            drawing = canvas.CaptureMouse();
        }
        private void Move(object sender, MouseEventArgs e)
        {
            if (!drawing) return;
            if (draft != null) canvas.Children.Remove(draft);
            Point end = Clamp(e.GetPosition(canvas));
            if (Tool == AnnotationTool.Mosaic)
            {
                var rect = new Rect(start, end);
                draft = new System.Windows.Shapes.Rectangle { Width = rect.Width, Height = rect.Height, Fill = new SolidColorBrush(Color.FromArgb(110, 180, 180, 180)), IsHitTestVisible = false };
                InkCanvas.SetLeft(draft, rect.X); InkCanvas.SetTop(draft, rect.Y);
            }
            else draft = CreateMark(Tool, start, end, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            if (draft != null) canvas.Children.Add(draft);
            e.Handled = true;
        }
        private void Up(object sender, MouseButtonEventArgs e)
        {
            if (!drawing) return;
            Move(sender, e); drawing = false; canvas.ReleaseMouseCapture();
            if (Tool == AnnotationTool.Mosaic)
            {
                canvas.Children.Remove(draft);
                draft = CreateMark(Tool, start, Clamp(e.GetPosition(canvas)), false);
                if (draft != null) canvas.Children.Add(draft);
            }
            if (draft != null) { draft = null; Record(); }
            e.Handled = true;
        }
        private void CancelDraft()
        {
            drawing = false;
            if (draft != null) { canvas.Children.Remove(draft); draft = null; }
            if (canvas.IsMouseCaptured) canvas.ReleaseMouseCapture();
        }
        internal void AddMark(AnnotationTool tool, Point from, Point to, bool constrain = false)
        {
            CommitText(); var mark = CreateMark(tool, Clamp(from), Clamp(to), constrain);
            if (mark != null) { canvas.Children.Add(mark); Record(); }
        }
        private UIElement CreateMark(AnnotationTool tool, Point from, Point to, bool constrain)
        {
            if ((to - from).Length < 1) return null;
            if (constrain)
            {
                Vector delta = to - from;
                if (tool == AnnotationTool.Rectangle || tool == AnnotationTool.Ellipse)
                {
                    double side = Math.Min(Math.Abs(delta.X), Math.Abs(delta.Y));
                    to = from + new Vector(Math.Sign(delta.X) * side, Math.Sign(delta.Y) * side);
                }
                else if (tool == AnnotationTool.Line || tool == AnnotationTool.Arrow)
                {
                    double angle = Math.Round(Math.Atan2(delta.Y, delta.X) / (Math.PI / 4)) * Math.PI / 4;
                    to = Clamp(from + new Vector(Math.Cos(angle), Math.Sin(angle)) * delta.Length);
                }
            }
            Rect rect = new Rect(from, to);
            if (tool == AnnotationTool.Mosaic)
            {
                Int32Rect pixels = ScreenCapture.PixelRect(rect, original.PixelWidth, original.PixelHeight);
                if (pixels.Width == 0 || pixels.Height == 0) return null;
                // Mosaic the current composite, so it also covers earlier text or strokes.
                BitmapSource image = Preview.RenderAnnotation(canvas);
                var crop = new CroppedBitmap(image, pixels);
                var small = new TransformedBitmap(crop, new ScaleTransform(Math.Max(1, pixels.Width / 12) / (double)pixels.Width, Math.Max(1, pixels.Height / 12) / (double)pixels.Height));
                // Materialize the tiny mosaic so undo history cannot retain a full-canvas snapshot per mark.
                var detached = new WriteableBitmap(small); detached.Freeze();
                var mosaic = new Image { Source = detached, Width = pixels.Width, Height = pixels.Height, Stretch = Stretch.Fill, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(mosaic, BitmapScalingMode.NearestNeighbor);
                InkCanvas.SetLeft(mosaic, pixels.X); InkCanvas.SetTop(mosaic, pixels.Y); return mosaic;
            }
            Geometry geometry;
            if (tool == AnnotationTool.Rectangle) geometry = new RectangleGeometry(rect);
            else if (tool == AnnotationTool.Ellipse) geometry = new EllipseGeometry(rect);
            else
            {
                var group = new GeometryGroup(); group.Children.Add(new LineGeometry(from, to));
                if (tool == AnnotationTool.Arrow)
                {
                    Vector direction = from - to; direction.Normalize();
                    Vector side = new Vector(-direction.Y, direction.X);
                    double size = Math.Min((to - from).Length * .45, Math.Max(12, thickness * 4));
                    group.Children.Add(new LineGeometry(to, to + direction * size + side * size * .5));
                    group.Children.Add(new LineGeometry(to, to + direction * size - side * size * .5));
                }
                geometry = group;
            }
            return new System.Windows.Shapes.Path { Data = geometry, Stroke = new SolidColorBrush(color), StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
        }
        private void BeginText(Point point)
        {
            text = new TextBox { Foreground = new SolidColorBrush(color), Background = Ui.Brush(32, 32, 32), FontSize = 16 + thickness * 2,
                BorderBrush = Ui.Accent, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinWidth = Math.Min(140, canvas.Width - point.X), MaxWidth = Math.Max(1, canvas.Width - point.X), Padding = new Thickness(2) };
            InkCanvas.SetLeft(text, point.X); InkCanvas.SetTop(text, point.Y); canvas.Children.Add(text);
            text.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { DiscardText(); e.Handled = true; }
                else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { CommitText(); e.Handled = true; }
            };
            text.Focus();
        }
        internal void AddText(Point point, string value)
        {
            CommitText(); BeginText(Clamp(point)); text.Text = value; CommitText();
        }
        internal void CommitText()
        {
            if (text == null) return;
            var input = text; text = null; canvas.Children.Remove(input); canvas.Focus();
            if (string.IsNullOrWhiteSpace(input.Text)) return;
            var label = new TextBlock { Text = input.Text, FontSize = input.FontSize, Foreground = input.Foreground, FontFamily = input.FontFamily,
                TextWrapping = TextWrapping.Wrap, MaxWidth = input.MaxWidth, IsHitTestVisible = false };
            InkCanvas.SetLeft(label, InkCanvas.GetLeft(input)); InkCanvas.SetTop(label, InkCanvas.GetTop(input)); canvas.Children.Add(label); Record();
        }
        internal void DiscardText() { if (text != null) { canvas.Children.Remove(text); text = null; canvas.Focus(); } }
        private State Snapshot() { return new State { Strokes = canvas.Strokes.ToArray(), Children = canvas.Children.Cast<UIElement>().ToArray() }; }
        private void Record()
        {
            if (restoring) return;
            history.RemoveRange(position + 1, history.Count - position - 1);
            history.Add(Snapshot()); position++;
            // History stores references, without duplicating full screenshot pixels.
            if (history.Count > 101) { history.RemoveAt(0); position--; }
            RefreshHistory(); if (Changed != null) Changed();
        }
        private void Restore()
        {
            restoring = true;
            try
            {
                canvas.Strokes = new StrokeCollection(history[position].Strokes);
                canvas.Children.Clear(); foreach (UIElement child in history[position].Children) canvas.Children.Add(child);
            }
            finally { restoring = false; }
            RefreshHistory(); if (Changed != null) Changed();
        }
        internal void Undo() { CommitText(); CancelDraft(); if (position > 0) { position--; Restore(); } }
        internal void Redo() { CommitText(); CancelDraft(); if (position + 1 < history.Count) { position++; Restore(); } }
        private void RefreshHistory() { undo.IsEnabled = position > 0; redo.IsEnabled = position + 1 < history.Count; }
        internal bool HandleKey(KeyEventArgs e)
        {
            if (IsTextEditing || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return false;
            if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) Undo();
            else if (e.Key == Key.Y || e.Key == Key.Z) Redo();
            else return false;
            return true;
        }
    }
}
