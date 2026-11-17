using System;
using System.Windows;
using System.Windows.Media;

namespace Snapline
{
    internal sealed class Clothespin : FrameworkElement
    {
        internal static readonly Point GripPoint = new Point(9, 28);
        private static readonly DrawingGroup Artwork = CreateArtwork();

        internal Clothespin()
        {
            Width = 18; Height = 32; IsHitTestVisible = false;
            HorizontalAlignment = HorizontalAlignment.Center;
            Effect = Ui.Shadow(1, 3, .22);
        }
        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing); drawing.DrawDrawing(Artwork);
        }
        private static DrawingGroup CreateArtwork()
        {
            var artwork = new DrawingGroup();
            using (var drawing = artwork.Open())
            {
                // Keep the tilt anchored at the closed jaws, where rope and paper meet.
                drawing.PushTransform(new RotateTransform(-4, GripPoint.X, GripPoint.Y));
                var back = Geometry.Parse("M10.3,1.1 Q11.7,.4 13.1,1.4 L14,12.3 Q14.2,14.9 13.3,17.1 L11.3,29.8 Q11.1,31.4 10,31.4 L8.9,31.4 L8.7,26.1 L9.8,17.2 L8.8,13.8 Z");
                var backWood = new LinearGradientBrush(Color.FromRgb(197, 151, 94), Color.FromRgb(234, 201, 149), new Point(0, 0), new Point(1, 0));
                drawing.DrawGeometry(backWood, new Pen(Ui.Brush(151, 111, 65), .65), back);
                drawing.DrawGeometry(null, new Pen(Ui.Brush(244, 219, 174), .55), Geometry.Parse("M11.3,1.5 L12.3,12.4 M12.3,19.1 L10.7,29.8"));

                // A darker side face and a bevel give the front arm real thickness.
                drawing.DrawGeometry(Ui.Brush(174, 126, 73), new Pen(Ui.Brush(147, 106, 61), .6),
                    Geometry.Parse("M8.8,1 L10.2,2.1 L9.1,12.4 L10.2,15.8 L9.2,18.9 L10.2,30.5 L9.1,31.6 L7.4,17.6 Z"));
                var front = Geometry.Parse("M3.7,1.1 Q3.6,.5 4.5,.5 L8.7,.5 Q9.5,.5 9.3,1.5 L8.1,12.1 L9.1,15.3 L8.3,18.4 L9.5,30.3 Q9.6,31.3 8.7,31.4 L4.8,31.4 Q3.7,31.4 3.8,30.3 L4.4,18.4 L3.6,15.2 L4.5,12.2 Z");
                var wood = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                wood.GradientStops.Add(new GradientStop(Color.FromRgb(250, 226, 183), 0));
                wood.GradientStops.Add(new GradientStop(Color.FromRgb(232, 196, 141), .35));
                wood.GradientStops.Add(new GradientStop(Color.FromRgb(215, 171, 112), .84));
                wood.GradientStops.Add(new GradientStop(Color.FromRgb(195, 148, 89), 1));
                drawing.DrawGeometry(wood, new Pen(Ui.Brush(169, 125, 75), .65), front);
                drawing.DrawGeometry(null, new Pen(Ui.Brush(255, 237, 203), .65), Geometry.Parse("M4.3,2 L4.9,12.2 L4,15.2 M4.9,19.2 L4.5,29.9 L8.8,29.9"));
                var grain = new Pen(new SolidColorBrush(Color.FromArgb(65, 143, 97, 49)), .45);
                drawing.DrawGeometry(null, grain, Geometry.Parse("M6,2.6 C5.4,5.9 7.1,8.2 6.4,11.1 M7.6,3.8 C7.1,7 8,8.9 7.3,11.6 M6,20 C5.5,23.1 7.5,25.6 6.4,29 M7.8,21.6 C7.2,24 8.6,26.4 8,28.8"));
                drawing.DrawGeometry(null, new Pen(Ui.Brush(182, 138, 84), .5), Geometry.Parse("M4.5,27.8 L9.1,27.8"));
                drawing.DrawGeometry(null, new Pen(Ui.Brush(244, 212, 162), .5), Geometry.Parse("M4.5,28.5 L9.1,28.5"));

                var wire = new Pen(Ui.Brush(92, 96, 91), .85) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                drawing.DrawGeometry(null, wire, Geometry.Parse("M5,15.3 L4.1,20.5 Q4.1,21.4 5.2,21.3 M11.8,15 L13.5,10.5 Q13.7,9.6 12.7,9.7"));
                drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(55, 74, 54, 33)), null, new Rect(3.9, 13.9, 9.7, 4.5), 1.5, 1.5);
                var metal = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
                metal.GradientStops.Add(new GradientStop(Color.FromRgb(224, 225, 215), 0));
                metal.GradientStops.Add(new GradientStop(Color.FromRgb(149, 155, 147), .4));
                metal.GradientStops.Add(new GradientStop(Color.FromRgb(201, 205, 194), .65));
                metal.GradientStops.Add(new GradientStop(Color.FromRgb(101, 110, 102), 1));
                drawing.DrawRoundedRectangle(metal, new Pen(Ui.Brush(100, 104, 96), .5), new Rect(4.1, 13.2, 9.3, 4.1), 1.5, 1.5);
                for (double x = 5.6; x < 13; x += 1.7)
                {
                    drawing.DrawLine(new Pen(Ui.Brush(111, 117, 108), .6), new Point(x, 13.8), new Point(x + .35, 16.7));
                    drawing.DrawLine(new Pen(Ui.Brush(228, 230, 218), .45), new Point(x + .7, 13.7), new Point(x + 1.05, 16.3));
                }
                drawing.Pop();
            }
            artwork.Freeze(); return artwork;
        }
    }

    internal sealed class Clothesline : FrameworkElement
    {
        internal static readonly double RopeY = Clothespin.GripPoint.Y;
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
                path.BeginFigure(new Point(0, RopeY), false, false);
                path.QuadraticBezierTo(new Point(width / 2, RopeY + 2 * sag), new Point(width, RopeY), true, false);
            }
            rope.Freeze();
            drawing.PushTransform(new TranslateTransform(0, 1.2));
            drawing.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(32, 63, 51, 32)), 4.2), rope);
            drawing.Pop();
            drawing.DrawGeometry(null, new Pen(Ui.Brush(153, 131, 99), 3.4), rope);
            drawing.DrawGeometry(null, new Pen(Ui.Brush(201, 178, 140), 2.8), rope);
            drawing.PushTransform(new TranslateTransform(0, -.65));
            drawing.DrawGeometry(null, new Pen(Ui.Brush(236, 220, 188), 1), rope);
            drawing.Pop();

            // Diagonal grooves follow the local tangent, so the twist stays continuous along the sag.
            drawing.PushClip(rope.GetWidenedPathGeometry(new Pen(Brushes.Black, 3.1)));
            var grooves = new StreamGeometry();
            var strands = new StreamGeometry();
            using (var groove = grooves.Open())
            using (var strand = strands.Open())
            {
                for (double x = -7; x < width + 7; x += 6.5)
                {
                    var center = new Point(x, RopeY + DropAt(x / width, width));
                    var tangent = new Vector(1, 4 * sag / width * (1 - 2 * x / width)); tangent.Normalize();
                    var normal = new Vector(-tangent.Y, tangent.X);
                    Point start = center - tangent * 2.1 - normal * 1.7;
                    Point control = center - tangent * .3;
                    Point end = center + tangent * 2.1 + normal * 1.7;
                    groove.BeginFigure(start, false, false); groove.QuadraticBezierTo(control, end, true, false);
                    Vector highlight = tangent * .9;
                    strand.BeginFigure(start + highlight, false, false); strand.QuadraticBezierTo(control + highlight, end + highlight, true, false);
                }
            }
            grooves.Freeze(); strands.Freeze();
            drawing.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(120, 127, 105, 74)), .55), grooves);
            drawing.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(155, 249, 234, 203)), .55), strands);
            drawing.Pop();
            var fiber = new Pen(new SolidColorBrush(Color.FromArgb(65, 224, 206, 171)), .4);
            for (double x = 11; x < width; x += 29)
            {
                double y = RopeY + DropAt(x / width, width);
                drawing.DrawLine(fiber, new Point(x - .8, y - 1.2), new Point(x + .5, y - 2.2));
            }
        }
    }
}
