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
using System.Windows.Shapes;

namespace Snapline
{
    internal enum AnnotationTool { Hand, Pen, Rectangle, Ellipse, Line, Arrow, Text, Mosaic, Eraser }

    // Both the capture overlay and the image preview edit in original image pixels.
    internal sealed class AnnotationTools
    {
        private readonly InkCanvas canvas;
        private readonly BitmapSource original;
        private readonly Brush exportBackground;
        private readonly Dictionary<AnnotationTool, Button> buttons = new Dictionary<AnnotationTool, Button>();
        private readonly List<State> history = new List<State>();
        private int position;
        private bool restoring, drawing;
        private Point start;
        private UIElement draft;
        private TextBox text;
        private TextBlock movingText, textBeforeMove;
        private Point textOrigin;
        private Size textSize;
        private Color color = Colors.OrangeRed;
        private double thickness = 3;
        private Button undo, redo;
        private sealed class State { internal Stroke[] Strokes; internal UIElement[] Children; }
        internal AnnotationTool Tool { get; private set; }
        internal bool HasAnnotations { get { return canvas.Strokes.Count > 0 || canvas.Children.Count > 0; } }
        internal bool IsTextEditing { get { return text != null; } }
        internal WrapPanel Toolbar { get; private set; }
        internal event Action Changed;

