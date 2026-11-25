using System;
using System.IO;
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
        private readonly AnnotationTools annotations;
        private readonly Button zoomLabel;
        private readonly Slider zoomSlider;
        private DispatcherOperation pendingZoom;
        private bool updatingZoom, saving;
        private bool fit = true;
        private double zoom = 1;
        private int rotation;
        private Point panStart;
        private double panX, panY;
        private bool panning;
        internal double Zoom { get { return zoom; } }
        internal int Rotation { get { return rotation; } }
        internal Preview(Store store, Shot shot, bool editing, Action<BitmapSource> pinImage = null)
        {
            Title = "截图预览";
            Icon = AppIcon.WindowImage();
            Width = Math.Min(1100, SystemParameters.WorkArea.Width - 80);
            Height = Math.Min(800, SystemParameters.WorkArea.Height - 80);
            MinWidth = 420; MinHeight = 320; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Ui.Surface; FontFamily = Ui.Font; Ui.StyleToolTips(this, false);
            UseLayoutRounding = true; SnapsToDevicePixels = true;
            original = Store.ReadImage(shot.Path);
            canvas = new InkCanvas { Width = original.PixelWidth, Height = original.PixelHeight,
                Background = new ImageBrush(original) { Stretch = Stretch.Fill }, EditingMode = InkCanvasEditingMode.None };
            canvas.DefaultDrawingAttributes = new DrawingAttributes { Color = Colors.OrangeRed,
                Width = Math.Max(3, original.PixelWidth / 240.0), Height = Math.Max(3, original.PixelWidth / 240.0), FitToCurve = true };
            var layout = new DockPanel { Background = Ui.Surface };
            var top = new StackPanel { Background = Ui.Surface };
            var command = new Grid { Margin = new Thickness(12, 8, 12, 8), Height = 36 };
            command.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            command.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            command.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal };
            Button annotate = AddIcon(toolbar, "edit", "展开或收起标注工具", null);
            toolbar.Children.Add(Ui.Divider());
            AddIcon(toolbar, "rotateLeft", "向左旋转 90°", delegate { Rotate(-90); });
            AddIcon(toolbar, "rotateRight", "向右旋转 90°", delegate { Rotate(90); });
            command.Children.Add(toolbar);
            var filename = new TextBlock { Text = System.IO.Path.GetFileName(shot.Path), FontSize = 12, Foreground = Ui.Muted,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(20, 0, 20, 0) };
            Grid.SetColumn(filename, 1); command.Children.Add(filename);
            var actions = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(actions, 2); command.Children.Add(actions);
            if (pinImage != null) AddIcon(actions, "pin", "将当前图片与标注贴到屏幕", delegate { pinImage(RenderEdited()); });
            var save = AddIcon(actions, "save", "保存旋转及标注到应用副本，原图保留", async delegate
            {
                if (saving) return;
                saving = true; IsEnabled = false;
                string previousInfo = info.Text;
                info.Text = "正在保存…";
                try { await store.ReplaceAsync(shot, RenderEdited()); }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is UnauthorizedAccessException)) throw;
                    MessageBox.Show(this, "保存失败，请检查图片是否仍在晾衣绳上，以及文件权限和磁盘空间。", "截图晾衣绳");
                    return;
                }
                finally { saving = false; IsEnabled = true; info.Text = previousInfo; }
                Close();
            });
            Ui.Select(save, true);
            AddIcon(actions, "delete", "删除此图片（仅删除应用副本）", delegate
            {
                try { store.Remove(shot); }
                catch (System.IO.IOException) { MessageBox.Show(this, "删除失败，请检查文件是否被占用或磁盘是否可用。", "截图晾衣绳"); return; }
                catch (UnauthorizedAccessException) { MessageBox.Show(this, "删除失败，请检查文件权限。", "截图晾衣绳"); return; }
                Close();
            });
            var layerButton = AddIcon(actions, "layers", "窗口层级", null);
            var layerMenu = Ui.Menu();
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
            store.LayerChanged += applyLayer; SourceInitialized += delegate { applyLayer(); Native.DarkTitleBar(this); };
            Activated += delegate { if (store.Settings.Layer == WindowLayer.Bottom) applyLayer(); };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { e.Cancel = saving; };
            Closed += delegate { CancelPendingZoom(); store.LayerChanged -= applyLayer; };
            Topmost = store.Settings.Layer == WindowLayer.Top; ShowActivated = store.Settings.Layer != WindowLayer.Bottom;
            layerButton.ToolTip = "窗口层级：" + Native.LayerName(store.Settings.Layer);
            top.Children.Add(command);
            annotations = new AnnotationTools(canvas, original);
            var annotation = annotations.Toolbar;
            annotation.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            var annotationSurface = new Border { Child = annotation, Background = Ui.Raised, BorderBrush = Ui.Edge, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(4), Margin = new Thickness(12, 0, 12, 10), Visibility = annotation.Visibility };
            Action refresh = delegate
            {
                Ui.Select(annotate, annotation.Visibility == Visibility.Visible);
            };
            annotate.Click += delegate
            {
                annotation.Visibility = annotation.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                annotationSurface.Visibility = annotation.Visibility;
                annotations.Select(annotation.Visibility == Visibility.Visible ? AnnotationTool.Pen : AnnotationTool.Hand); refresh();
            };
            top.Children.Add(annotationSurface);
            var topSurface = new Border { Child = top, BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 0, 0, 1) };
            DockPanel.SetDock(topSurface, Dock.Top); layout.Children.Add(topSurface);
            var bottom = new Grid { Background = Ui.Surface, Height = 48 };
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            info = new TextBlock { Foreground = Ui.Muted, FontSize = 11, Margin = new Thickness(18, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = "保存只修改应用副本" };
            bottom.Children.Add(info);
            var zoomTools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 3, 8, 3) };
            Grid.SetColumn(zoomTools, 1); bottom.Children.Add(zoomTools);
            AddIcon(zoomTools, "fit", "适应窗口（Ctrl+0）", FitImage);
            zoomLabel = AddIcon(zoomTools, "", "缩放比例：选择适应窗口或原始大小", null); zoomLabel.Width = 68;
            zoomLabel.Background = Ui.Raised; zoomLabel.FontWeight = FontWeights.Medium;
            var zoomMenu = Ui.Menu();
            var fitItem = new MenuItem { Header = "适应窗口" }; fitItem.Click += delegate { FitImage(); }; zoomMenu.Items.Add(fitItem);
            foreach (double ratio in new[] { .25, .5, 1, 2, 4 })
            {
                double selected = ratio; var item = new MenuItem { Header = (ratio * 100) + "%" }; item.Click += delegate { ChangeZoom(selected); }; zoomMenu.Items.Add(item);
            }
            zoomLabel.Click += delegate { zoomMenu.PlacementTarget = zoomLabel; zoomMenu.IsOpen = true; };
            AddIcon(zoomTools, "minus", "缩小（Ctrl+− / 滚轮）", delegate { ChangeZoom(zoom / 1.25); });
            zoomSlider = new Slider { Minimum = Math.Log(.05), Maximum = Math.Log(8), Width = 140, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0), ToolTip = "缩放", IsMoveToPointEnabled = true };
            Ui.StyleZoomSlider(zoomSlider);
            System.Windows.Automation.AutomationProperties.SetName(zoomSlider, "缩放比例");
            zoomSlider.ValueChanged += delegate { if (!updatingZoom) ChangeZoom(Math.Exp(zoomSlider.Value)); }; zoomTools.Children.Add(zoomSlider);
            AddIcon(zoomTools, "plus", "放大（Ctrl++ / 滚轮）", delegate { ChangeZoom(zoom * 1.25); });
            var bottomSurface = new Border { Child = bottom, BorderBrush = Ui.Edge, BorderThickness = new Thickness(0, 1, 0, 0) };
            DockPanel.SetDock(bottomSurface, Dock.Bottom); layout.Children.Add(bottomSurface);
            layout.SizeChanged += delegate
            {
                filename.Visibility = layout.ActualWidth < 650 ? Visibility.Collapsed : Visibility.Visible;
                zoomSlider.Visibility = layout.ActualWidth < 650 ? Visibility.Collapsed : Visibility.Visible;
            };
            var stage = new Grid { Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            stage.Children.Add(canvas);
            viewer = new ScrollViewer { Background = new RadialGradientBrush(Color.FromRgb(49, 50, 51), Color.FromRgb(37, 38, 40)), HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stage, CanContentScroll = false, PanningMode = PanningMode.Both };
            Ui.StyleScrollBars(viewer);
            layout.Children.Add(viewer); Content = layout;
            viewer.SizeChanged += delegate { if (fit) FitImage(); };
            Loaded += delegate { FitImage(); };
            viewer.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e) { ChangeZoom(zoom * (e.Delta > 0 ? 1.15 : 1 / 1.15)); e.Handled = true; };
            canvas.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (annotations.Tool != AnnotationTool.Hand) return;
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
                if (annotations.IsTextEditing) return;
                if (e.Key == Key.Escape) Close();
                if (annotations.HandleKey(e)) { e.Handled = true; return; }
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                if (e.Key == Key.D0 || e.Key == Key.NumPad0) FitImage();
                else if (e.Key == Key.D1 || e.Key == Key.NumPad1) ChangeZoom(1);
                else if (e.Key == Key.Add || e.Key == Key.OemPlus) ChangeZoom(zoom * 1.25);
                else if (e.Key == Key.Subtract || e.Key == Key.OemMinus) ChangeZoom(zoom / 1.25);
                else return;
                e.Handled = true;
            };
            annotations.Select(editing ? AnnotationTool.Pen : AnnotationTool.Hand); refresh(); UpdateTransform();
        }
        internal void FitImage()
        {
            CancelPendingZoom();
            fit = true;
            double width = rotation % 180 == 0 ? canvas.Width : canvas.Height;
            double height = rotation % 180 == 0 ? canvas.Height : canvas.Width;
            zoom = Math.Max(.0001, Math.Min(1, Math.Min(Math.Max(1, viewer.ActualWidth - 56) / width, Math.Max(1, viewer.ActualHeight - 56) / height)));
            UpdateTransform(); viewer.ScrollToHorizontalOffset(0); viewer.ScrollToVerticalOffset(0);
        }
        internal void ChangeZoom(double value)
        {
            fit = false;
            double next = Math.Max(Math.Min(.05, zoom), Math.Min(8, value));
            if (next == zoom) return;
            if (pendingZoom == null)
            {
                double startingZoom = zoom;
                double x = viewer.HorizontalOffset + viewer.ViewportWidth / 2, y = viewer.VerticalOffset + viewer.ViewportHeight / 2;
                pendingZoom = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
                {
                    pendingZoom = null;
                    double ratio = zoom / startingZoom;
                    viewer.ScrollToHorizontalOffset(x * ratio - viewer.ViewportWidth / 2);
                    viewer.ScrollToVerticalOffset(y * ratio - viewer.ViewportHeight / 2);
                }));
            }
            zoom = next; UpdateTransform();
        }
        private void CancelPendingZoom()
        {
            if (pendingZoom == null) return;
            pendingZoom.Abort(); pendingZoom = null;
        }
        internal void Rotate(int degrees)
        {
            CancelPendingZoom();
            rotation = (rotation + degrees + 360) % 360;
            if (fit) FitImage(); else { UpdateTransform(); viewer.ScrollToHorizontalOffset(0); viewer.ScrollToVerticalOffset(0); }
        }
        private void UpdateTransform()
        {
            var transform = new TransformGroup(); transform.Children.Add(new RotateTransform(rotation)); transform.Children.Add(new ScaleTransform(zoom, zoom));
            canvas.LayoutTransform = transform;
            RenderOptions.SetBitmapScalingMode(canvas, zoom >= 1 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.Linear);
            if (info != null) info.Text = string.Format("PNG   ·   {0} × {1}{2}", original.PixelWidth, original.PixelHeight, rotation == 0 ? "" : "   ·   " + rotation + "°");
            if (zoomLabel != null) zoomLabel.Content = string.Format("{0:0.#}%", zoom * 100);
            if (zoomSlider != null) { updatingZoom = true; zoomSlider.Value = Math.Log(Math.Max(.05, zoom)); updatingZoom = false; }
        }
        internal BitmapSource RenderEdited()
        {
            BitmapSource result = annotations.Render();
            if (rotation == 0) return result;
            var rotated = new TransformedBitmap(result, new RotateTransform(rotation)); rotated.Freeze(); return rotated;
        }
        internal static Button AddIcon(Panel toolbar, string icon, string tip, Action action)
        {
            var button = Ui.IconButton(icon, tip, action); button.Margin = new Thickness(2, 0, 2, 0);
            toolbar.Children.Add(button); return button;
        }
        internal static BitmapSource RenderAnnotation(InkCanvas canvas, Int32Rect? region = null, Brush background = null)
        {
            // Keep export independent of live WPF layout clips, DPI, transforms, opacity and focus.
            var export = new InkCanvas { Width = canvas.Width, Height = canvas.Height, Background = background ?? canvas.Background,
                Strokes = canvas.Strokes.Clone(), ClipToBounds = true, EditingMode = InkCanvasEditingMode.None };
            foreach (UIElement element in canvas.Children)
            {
                FrameworkElement copy;
                var path = element as System.Windows.Shapes.Path;
                var image = element as Image;
                var text = element as TextBlock;
                var rectangle = element as System.Windows.Shapes.Rectangle;
                if (path != null) copy = new System.Windows.Shapes.Path { Data = path.Data, Stroke = path.Stroke, Fill = path.Fill, StrokeThickness = path.StrokeThickness,
                    StrokeStartLineCap = path.StrokeStartLineCap, StrokeEndLineCap = path.StrokeEndLineCap, StrokeLineJoin = path.StrokeLineJoin };
                else if (image != null)
                {
                    copy = new Image { Source = image.Source, Width = image.Width, Height = image.Height, Stretch = image.Stretch };
                    RenderOptions.SetBitmapScalingMode(copy, RenderOptions.GetBitmapScalingMode(image));
                }
                else if (text != null) copy = new TextBlock { Text = text.Text, FontFamily = text.FontFamily, FontSize = text.FontSize, FontWeight = text.FontWeight,
                    FontStyle = text.FontStyle, Foreground = text.Foreground, TextWrapping = text.TextWrapping, MaxWidth = text.MaxWidth };
                else if (rectangle != null) continue; // The mosaic outline only exists during a drag.
                else throw new InvalidOperationException("无法导出该标注类型，请先结束文字编辑。");
                InkCanvas.SetLeft(copy, InkCanvas.GetLeft(element)); InkCanvas.SetTop(copy, InkCanvas.GetTop(element)); export.Children.Add(copy);
            }
            export.Measure(new Size(export.Width, export.Height)); export.Arrange(new Rect(0, 0, export.Width, export.Height)); export.UpdateLayout();
            Int32Rect bounds = region ?? new Int32Rect(0, 0, (int)export.Width, (int)export.Height);
            Visual visual = export;
            if (region.HasValue)
            {
                var positioned = new ContainerVisual { Offset = new Vector(-bounds.X, -bounds.Y) };
                positioned.Children.Add(export);
                visual = positioned;
            }
            var result = new RenderTargetBitmap(bounds.Width, bounds.Height, 96, 96, PixelFormats.Pbgra32);
            result.Render(visual); result.Freeze(); return result;
        }
    }
}
