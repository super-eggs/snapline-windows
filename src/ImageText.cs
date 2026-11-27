using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapline
{
    internal sealed class ImageText : FrameworkElement
    {
        private const string SelectionHint = "在图上拖选文字 · Ctrl+C 复制 · Ctrl+A 全选 · Esc 退出";
        private static readonly Brush SelectionBrush = CreateSelectionBrush();
        private readonly List<Line> lines = new List<Line>();
        private CancellationTokenSource pending;
        private bool stopped;
        private Position anchor, caret;

        internal event Action StatusChanged;
        internal string Status { get; private set; }
        internal bool HasSelection { get { return anchor.Line >= 0 && anchor.CompareTo(caret) != 0; } }

        internal ImageText(int width, int height)
        {
            Width = width; Height = height; ClipToBounds = true; Focusable = true;
            Status = "正在本机识别文字… Esc 取消";
            Cursor = Cursors.Wait;
            anchor = caret = new Position(-1, 0);
            AutomationProperties.SetName(this, "原图文字选区");
        }

        private static Brush CreateSelectionBrush()
        {
            var brush = new SolidColorBrush(Color.FromArgb(95, 34, 124, 248));
            brush.Freeze();
            return brush;
        }

        internal async Task LoadAsync(Recognition engine, BitmapSource image)
        {
            using (var request = new CancellationTokenSource())
            {
                pending = request;
                try
                {
                    var result = await engine.ReadAsync(image, request.Token);
                    if (!stopped) SetLines(result);
                }
                catch (OperationCanceledException) { if (!stopped) throw; }
                catch (Exception ex)
                {
                    if (!(ex is IOException || ex is InvalidDataException || ex is TimeoutException || ex is Win32Exception || ex is SerializationException
                        || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is FormatException)) throw;
                    if (!stopped)
                    {
                        Cursor = Cursors.Arrow;
                        SetStatus(ex is TimeoutException || ex is InvalidDataException ? ex.Message : "提取文字失败，请检查提字组件。");
                    }
                }
                finally { pending = null; }
            }
        }

        internal void Stop()
        {
            stopped = true;
            if (IsMouseCaptured) ReleaseMouseCapture();
            if (pending != null) pending.Cancel();
        }

        internal void SetLines(IEnumerable<RecognitionLine> result)
        {
            lines.Clear();
            foreach (var entry in result)
            {
                Point[] box;
                if (entry == null || string.IsNullOrWhiteSpace(entry.Text) || !TryReadBox(entry.Box, out box)) continue;
                var line = new Line(entry, box);
                if (line.Bounds.Width > 0 && line.Bounds.Height > 0 && line.Axis.LengthSquared > 0) lines.Add(line);
            }
            anchor = caret = new Position(-1, 0);
            Cursor = lines.Count > 0 ? Cursors.IBeam : Cursors.Arrow;
            InvalidateVisual();
            SetStatus(lines.Count > 0 ? SelectionHint : "选区中未识别到文字 · Esc 退出");
        }

        private void SetStatus(string status)
        {
            Status = status;
            if (StatusChanged != null) StatusChanged();
        }

        internal void SelectAll()
        {
            if (lines.Count == 0) return;
            anchor = new Position(0, 0);
            caret = new Position(lines.Count - 1, lines[lines.Count - 1].Count);
            InvalidateVisual();
            if (Status != SelectionHint) SetStatus(SelectionHint);
        }

        internal bool HandleKey(KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return false;
            if (e.Key == Key.A) { SelectAll(); return true; }
            if (e.Key == Key.C) { CopySelection(); return true; }
            return false;
        }

        internal string SelectedText()
        {
            if (!HasSelection) return "";
            Position start, end;
            OrderedRange(out start, out end);
            var text = new StringBuilder();
            for (int i = start.Line; i <= end.Line; i++)
            {
                Line line = lines[i];
                int from = line.Offsets[i == start.Line ? start.Caret : 0];
                int to = line.Offsets[i == end.Line ? end.Caret : line.Count];
                if (i > start.Line) text.AppendLine();
                text.Append(line.Text, from, to - from);
            }
            return text.ToString();
        }

        internal async void CopySelection()
        {
            await CopySelectionAsync(Clipboard.SetText);
        }

        internal async Task CopySelectionAsync(Action<string> publish)
        {
            string selected = SelectedText();
            if (selected.Length == 0)
            {
                if (lines.Count > 0) SetStatus("在图上拖选要复制的文字，或按 Ctrl+A 全选。");
                return;
            }
            // A retry must never change the clipboard after the capture or selection was dismissed.
            bool copied = await ClipboardImages.TryCopyAsync(delegate
            {
                if (!stopped && selected == SelectedText()) publish(selected);
            });
            if (!stopped && selected == SelectedText())
                SetStatus(copied ? "文字已复制 · 可继续拖选 · Esc 退出" : "剪贴板正被占用，请再次复制。");
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            e.Handled = true;
            if (BeginSelection(e.GetPosition(this))) { Focus(); CaptureMouse(); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) ExtendSelection(e.GetPosition(this));
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (IsMouseCaptured) { ExtendSelection(e.GetPosition(this)); ReleaseMouseCapture(); }
            e.Handled = true;
        }

        internal bool BeginSelection(Point point)
        {
            anchor = caret = FindCaret(point, true);
            InvalidateVisual();
            if (lines.Count > 0 && Status != SelectionHint) SetStatus(SelectionHint);
            return anchor.Line >= 0;
        }

        internal void ExtendSelection(Point point)
        {
            if (anchor.Line < 0) return;
            var next = FindCaret(point, false);
            if (next.Line < 0 || next.CompareTo(caret) == 0) return;
            caret = next; InvalidateVisual();
        }

        private Position FindCaret(Point point, bool nearLine)
        {
            int best = -1;
            double vertical = double.PositiveInfinity, horizontal = double.PositiveInfinity;
            for (int i = 0; i < lines.Count; i++)
            {
                Rect bounds = lines[i].Bounds;
                double dx = Math.Max(0, Math.Max(bounds.Left - point.X, point.X - bounds.Right));
                double dy = Math.Max(0, Math.Max(bounds.Top - point.Y, point.Y - bounds.Bottom));
                if (nearLine && (dx > 5 || dy > 5)) continue;
                // Dragging past the end of a short line should still select to that line's end.
                if (dy < vertical || dy == vertical && dx < horizontal)
                {
                    vertical = dy; horizontal = dx; best = i;
                }
            }
            return best < 0 ? new Position(-1, 0) : new Position(best, lines[best].CaretAt(point));
        }

        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            context.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            if (!HasSelection) return;
            Position start, end;
            OrderedRange(out start, out end);
            for (int i = start.Line; i <= end.Line; i++)
            {
                Line line = lines[i];
                int from = i == start.Line ? start.Caret : 0;
                int to = i == end.Line ? end.Caret : line.Count;
                if (from < to) context.DrawGeometry(SelectionBrush, null, line.Highlight(from, to));
            }
        }

        private void OrderedRange(out Position start, out Position end)
        {
            bool forward = anchor.CompareTo(caret) <= 0;
            start = forward ? anchor : caret;
            end = forward ? caret : anchor;
        }

        private static bool TryReadBox(double[][] points, out Point[] box)
        {
            box = null;
            if (points == null || points.Length != 4 || points.Any(p => p == null || p.Length != 2
                || p.Any(value => double.IsNaN(value) || double.IsInfinity(value)))) return false;
            box = points.Select(p => new Point(p[0], p[1])).ToArray();
            return true;
        }

        private struct Position
        {
            internal readonly int Line, Caret;
            internal Position(int line, int caret) { Line = line; Caret = caret; }
            internal int CompareTo(Position other) { return Line == other.Line ? Caret.CompareTo(other.Caret) : Line.CompareTo(other.Line); }
        }

        private sealed class Line
        {
            internal readonly string Text;
            internal readonly int[] Offsets;
            internal readonly Rect Bounds;
            internal readonly Vector Axis;
            private readonly Point[] box;
            private readonly Point origin;
            private readonly double[] stops;
            internal int Count { get { return Offsets.Length - 1; } }

            internal Line(RecognitionLine source, Point[] box)
            {
                this.box = box; Text = source.Text;
                Offsets = StringInfo.ParseCombiningCharacters(Text).Concat(new[] { Text.Length }).ToArray();
                Bounds = new Rect(box[0], box[1]); Bounds.Union(box[2]); Bounds.Union(box[3]);
                origin = Interpolate(box[0], box[3], .5);
                Axis = Interpolate(box[1], box[2], .5) - origin;
                stops = BuildStops(source.Glyphs);
            }

            private double Project(Point p) { return Vector.Multiply(p - origin, Axis) / Axis.LengthSquared; }

            private double[] BuildStops(RecognitionGlyph[] glyphs)
            {
                var values = new double?[Count + 1];
                int searchFrom = 0;
                if (Axis.LengthSquared > 0 && glyphs != null)
                {
                    foreach (RecognitionGlyph glyph in glyphs)
                    {
                        Point[] points;
                        if (glyph == null || string.IsNullOrEmpty(glyph.Text) || !TryReadBox(glyph.Box, out points)) continue;
                        int index = Text.IndexOf(glyph.Text, searchFrom, StringComparison.Ordinal);
                        if (index < 0) continue;
                        int end = index + glyph.Text.Length;
                        int first = Array.BinarySearch(Offsets, index), last = Array.BinarySearch(Offsets, end);
                        if (first < 0 || last <= first) continue;
                        searchFrom = end;
                        double left = points.Min(p => Project(p)), right = points.Max(p => Project(p));
                        for (int i = first; i <= last; i++)
                        {
                            double coordinate = left + (right - left) * (i - first) / (last - first);
                            coordinate = Math.Max(0, Math.Min(1, coordinate));
                            values[i] = values[i].HasValue ? (values[i].Value + coordinate) / 2 : coordinate;
                        }
                    }
                }
                if (!values[0].HasValue) values[0] = 0;
                if (!values[Count].HasValue) values[Count] = 1;
                var result = new double[Count + 1];
                result[0] = values[0].Value;
                int start = 0;
                // Spaces can have no box. Interpolate their stops between neighboring OCR glyphs.
                for (int end = 1; end <= Count; end++)
                {
                    if (!values[end].HasValue) continue;
                    double span = Math.Max(0, values[end].Value - result[start]);
                    for (int i = start + 1; i <= end; i++) result[i] = result[start] + span * (i - start) / (end - start);
                    start = end;
                }
                return result;
            }

            internal int CaretAt(Point point)
            {
                double position = Project(point);
                int after = Array.BinarySearch(stops, position);
                if (after >= 0) return after;
                after = ~after;
                if (after == 0 || after > Count) return Math.Min(after, Count);
                return position - stops[after - 1] < stops[after] - position ? after - 1 : after;
            }

            internal Geometry Highlight(int first, int last)
            {
                double start = stops[first], end = stops[last];
                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                {
                    context.BeginFigure(Interpolate(box[0], box[1], start), true, true);
                    context.LineTo(Interpolate(box[0], box[1], end), true, false);
                    context.LineTo(Interpolate(box[3], box[2], end), true, false);
                    context.LineTo(Interpolate(box[3], box[2], start), true, false);
                }
                geometry.Freeze();
                return geometry;
            }

            private static Point Interpolate(Point start, Point end, double position) { return start + (end - start) * position; }
        }
    }
}
