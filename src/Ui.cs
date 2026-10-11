using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Snapline
{
    internal static class Ui
    {
        internal static readonly Brush Surface = Brush(31, 39, 49);
        internal static readonly Brush Raised = Brush(44, 55, 68);
        internal static readonly Brush Edge = Brush(65, 78, 92);
        internal static readonly Brush Text = Brush(240, 243, 247);
        internal static readonly Brush Muted = Brush(169, 183, 197);
        internal static readonly Brush Accent = Brush(216, 184, 134);
        internal static readonly Brush LightSurface = Brush(249, 248, 245);
        internal static readonly Brush LightText = Brush(49, 56, 64);
        internal static readonly Brush LightMuted = Brush(103, 111, 121);
        internal static readonly Brush LightEdge = Brush(222, 222, 216);
        internal static SolidColorBrush Brush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b)); brush.Freeze(); return brush;
        }
        internal static Button Button(string text, string tip, Action action, bool light = false)
        {
            var button = new Button
            {
                Content = text, ToolTip = tip, FontSize = 12, FontFamily = new FontFamily("Microsoft YaHei UI"),
                Foreground = light ? LightText : Text, Background = light ? LightSurface : Raised, BorderBrush = light ? LightEdge : Edge,
                BorderThickness = new Thickness(1), Padding = new Thickness(12, 5, 12, 5),
                MinHeight = 32, MinWidth = 40, Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center
            };
            AutomationProperties.SetName(button, tip);
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border), "Surface");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
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
            hover.Setters.Add(new Setter(Border.BackgroundProperty, light ? Brush(237, 235, 229) : Brush(61, 74, 89), "Surface")); template.Triggers.Add(hover);
            var pressed = new Trigger { Property = System.Windows.Controls.Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, light ? Brush(223, 219, 210) : Brush(78, 91, 105), "Surface")); template.Triggers.Add(pressed);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "Surface")); template.Triggers.Add(focus);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .4, "Surface")); template.Triggers.Add(disabled);
            button.Template = template;
            if (action != null) button.Click += delegate { action(); };
            return button;
        }
    }
}
