using System;
using System.Windows;
using System.Windows.Media;

namespace Snapline
{
    internal sealed class CaptureMask : FrameworkElement
    {
        private const int TileSize = 64;
        private readonly VisualCollection tiles;
        private readonly Brush dim = new SolidColorBrush(Color.FromArgb(130, 0, 0, 0));

        internal CaptureMask(int width, int height)
        {
            tiles = new VisualCollection(this);
            IsHitTestVisible = false;
            dim.Freeze();
            for (int y = 0; y < height; y += TileSize)
                for (int x = 0; x < width; x += TileSize)
                {
                    var bounds = new Rect(x, y, Math.Min(TileSize, width - x), Math.Min(TileSize, height - y));
                    tiles.Add(new Tile(bounds, dim));
                }
        }

        protected override int VisualChildrenCount { get { return tiles.Count; } }
        protected override Visual GetVisualChild(int index) { return tiles[index]; }

        internal void Reveal(Rect selection)
        {
            // Separate visuals keep WPF from repainting the full desktop when an edge moves.
            foreach (Tile tile in tiles) tile.Reveal(selection, dim);
        }

        private sealed class Tile : DrawingVisual
        {
            private readonly Rect bounds;
            private Rect opening = Rect.Empty;

            internal Tile(Rect bounds, Brush dim)
            {
                this.bounds = bounds;
                using (var drawing = RenderOpen()) drawing.DrawRectangle(dim, null, bounds);
            }

            internal void Reveal(Rect selection, Brush dim)
            {
                var area = Rect.Intersect(bounds, selection);
                if (area.Width == 0 || area.Height == 0) area = Rect.Empty;
                if (area == opening) return;
                opening = area;
                using (var drawing = RenderOpen())
                {
                    if (area.IsEmpty) drawing.DrawRectangle(dim, null, bounds);
                    else
                    {
                        DrawStrip(drawing, dim, bounds.Left, bounds.Top, bounds.Width, area.Top - bounds.Top);
                        DrawStrip(drawing, dim, bounds.Left, area.Bottom, bounds.Width, bounds.Bottom - area.Bottom);
                        DrawStrip(drawing, dim, bounds.Left, area.Top, area.Left - bounds.Left, area.Height);
                        DrawStrip(drawing, dim, area.Right, area.Top, bounds.Right - area.Right, area.Height);
                    }
                }
            }

            private static void DrawStrip(DrawingContext drawing, Brush dim, double x, double y, double width, double height)
            {
                if (width > 0 && height > 0) drawing.DrawRectangle(dim, null, new Rect(x, y, width, height));
            }
        }
    }
}
