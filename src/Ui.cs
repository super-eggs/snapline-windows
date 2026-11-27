using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Snapline
{
    internal static class Ui
    {
        internal static readonly FontFamily Font = new FontFamily("Segoe UI, Microsoft YaHei UI");
        internal static readonly Brush Surface = Brush(34, 35, 37);
        internal static readonly Brush Raised = Brush(47, 48, 50);
        internal static readonly Brush Edge = Brush(65, 66, 67);
        internal static readonly Brush Text = Brush(239, 237, 232);
        internal static readonly Brush Muted = Brush(166, 166, 162);
        internal static readonly Brush Accent = Brush(216, 184, 134);
        internal static readonly Brush Selected = Brush(69, 62, 51);
        internal static readonly Brush LightSelected = Brush(237, 226, 205);
        internal static readonly Brush LightSurface = Brush(251, 250, 247);
        internal static readonly Brush LightText = Brush(61, 63, 62);
        internal static readonly Brush LightMuted = Brush(117, 117, 111);
        internal static readonly Brush LightEdge = Brush(224, 222, 215);
        internal static SolidColorBrush Brush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b)); brush.Freeze(); return brush;
        }
        internal static Button Button(string text, string tip, Action action, bool light = false)
        {
            var button = new Button
            {
                Content = text, ToolTip = tip, FontSize = 12, FontFamily = Font,
                Foreground = light ? LightText : Text, Background = light ? LightSurface : Raised, BorderBrush = light ? LightEdge : Edge,
                BorderThickness = new Thickness(1), Padding = new Thickness(12, 5, 12, 5), FocusVisualStyle = null,
                MinHeight = 32, MinWidth = 40, Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetInitialShowDelay(button, 550);
            ToolTipService.SetShowDuration(button, 5000);
            AutomationProperties.SetName(button, tip);
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border), "Surface");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter); template.VisualTree = border;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, light ? Brush(238, 236, 230) : Brush(59, 60, 61), "Surface")); template.Triggers.Add(hover);
            var pressed = new Trigger { Property = System.Windows.Controls.Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, light ? Brush(226, 220, 209) : Brush(75, 70, 61), "Surface")); template.Triggers.Add(pressed);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "Surface")); template.Triggers.Add(focus);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .4, "Surface")); template.Triggers.Add(disabled);
            button.Template = template;
            if (action != null) button.Click += delegate { action(); };
            return button;
        }
        internal static DropShadowEffect Shadow(double depth, double blur, double opacity)
        {
            return new DropShadowEffect { Color = Color.FromRgb(25, 28, 28), Direction = 270, ShadowDepth = depth, BlurRadius = blur, Opacity = opacity };
        }
        internal static Button IconButton(string icon, string tip, Action action, bool light = false)
        {
            var button = Button("", tip, action, light);
            button.Width = 36; button.Height = 34; button.MinWidth = 36; button.Padding = new Thickness(6);
            button.Background = button.BorderBrush = Brushes.Transparent; button.Tag = icon;
            if (icon != "") button.Content = Icon(icon, button);
            return button;
        }
        internal static Button CompactIconButton(string icon, string tip, Action action)
        {
            var button = IconButton(icon, tip, action);
            button.Width = button.MinWidth = button.Height = button.MinHeight = 32;
            button.Margin = new Thickness(0); button.Padding = new Thickness(6);
            var drawing = (System.Windows.Shapes.Path)button.Content; drawing.Width = drawing.Height = 16;
            return button;
        }
        internal static Border CompactDivider()
        {
            var divider = Divider(); divider.Margin = new Thickness(4, 0, 4, 0); return divider;
        }
        private static System.Windows.Shapes.Path Icon(string icon, Button button)
        {
            string geometry;
            switch (icon)
            {
                case "edit": case "pen": geometry = "M3,15 L3,18 L6,18 L17,7 L13,3 Z M11,5 L15,9"; break;
                case "rotateLeft": geometry = "M3,8 A7,7 0 1 1 4,15 M3,3 L3,8 L8,8"; break;
                case "rotateRight": geometry = "M17,8 A7,7 0 1 0 16,15 M17,3 L17,8 L12,8"; break;
                case "save": geometry = "M3,2 L15,2 L18,5 L18,18 L2,18 L2,2 Z M6,2 L6,7 L14,7 L14,2 M6,18 L6,11 L14,11 L14,18"; break;
                case "delete": geometry = "M3,5 L17,5 M7,5 L7,2 L13,2 L13,5 M5,5 L6,18 L14,18 L15,5 M8,8 L8,15 M12,8 L12,15"; break;
                case "layers": geometry = "M2,6 L10,2 L18,6 L10,10 Z M2,10 L10,14 L18,10 M2,14 L10,18 L18,14"; break;
                case "hand": geometry = "M6,11 L6,5 Q6,2 8,5 L8,10 L8,3 Q9,1 10,3 L10,10 L10,4 Q12,2 12,4 L12,10 L12,6 Q14,4 14,6 L14,13 Q14,18 10,18 L7,18 L2,12 Q2,9 5,12 Z"; break;
                case "eraser": geometry = "M2,12 L11,3 L18,10 L10,18 L8,18 Z M7,7 L14,14 M8,18 L19,18"; break;
                case "undo": geometry = "M3,9 L3,3 M3,9 L9,9 M3,9 Q8,1 14,6 Q20,12 14,17"; break;
                case "redo": geometry = "M17,9 L17,3 M17,9 L11,9 M17,9 Q12,1 6,6 Q0,12 6,17"; break;
                case "rectangle": geometry = "M2,4 L18,4 L18,16 L2,16 Z"; break;
                case "ellipse": geometry = "M18,10 A8,6 0 1 1 2,10 A8,6 0 1 1 18,10"; break;
                case "line": geometry = "M3,17 L17,3"; break;
                case "arrow": geometry = "M3,17 L17,3 M7,3 L17,3 L17,13"; break;
                case "text": geometry = "M3,4 L17,4 M10,4 L10,18 M6,18 L14,18"; break;
                case "ocr": geometry = "M2,6 L2,2 L6,2 M14,2 L18,2 L18,6 M18,14 L18,18 L14,18 M6,18 L2,18 L2,14 M5,6 L15,6 M10,6 L10,15"; break;
                case "ocrCopy": geometry = "M7,6 L17,6 L17,18 L7,18 Z M13,6 L13,2 L3,2 L3,14 L7,14"; break;
                case "mosaic": geometry = "M2,2 L18,2 L18,18 L2,18 Z M2,7 L18,7 M2,12 L18,12 M7,2 L7,18 M12,2 L12,18"; break;
                case "fit": geometry = "M7,3 L3,3 L3,7 M13,3 L17,3 L17,7 M3,13 L3,17 L7,17 M13,17 L17,17 L17,13"; break;
                case "minus": geometry = "M4,10 L16,10"; break;
                case "plus": case "import": geometry = "M4,10 L16,10 M10,4 L10,16"; break;
                case "pin": geometry = "M7,2 L14,2 M8,2 L8,8 L5,12 L15,12 L12,8 L12,2 M10,12 L10,19"; break;
                case "collapse": geometry = "M5,12 L10,7 L15,12"; break;
                case "left": geometry = "M12,4 L6,10 L12,16"; break;
                case "right": geometry = "M7,4 L13,10 L7,16"; break;
                case "picker": geometry = "M12,2 L18,8 M14,1 L19,6 L16,9 L11,4 Z M12,5 L3,14 L2,18 L6,17 L15,8 M4,13 L7,16"; break;
                case "reselect": geometry = "M2,7 L2,2 L7,2 M13,2 L18,2 L18,7 M18,13 L18,18 L13,18 M7,18 L2,18 L2,13 M7,10 L13,10 M10,7 L10,13"; break;
                case "width": geometry = "M3,3 L17,3 L17,4 L3,4 Z M3,8 L17,8 L17,10 L3,10 Z M3,14 L17,14 L17,18 L3,18 Z"; break;
                case "close": geometry = "M5,5 L15,15 M15,5 L5,15"; break;
                case "check": geometry = "M3,10 L8,15 L17,5"; break;
                default: geometry = "M10,4 A6,6 0 1 1 9.99,4 Z"; break;
            }
            var path = new System.Windows.Shapes.Path { Data = Geometry.Parse(geometry), Width = 18, Height = 18,
                Stretch = Stretch.Uniform, StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
            path.SetBinding(System.Windows.Shapes.Path.StrokeProperty, new Binding("Foreground") { Source = button });
            if (icon == "color") path.SetBinding(System.Windows.Shapes.Path.FillProperty, new Binding("Foreground") { Source = button });
            if (icon == "width") { path.SetBinding(System.Windows.Shapes.Path.FillProperty, new Binding("Foreground") { Source = button }); path.Stroke = null; }
            return path;
        }
        internal static void Select(Button button, bool selected, bool light = false)
        {
            button.Background = selected ? (light ? LightSelected : Selected) : Brushes.Transparent;
            button.Foreground = selected ? (light ? LightText : Accent) : (light ? LightText : Text);
            button.BorderBrush = selected ? (light ? Brush(206, 187, 151) : Brush(105, 91, 69)) : Brushes.Transparent;
        }
        internal static Border Divider(bool light = false)
        {
            return new Border { Width = 1, Height = 16, Background = light ? LightEdge : Edge, Margin = new Thickness(7, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        }
        internal static void StyleToolTips(FrameworkElement element, bool light)
        {
            var style = new Style(typeof(ToolTip));
            style.Setters.Add(new Setter(Control.BackgroundProperty, light ? LightSurface : Raised));
            style.Setters.Add(new Setter(Control.ForegroundProperty, light ? LightText : Text));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, light ? LightEdge : Edge));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, Font));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 7, 10, 7)));
            element.Resources.Add(typeof(ToolTip), style);
        }
        internal static ContextMenu Menu(bool light = false)
        {
            var menu = new ContextMenu { Background = light ? LightSurface : Raised, Foreground = light ? LightText : Text,
                BorderBrush = light ? LightEdge : Edge, BorderThickness = new Thickness(1), Padding = new Thickness(5), FontFamily = Font, FontSize = 12 };
            var template = new ControlTemplate(typeof(ContextMenu));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            border.SetValue(Border.PaddingProperty, new Thickness(5));
            var items = new FrameworkElementFactory(typeof(StackPanel)); items.SetValue(Panel.IsItemsHostProperty, true);
            border.AppendChild(items); template.VisualTree = border; menu.Template = template;
            var itemStyle = new Style(typeof(MenuItem));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 7, 14, 7)));
            itemStyle.Setters.Add(new Setter(Control.MinHeightProperty, 32.0));
            var itemTemplate = new ControlTemplate(typeof(MenuItem));
            var itemBorder = new FrameworkElementFactory(typeof(Border), "Surface");
            itemBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            itemBorder.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            itemBorder.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var row = new FrameworkElementFactory(typeof(DockPanel));
            var check = new FrameworkElementFactory(typeof(TextBlock), "Check");
            check.SetValue(TextBlock.TextProperty, "✓"); check.SetValue(FrameworkElement.WidthProperty, 22.0);
            check.SetValue(UIElement.OpacityProperty, 0.0); check.SetValue(TextBlock.ForegroundProperty, light ? LightText : Accent);
            row.AppendChild(check);
            var header = new FrameworkElementFactory(typeof(ContentPresenter)); header.SetValue(ContentPresenter.ContentSourceProperty, "Header");
            row.AppendChild(header); itemBorder.AppendChild(row); itemTemplate.VisualTree = itemBorder;
            var highlight = new Trigger { Property = MenuItem.IsHighlightedProperty, Value = true };
            highlight.Setters.Add(new Setter(Border.BackgroundProperty, light ? LightSelected : Selected, "Surface")); itemTemplate.Triggers.Add(highlight);
            var isChecked = new Trigger { Property = MenuItem.IsCheckedProperty, Value = true };
            isChecked.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0, "Check")); itemTemplate.Triggers.Add(isChecked);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .4, "Surface")); itemTemplate.Triggers.Add(disabled);
            itemStyle.Setters.Add(new Setter(Control.TemplateProperty, itemTemplate)); menu.Resources.Add(typeof(MenuItem), itemStyle);
            return menu;
        }
        internal static void StyleZoomSlider(Slider slider)
        {
            slider.Background = Edge; slider.Foreground = Accent; slider.BorderBrush = Text;
            slider.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
                <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Slider'>
                    <Grid Height='32'>
                        <Border Height='3' CornerRadius='1.5' Background='{TemplateBinding Background}' VerticalAlignment='Center'/>
                        <Track x:Name='PART_Track' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
                               Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}'
                               Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
                            <Track.Thumb>
                                <Thumb Width='12' Height='12' Background='{TemplateBinding Foreground}' BorderBrush='{TemplateBinding BorderBrush}' Cursor='Hand' Focusable='False'>
                                    <Thumb.Template>
                                        <ControlTemplate TargetType='Thumb'>
                                            <Border CornerRadius='6' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'/>
                                        </ControlTemplate>
                                    </Thumb.Template>
                                </Thumb>
                            </Track.Thumb>
                        </Track>
                    </Grid>
                    <ControlTemplate.Triggers>
                        <Trigger Property='IsKeyboardFocused' Value='True'>
                            <Setter Property='Background' Value='#BCA786'/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>");
        }
        internal static void StyleScrollBars(FrameworkElement element)
        {
            element.Resources[SystemColors.ControlBrushKey] = Surface;
            element.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), System.Windows.Markup.XamlReader.Parse(@"
                <Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'>
                    <Setter Property='Width' Value='12'/>
                    <Setter Property='Background' Value='#252628'/>
                    <Setter Property='Template'>
                        <Setter.Value>
                            <ControlTemplate TargetType='ScrollBar'>
                                <Grid Background='{TemplateBinding Background}'>
                                    <Track x:Name='PART_Track' IsDirectionReversed='True' Orientation='{TemplateBinding Orientation}'
                                           Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' ViewportSize='{TemplateBinding ViewportSize}'
                                           Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
                                        <Track.DecreaseRepeatButton>
                                            <RepeatButton x:Name='Decrease' Command='{x:Static ScrollBar.PageUpCommand}' Focusable='False'>
                                                <RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template>
                                            </RepeatButton>
                                        </Track.DecreaseRepeatButton>
                                        <Track.Thumb>
                                            <Thumb x:Name='ScrollThumb' MinHeight='24' Background='#666765'>
                                                <Thumb.Template>
                                                    <ControlTemplate TargetType='Thumb'>
                                                        <Border x:Name='Handle' Background='{TemplateBinding Background}' CornerRadius='3' Margin='3'/>
                                                        <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Handle' Property='Background' Value='#BCA786'/></Trigger></ControlTemplate.Triggers>
                                                    </ControlTemplate>
                                                </Thumb.Template>
                                            </Thumb>
                                        </Track.Thumb>
                                        <Track.IncreaseRepeatButton>
                                            <RepeatButton x:Name='Increase' Command='{x:Static ScrollBar.PageDownCommand}' Focusable='False'>
                                                <RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template>
                                            </RepeatButton>
                                        </Track.IncreaseRepeatButton>
                                    </Track>
                                </Grid>
                                <ControlTemplate.Triggers>
                                    <Trigger Property='Orientation' Value='Horizontal'>
                                        <Setter TargetName='PART_Track' Property='IsDirectionReversed' Value='False'/>
                                        <Setter TargetName='ScrollThumb' Property='MinHeight' Value='0'/>
                                        <Setter TargetName='ScrollThumb' Property='MinWidth' Value='24'/>
                                        <Setter TargetName='Decrease' Property='Command' Value='{x:Static ScrollBar.PageLeftCommand}'/>
                                        <Setter TargetName='Increase' Property='Command' Value='{x:Static ScrollBar.PageRightCommand}'/>
                                    </Trigger>
                                </ControlTemplate.Triggers>
                            </ControlTemplate>
                        </Setter.Value>
                    </Setter>
                    <Style.Triggers><Trigger Property='Orientation' Value='Horizontal'><Setter Property='Width' Value='Auto'/><Setter Property='Height' Value='12'/></Trigger></Style.Triggers>
                </Style>"));
        }
    }
}