        internal AnnotationTools(InkCanvas canvas, BitmapSource original, Brush exportBackground = null)
        {
            this.canvas = canvas; this.original = original; this.exportBackground = exportBackground;
            canvas.ClipToBounds = true;
            canvas.DefaultDrawingAttributes = new DrawingAttributes { Color = color, Width = thickness, Height = thickness, FitToCurve = true };
            Toolbar = new WrapPanel();
            foreach (AnnotationTool tool in Enum.GetValues(typeof(AnnotationTool)))
            {
                AnnotationTool selected = tool;
                string[] icons = { "hand", "pen", "rectangle", "ellipse", "line", "arrow", "text", "mosaic", "eraser" };
                string[] names = { "拖动 / 调整选区", "画笔", "矩形", "椭圆", "直线", "箭头", "文字（点击输入，Ctrl+Enter 完成，拖动可移动）", "马赛克（拖出矩形）", "橡皮（擦除笔迹，其他标注可撤销）" };
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
            canvas.LostMouseCapture += delegate { if (drawing || movingText != null) CancelDraft(); };
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
            if (Tool == AnnotationTool.Pen || Tool == AnnotationTool.Eraser || text != null && text.IsMouseOver) return;
            Point point = Clamp(e.GetPosition(canvas));
            if (Tool == AnnotationTool.Hand || Tool == AnnotationTool.Text)
            {
                TextBlock existing = FindText(point);
                if (existing != null)
                {
                    CommitText();
                    if (canvas.CaptureMouse()) BeginTextMove(existing, point);
                    e.Handled = true;
                    return;
                }
                if (Tool == AnnotationTool.Hand) return;
            }
            CommitText(); start = point; e.Handled = true;
            if (Tool == AnnotationTool.Text) { BeginText(start); return; }
            drawing = canvas.CaptureMouse();
        }
        private void Move(object sender, MouseEventArgs e)
        {
            if (movingText != null)
            {
                MoveText(e.GetPosition(canvas));
                e.Handled = true;
                return;
            }
            if (!drawing)
            {
                if (Tool == AnnotationTool.Text) canvas.Cursor = HasTextAt(e.GetPosition(canvas)) ? Cursors.SizeAll : Cursors.IBeam;
                return;
            }
            UpdateDraft(Clamp(e.GetPosition(canvas)));
            e.Handled = true;
        }
        private void UpdateDraft(Point end)
        {
            if ((end - start).Length < 1) { RemoveDraft(); return; }
            if (Tool == AnnotationTool.Mosaic)
            {
                var rect = new Rect(start, end);
                var outline = (Rectangle)draft;
                if (outline == null)
                {
                    outline = new Rectangle { Stroke = Ui.Accent, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 2 }, IsHitTestVisible = false };
                    draft = outline; canvas.Children.Add(outline);
                }
                outline.Width = rect.Width; outline.Height = rect.Height;
                InkCanvas.SetLeft(outline, rect.X); InkCanvas.SetTop(outline, rect.Y);
                return;
            }
            var geometry = MarkGeometry(Tool, start, end, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            if (draft != null) ((Path)draft).Data = geometry;
            else { draft = ShapeMark(geometry); canvas.Children.Add(draft); }
        }
        private void Up(object sender, MouseButtonEventArgs e)
        {
            if (movingText != null)
            {
                MoveText(e.GetPosition(canvas));
                EndTextMove(true);
                canvas.ReleaseMouseCapture();
                e.Handled = true;
                return;
            }
            if (!drawing) return;
            Point end = Clamp(e.GetPosition(canvas));
            drawing = false; canvas.ReleaseMouseCapture();
            if (Tool == AnnotationTool.Mosaic)
            {
                RemoveDraft();
                draft = CreateMark(Tool, start, end, false);
                if (draft != null) canvas.Children.Add(draft);
            }
            else UpdateDraft(end);
            if (draft != null) { draft = null; Record(); }
            e.Handled = true;
        }
        private void RemoveDraft()
        {
            if (draft == null) return;
            canvas.Children.Remove(draft); draft = null;
        }
        private void CancelDraft()
        {
            if (movingText != null) EndTextMove(false);
            drawing = false;
            RemoveDraft();
            if (canvas.IsMouseCaptured) canvas.ReleaseMouseCapture();
        }
        internal void AddMark(AnnotationTool tool, Point from, Point to, bool constrain = false)
        {
            CommitText(); var mark = CreateMark(tool, Clamp(from), Clamp(to), constrain);
            if (mark != null) { canvas.Children.Add(mark); Record(); }
        }
        internal BitmapSource Render()
        {
            CancelDraft(); CommitText();
            if (exportBackground != null && !HasAnnotations)
            {
                // Copy just the selection into a fresh buffer so a pin cannot retain the desktop.
                var copy = new WriteableBitmap(original.PixelWidth, original.PixelHeight, 96, 96, original.Format, original.Palette);
                var area = new Int32Rect(0, 0, copy.PixelWidth, copy.PixelHeight);
                copy.Lock();
                try
                {
                    original.CopyPixels(area, copy.BackBuffer, checked(copy.BackBufferStride * copy.PixelHeight), copy.BackBufferStride);
                    copy.AddDirtyRect(area);
                }
                finally { copy.Unlock(); }
                copy.Freeze();
                return copy;
            }
            return Preview.RenderAnnotation(canvas, background: exportBackground);
        }
        private UIElement CreateMark(AnnotationTool tool, Point from, Point to, bool constrain)
        {
            if ((to - from).Length < 1) return null;
            if (tool != AnnotationTool.Mosaic) return ShapeMark(MarkGeometry(tool, from, to, constrain));

            Int32Rect pixels = ScreenCapture.PixelRect(new Rect(from, to), original.PixelWidth, original.PixelHeight);
            if (pixels.Width == 0 || pixels.Height == 0) return null;
            // Render the patch at original resolution so mosaic also covers earlier text and strokes.
            BitmapSource patch = Preview.RenderAnnotation(canvas, pixels, exportBackground);
            var small = new TransformedBitmap(patch, new ScaleTransform(Math.Max(1, pixels.Width / 12) / (double)pixels.Width, Math.Max(1, pixels.Height / 12) / (double)pixels.Height));
            // Detach the small bitmap so undo history does not retain the rendered patch.
            var detached = new WriteableBitmap(small); detached.Freeze();
            var mosaic = new Image { Source = detached, Width = pixels.Width, Height = pixels.Height, Stretch = Stretch.Fill, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(mosaic, BitmapScalingMode.NearestNeighbor);
            InkCanvas.SetLeft(mosaic, pixels.X); InkCanvas.SetTop(mosaic, pixels.Y); return mosaic;
        }
        private Geometry MarkGeometry(AnnotationTool tool, Point from, Point to, bool constrain)
        {
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
            if (tool == AnnotationTool.Rectangle) return new RectangleGeometry(rect);
            if (tool == AnnotationTool.Ellipse) return new EllipseGeometry(rect);

            var group = new GeometryGroup(); group.Children.Add(new LineGeometry(from, to));
            if (tool == AnnotationTool.Arrow && to != from)
            {
                Vector direction = from - to; direction.Normalize();
                Vector side = new Vector(-direction.Y, direction.X);
                double size = Math.Min((to - from).Length * .45, Math.Max(12, thickness * 4));
                group.Children.Add(new LineGeometry(to, to + direction * size + side * size * .5));
                group.Children.Add(new LineGeometry(to, to + direction * size - side * size * .5));
            }
            return group;
        }
        private Path ShapeMark(Geometry geometry)
        {
            return new Path { Data = geometry, Stroke = new SolidColorBrush(color), StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
        }
        private void BeginText(Point point)
        {
            double size = 16 + thickness * 2;
            double minWidth = Math.Min(140, canvas.Width);
            point.X = Math.Min(point.X, canvas.Width - minWidth);
            point.Y = Math.Min(point.Y, Math.Max(0, canvas.Height - Math.Ceiling(size * 1.4)));
            text = new TextBox { Foreground = new SolidColorBrush(color), Background = Brushes.Transparent, FontSize = size,
                BorderBrush = Ui.Accent, BorderThickness = new Thickness(1), CaretBrush = Ui.Accent, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                MinWidth = minWidth, MaxWidth = canvas.Width - point.X, Padding = new Thickness(2) };
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
            var label = TextLabel(input.Text, input.FontSize, input.Foreground, input.FontFamily, input.MaxWidth);
            label.Measure(new Size(label.MaxWidth, double.PositiveInfinity));
            InkCanvas.SetLeft(label, InkCanvas.GetLeft(input));
            InkCanvas.SetTop(label, Math.Min(InkCanvas.GetTop(input), Math.Max(0, canvas.Height - label.DesiredSize.Height)));
            canvas.Children.Add(label);
            Record();
        }
        private static TextBlock TextLabel(string value, double size, Brush ink, FontFamily family, double maxWidth)
        {
            return new TextBlock { Text = value, FontSize = size, Foreground = ink, FontFamily = family,
                TextWrapping = TextWrapping.Wrap, MaxWidth = maxWidth, IsHitTestVisible = false };
        }
        private TextBlock FindText(Point point)
        {
            return canvas.Children.OfType<TextBlock>().Reverse().FirstOrDefault(label =>
                new Rect(new Point(InkCanvas.GetLeft(label), InkCanvas.GetTop(label)), label.RenderSize).Contains(point));
        }
        internal bool HasTextAt(Point point) { return FindText(point) != null; }
        private void ReplaceText(TextBlock current, TextBlock replacement)
        {
            int index = canvas.Children.IndexOf(current);
            canvas.Children.RemoveAt(index);
            canvas.Children.Insert(index, replacement);
        }
        private void BeginTextMove(TextBlock existing, Point point)
        {
            textBeforeMove = existing;
            textOrigin = new Point(InkCanvas.GetLeft(existing), InkCanvas.GetTop(existing));
            textSize = existing.RenderSize;
            start = point;
            // Keep the committed label untouched because earlier undo states reference it.
            movingText = TextLabel(existing.Text, existing.FontSize, existing.Foreground, existing.FontFamily, existing.MaxWidth);
            InkCanvas.SetLeft(movingText, textOrigin.X); InkCanvas.SetTop(movingText, textOrigin.Y);
            ReplaceText(existing, movingText);
            canvas.Cursor = Cursors.SizeAll;
        }
        private void MoveText(Point point)
        {
            Point destination = textOrigin + (point - start);
            InkCanvas.SetLeft(movingText, Math.Max(0, Math.Min(canvas.Width - textSize.Width, destination.X)));
            InkCanvas.SetTop(movingText, Math.Max(0, Math.Min(canvas.Height - textSize.Height, destination.Y)));
        }
        private void EndTextMove(bool commit)
        {
            TextBlock label = movingText, previous = textBeforeMove;
            movingText = textBeforeMove = null;
            if (commit && (InkCanvas.GetLeft(label) != textOrigin.X || InkCanvas.GetTop(label) != textOrigin.Y)) Record();
            else ReplaceText(label, previous);
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
