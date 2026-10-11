using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Snapline
{
    internal sealed class Preview : Window
    {
        private readonly InkCanvas canvas;
        private readonly ScrollViewer viewer;
        private readonly TextBlock info;
        private readonly BitmapSource original;
        private readonly Button zoomLabel;
        private readonly Slider zoomSlider;
        private bool updatingZoom;
        private bool fit = true;
        private double zoom = 1;
        private int rotation;
        private Point panStart;
        private double panX, panY;
        private bool panning;
        internal double Zoom { get { return zoom; } }
        internal int Rotation { get { return rotation; } }
        internal Preview(Store store, Shot shot, bool editing)
        {
            Title = "截图预览";
            Width = Math.Min(1100, SystemParameters.WorkArea.Width - 80);
            Height = Math.Min(800, SystemParameters.WorkArea.Height - 80);
            MinWidth = 420; MinHeight = 320; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Ui.Brush(32, 32, 32); FontFamily = new FontFamily("Microsoft YaHei UI");
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            original = Store.ReadImage(shot.Path);
            canvas = new InkCanvas { Width = original.PixelWidth, Height = original.PixelHeight,
                Background = new ImageBrush(original) { Stretch = Stretch.Fill }, EditingMode = InkCanvasEditingMode.None };
            canvas.DefaultDrawingAttributes = new DrawingAttributes { Color = Colors.OrangeRed,
                Width = Math.Max(3, original.PixelWidth / 240.0), Height = Math.Max(3, original.PixelWidth / 240.0), FitToCurve = true };
            var layout = new DockPanel { Background = Ui.Brush(32, 32, 32) };
            var top = new StackPanel { Background = Ui.Brush(32, 32, 32) };
            var command = new Grid { Margin = new Thickness(8, 4, 8, 4), Height = 40 };
            command.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            command.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            command.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
            Button annotate = AddIcon(toolbar, "edit", "展开或收起标注工具", null);
            AddIcon(toolbar, "rotateLeft", "向左旋转 90°", delegate { Rotate(-90); });
            AddIcon(toolbar, "rotateRight", "向右旋转 90°", delegate { Rotate(90); });
            command.Children.Add(toolbar);
            var filename = new TextBlock { Text = System.IO.Path.GetFileName(shot.Path), FontSize = 12, Foreground = Ui.Text,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(20, 0, 20, 0) };
            Grid.SetColumn(filename, 1); command.Children.Add(filename);
            var actions = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(actions, 2); command.Children.Add(actions);
            AddIcon(actions, "save", "保存旋转及标注到应用副本，原图保留", delegate
            {
                try { store.Replace(shot, RenderEdited()); Close(); }
                catch (Exception) { MessageBox.Show(this, "保存失败，请检查文件权限或磁盘空间。", "截图晾衣绳"); }
            });
            var layerButton = AddIcon(actions, "layers", "窗口层级", null);
            var layerMenu = new ContextMenu();
            foreach (WindowLayer layer in Enum.GetValues(typeof(WindowLayer)))
            {
                WindowLayer selectedLayer = layer;
                var item = new MenuItem { Header = Native.LayerName(layer), IsCheckable = true, Tag = layer };
                item.Click += delegate { store.SetLayer(selectedLayer); }; layerMenu.Items.Add(item);
            }
            layerButton.Click += delegate
            {
                foreach (MenuItem item in layerMenu.Items) item.IsChecked = (WindowLayer)item.Tag == store.Settings.Layer;
                layerMenu.PlacementTarget = layerButton; layerMenu.IsOpen = true;
            };
            Action applyLayer = delegate { Native.ApplyLayer(this, store.Settings.Layer); layerButton.ToolTip = "窗口层级：" + Native.LayerName(store.Settings.Layer); };
            store.LayerChanged += applyLayer; SourceInitialized += delegate { applyLayer(); };
            Activated += delegate { if (store.Settings.Layer == WindowLayer.Bottom) applyLayer(); };
            Closed += delegate { store.LayerChanged -= applyLayer; };
            Topmost = store.Settings.Layer == WindowLayer.Top; ShowActivated = store.Settings.Layer != WindowLayer.Bottom;
            layerButton.ToolTip = "窗口层级：" + Native.LayerName(store.Settings.Layer);
            top.Children.Add(command);
            var annotation = new WrapPanel { Margin = new Thickness(8, 0, 8, 4), Visibility = editing ? Visibility.Visible : Visibility.Collapsed };
            Button pen = null, eraser = null, browse = null;
            Action refresh = delegate
            {
                pen.Background = canvas.EditingMode == InkCanvasEditingMode.Ink ? Ui.Brush(65, 65, 65) : Brushes.Transparent;
                eraser.Background = canvas.EditingMode == InkCanvasEditingMode.EraseByStroke ? Ui.Brush(65, 65, 65) : Brushes.Transparent;
                browse.Background = canvas.EditingMode == InkCanvasEditingMode.None ? Ui.Brush(65, 65, 65) : Brushes.Transparent;
                annotate.Background = annotation.Visibility == Visibility.Visible ? Ui.Brush(65, 65, 65) : Brushes.Transparent;
                canvas.Cursor = canvas.EditingMode == InkCanvasEditingMode.None ? Cursors.Hand : Cursors.Cross;
            };
            annotate.Click += delegate
            {
                annotation.Visibility = annotation.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                canvas.EditingMode = annotation.Visibility == Visibility.Visible ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None; refresh();
            };
            browse = AddIcon(annotation, "hand", "拖动查看图片", delegate { canvas.EditingMode = InkCanvasEditingMode.None; refresh(); });
            pen = AddIcon(annotation, "pen", "画笔标注", delegate { canvas.EditingMode = InkCanvasEditingMode.Ink; refresh(); });
            eraser = AddIcon(annotation, "eraser", "擦除整笔标注", delegate { canvas.EditingMode = InkCanvasEditingMode.EraseByStroke; refresh(); });
            Action undo = delegate { if (canvas.Strokes.Count > 0) canvas.Strokes.RemoveAt(canvas.Strokes.Count - 1); };
            var undoButton = AddIcon(annotation, "undo", "撤销上一笔（Ctrl+Z）", undo); undoButton.IsEnabled = false;
            canvas.Strokes.StrokesChanged += delegate { undoButton.IsEnabled = canvas.Strokes.Count > 0; };
            foreach (Color color in new[] { Colors.OrangeRed, Colors.Gold, Colors.DodgerBlue })
            {
                Color selected = color;
                var button = AddIcon(annotation, "color", color == Colors.OrangeRed ? "红色" : color == Colors.Gold ? "黄色" : "蓝色",
                    delegate { canvas.DefaultDrawingAttributes.Color = selected; canvas.EditingMode = InkCanvasEditingMode.Ink; refresh(); });
                button.Foreground = new SolidColorBrush(color);
            }
            top.Children.Add(annotation); DockPanel.SetDock(top, Dock.Top); layout.Children.Add(top);
            var bottom = new Grid { Background = Ui.Brush(39, 39, 39), Height = 42 };
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            info = new TextBlock { Foreground = Ui.Brush(190, 190, 190), FontSize = 11, Margin = new Thickness(16, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = "保存只修改应用副本" };
            bottom.Children.Add(info);
            var zoomTools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 3, 8, 3) };
            Grid.SetColumn(zoomTools, 1); bottom.Children.Add(zoomTools);
            AddIcon(zoomTools, "fit", "适应窗口（Ctrl+0）", FitImage);
            zoomLabel = AddIcon(zoomTools, "", "缩放比例：选择适应窗口或原始大小", null); zoomLabel.Width = 68;
            var zoomMenu = new ContextMenu();
            var fitItem = new MenuItem { Header = "适应窗口" }; fitItem.Click += delegate { FitImage(); }; zoomMenu.Items.Add(fitItem);
            foreach (double ratio in new[] { .25, .5, 1, 2, 4 })
            {
                double selected = ratio; var item = new MenuItem { Header = (ratio * 100) + "%" }; item.Click += delegate { ChangeZoom(selected); }; zoomMenu.Items.Add(item);
            }
            zoomLabel.Click += delegate { zoomMenu.PlacementTarget = zoomLabel; zoomMenu.IsOpen = true; };
            AddIcon(zoomTools, "minus", "缩小（Ctrl+− / 滚轮）", delegate { ChangeZoom(zoom / 1.25); });
            zoomSlider = new Slider { Minimum = Math.Log(.05), Maximum = Math.Log(8), Width = 140, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), ToolTip = "缩放", IsMoveToPointEnabled = true };
            System.Windows.Automation.AutomationProperties.SetName(zoomSlider, "缩放比例");
            zoomSlider.ValueChanged += delegate { if (!updatingZoom) ChangeZoom(Math.Exp(zoomSlider.Value)); }; zoomTools.Children.Add(zoomSlider);
            AddIcon(zoomTools, "plus", "放大（Ctrl++ / 滚轮）", delegate { ChangeZoom(zoom * 1.25); });
            DockPanel.SetDock(bottom, Dock.Bottom); layout.Children.Add(bottom);
            layout.SizeChanged += delegate
            {
                filename.Visibility = layout.ActualWidth < 650 ? Visibility.Collapsed : Visibility.Visible;
                zoomSlider.Visibility = layout.ActualWidth < 650 ? Visibility.Collapsed : Visibility.Visible;
            };
            var stage = new Grid { Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stage.Children.Add(canvas);
            viewer = new ScrollViewer { Background = Ui.Brush(32, 32, 32), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stage, CanContentScroll = false, PanningMode = PanningMode.Both };
            layout.Children.Add(viewer); Content = layout;
            viewer.SizeChanged += delegate { if (fit) FitImage(); };
            Loaded += delegate { FitImage(); };
            viewer.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e) { ChangeZoom(zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15)); e.Handled = true; };
            canvas.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (canvas.EditingMode != InkCanvasEditingMode.None) return;
                panStart = e.GetPosition(viewer); panX = viewer.HorizontalOffset; panY = viewer.VerticalOffset;
                panning = canvas.CaptureMouse(); e.Handled = true;
            };
            canvas.PreviewMouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!panning) return;
                Point point = e.GetPosition(viewer);
                viewer.ScrollToHorizontalOffset(panX + panStart.X - point.X);
                viewer.ScrollToVerticalOffset(panY + panStart.Y - point.Y); e.Handled = true;
            };
            canvas.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) { if (panning) { panning = false; canvas.ReleaseMouseCapture(); e.Handled = true; } };
            canvas.LostMouseCapture += delegate { panning = false; };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) Close();
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                if (e.Key == Key.Z) undo();
                else if (e.Key == Key.D0 || e.Key == Key.NumPad0) FitImage();
                else if (e.Key == Key.D1 || e.Key == Key.NumPad1) ChangeZoom(1);
                else if (e.Key == Key.Add || e.Key == Key.OemPlus) ChangeZoom(zoom * 1.25);
                else if (e.Key == Key.Subtract || e.Key == Key.OemMinus) ChangeZoom(zoom / 1.25);
                else return;
                e.Handled = true;
            };
            canvas.EditingMode = editing ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None; refresh(); UpdateTransform();
        }
        internal void FitImage()
        {
            fit = true;
            double width = rotation % 180 == 0 ? canvas.Width : canvas.Height;
            double height = rotation % 180 == 0 ? canvas.Height : canvas.Width;
            zoom = Math.Max(.0001, Math.Min(1, Math.Min(Math.Max(1, viewer.ActualWidth - 20) / width, Math.Max(1, viewer.ActualHeight - 20) / height)));
            UpdateTransform(); viewer.ScrollToHorizontalOffset(0); viewer.ScrollToVerticalOffset(0);
        }
        internal void ChangeZoom(double value)
        {
            double old = zoom;
            double x = viewer.HorizontalOffset + viewer.ViewportWidth / 2, y = viewer.VerticalOffset + viewer.ViewportHeight / 2;
            fit = false; zoom = Math.Max(Math.Min(.05, zoom), Math.Min(8, value)); UpdateTransform();
            double ratio = zoom / old;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
            {
                viewer.ScrollToHorizontalOffset(x * ratio - viewer.ViewportWidth / 2);
                viewer.ScrollToVerticalOffset(y * ratio - viewer.ViewportHeight / 2);
            }));
        }
        internal void Rotate(int degrees)
        {
            rotation = (rotation + degrees + 360) % 360;
            if (fit) FitImage(); else { UpdateTransform(); viewer.ScrollToHorizontalOffset(0); viewer.ScrollToVerticalOffset(0); }
        }
        private void UpdateTransform()
        {
            var transform = new TransformGroup(); transform.Children.Add(new RotateTransform(rotation)); transform.Children.Add(new ScaleTransform(zoom, zoom));
            canvas.LayoutTransform = transform;
            if (info != null) info.Text = string.Format("PNG   ·   {0} × {1}{2}", original.PixelWidth, original.PixelHeight, rotation == 0 ? "" : "   ·   " + rotation + "°");
            if (zoomLabel != null) zoomLabel.Content = string.Format("{0:0.#}%", zoom * 100);
            if (zoomSlider != null) { updatingZoom = true; zoomSlider.Value = Math.Log(Math.Max(.05, zoom)); updatingZoom = false; }
        }
        internal BitmapSource RenderEdited()
        {
            Transform transform = canvas.LayoutTransform;
            BitmapSource result;
            try { canvas.LayoutTransform = Transform.Identity; result = RenderAnnotation(canvas); }
            finally { canvas.LayoutTransform = transform; }
            if (rotation == 0) return result;
            var rotated = new TransformedBitmap(result, new RotateTransform(rotation)); rotated.Freeze(); return rotated;
        }
        private static Button AddIcon(Panel toolbar, string icon, string tip, Action action)
        {
            var button = new Button { Width = 36, Height = 34, MinHeight = 32, Background = Brushes.Transparent, Foreground = Ui.Text,
                BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(0), ToolTip = tip, Tag = icon, FontSize = 12, Cursor = Cursors.Hand, Margin = new Thickness(2, 0, 2, 0) };
            System.Windows.Automation.AutomationProperties.SetName(button, tip);
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border), "Surface");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4)); border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(content); template.VisualTree = border;
            foreach (var state in new[] { new Trigger { Property = IsMouseOverProperty, Value = true }, new Trigger { Property = Button.IsPressedProperty, Value = true }, new Trigger { Property = IsKeyboardFocusedProperty, Value = true } })
            { state.Setters.Add(new Setter(Border.BackgroundProperty, Ui.Brush(65, 65, 65), "Surface")); template.Triggers.Add(state); }
            var disabled = new Trigger { Property = IsEnabledProperty, Value = false }; disabled.Setters.Add(new Setter(OpacityProperty, .35, "Surface")); template.Triggers.Add(disabled); button.Template = template;
            if (icon != "")
            {
                string geometry;
                switch (icon)
                {
                    case "edit": case "pen": geometry = "M3,15 L3,18 L6,18 L17,7 L13,3 Z M11,5 L15,9"; break;
                    case "rotateLeft": geometry = "M3,8 A7,7 0 1 1 4,15 M3,3 L3,8 L8,8"; break;
                    case "rotateRight": geometry = "M17,8 A7,7 0 1 0 16,15 M17,3 L17,8 L12,8"; break;
                    case "save": geometry = "M3,2 L15,2 L18,5 L18,18 L2,18 L2,2 Z M6,2 L6,7 L14,7 L14,2 M6,18 L6,11 L14,11 L14,18"; break;
                    case "layers": geometry = "M2,6 L10,2 L18,6 L10,10 Z M2,10 L10,14 L18,10 M2,14 L10,18 L18,14"; break;
                    case "hand": geometry = "M6,11 L6,5 Q6,2 8,5 L8,10 L8,3 Q9,1 10,3 L10,10 L10,4 Q12,2 12,4 L12,10 L12,6 Q14,4 14,6 L14,13 Q14,18 10,18 L7,18 L2,12 Q2,9 5,12 Z"; break;
                    case "eraser": geometry = "M2,12 L11,3 L18,10 L10,18 L8,18 Z M7,7 L14,14 M8,18 L19,18"; break;
                    case "undo": geometry = "M3,9 L3,3 M3,9 L9,9 M3,9 Q8,1 14,6 Q20,12 14,17"; break;
                    case "fit": geometry = "M7,3 L3,3 L3,7 M13,3 L17,3 L17,7 M3,13 L3,17 L7,17 M13,17 L17,17 L17,13 M7,7 L13,7 L13,13 L7,13 Z"; break;
                    case "minus": geometry = "M10,4 A6,6 0 1 1 9.99,4 M14,14 L19,19 M7,10 L13,10"; break;
                    case "plus": geometry = "M10,4 A6,6 0 1 1 9.99,4 M14,14 L19,19 M7,10 L13,10 M10,7 L10,13"; break;
                    default: geometry = "M10,3 A7,7 0 1 1 9.99,3 Z"; break;
                }
                var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(geometry), Width = 18, Height = 18, Stretch = Stretch.Uniform, StrokeThickness = 1.4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
                path.SetBinding(System.Windows.Shapes.Path.StrokeProperty, new System.Windows.Data.Binding("Foreground") { Source = button });
                if (icon == "color") path.SetBinding(System.Windows.Shapes.Path.FillProperty, new System.Windows.Data.Binding("Foreground") { Source = button });
                button.Content = path;
            }
            if (action != null) button.Click += delegate { action(); }; toolbar.Children.Add(button); return button;
        }
        internal static BitmapSource RenderAnnotation(InkCanvas canvas)
        {
            canvas.Measure(new Size(canvas.Width, canvas.Height)); canvas.Arrange(new Rect(0, 0, canvas.Width, canvas.Height)); canvas.UpdateLayout();
            var result = new RenderTargetBitmap((int)canvas.Width, (int)canvas.Height, 96, 96, PixelFormats.Pbgra32);
            result.Render(canvas); result.Freeze(); return result;
        }
    }
}
