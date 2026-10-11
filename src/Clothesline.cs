using System;
using System.Windows;
using System.Windows.Media;

namespace Snapline
{
    internal sealed class Clothesline : FrameworkElement
    {
        internal static double DropAt(double fraction, double width)
        {
            double t = Math.Max(0, Math.Min(1, fraction));
            return 4 * Math.Min(32, width * .055) * t * (1 - t);
        }
        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            double width = ActualWidth;
            if (width <= 0) return;
            double sag = DropAt(.5, width);
            var rope = new StreamGeometry();
            using (var path = rope.Open())
            {
                path.BeginFigure(new Point(0, 12), false, false);
                path.QuadraticBezierTo(new Point(width / 2, 12 + 2 * sag), new Point(width, 12), true, false);
            }
            rope.Freeze();
            var pen = new Pen(Ui.Brush(194, 175, 143), 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            drawing.DrawGeometry(null, pen, rope);
        }
    }
}
