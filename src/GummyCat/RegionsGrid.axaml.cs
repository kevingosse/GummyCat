using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using GummyCat.Models;
using Microsoft.Diagnostics.Runtime;

namespace GummyCat
{
    public partial class RegionsGrid : UserControl
    {
        internal const int RegionSize = 20;

        private IReadOnlyList<SubHeap>? _subHeaps;
        private List<(int start, int end, Segment? region, SubHeap? subHeap)> _regions = new();
        private Point _lastPointerPosition;

        public RegionsGrid()
        {
            InitializeComponent();
        }

        protected int RectanglesPerLine => (int)(RenderSurface.Bounds.Width / RegionSize);

        public void SetRegions(IReadOnlyList<SubHeap> subHeaps)
        {
            _subHeaps = subHeaps;
            ComputeRegions();
            InvalidateVisual();

            Hover(_lastPointerPosition);
        }

        private void ComputeRegions()
        {
            _regions.Clear();

            if (_subHeaps == null)
            {
                return;
            }

            ulong lastRegionEnd = 0;
            int index = 0;

            var segments = from subHeap in _subHeaps
                           from segment in subHeap.Segments
                           orderby segment.Start
                           select (segment, subHeap);

            foreach (var (segment, subHeap) in segments)
            {
                if (segment.Flags.HasFlag((ClrSegmentFlags)32))
                {
                    Debug.WriteLine($"Skipping decommitted region - {segment.Address:x2}");
                    continue;
                }

                var generation = segment.Generation;

                var start = segment.Start;
                var end = segment.ReservedMemory.End;

                if (lastRegionEnd != 0)
                {
                    var emptySize = start - lastRegionEnd;

                    if (emptySize > 40)
                    {
                        var emptySizeUnits = (int)ToUnits(emptySize, false);

                        _regions.Add((index, index + emptySizeUnits, null, null));

                        index += emptySizeUnits;
                    }
                }

                var size = end - start;
                var sizeUnits = (int)ToUnits(size);

                _regions.Add((index, index + sizeUnits, segment, subHeap));

                index += sizeUnits;
                lastRegionEnd = end;
            }

            var totalLines = Math.Ceiling((double)index / RectanglesPerLine);
            var linesOnScreen = Math.Ceiling(RenderSurface.Bounds.Height / RegionSize);

            VerticalScrollBar.Maximum = totalLines - linesOnScreen;
            RenderSurface.SetRegions(_regions);
        }

        private static double ToUnits(ulong length, bool roundUp = true)
        {
            var value = length / (1024.0 * 1024);
            return roundUp ? Math.Ceiling(value) : Math.Floor(value);
        }

        private void Hover(Point mousePosition)
        {
            _lastPointerPosition = mousePosition;
            HoverPanel.Children.Clear();

            var offset = (int)VerticalScrollBar.Value;

            // Compute the index of the hovered square
            var line = (int)(mousePosition.Y / RegionSize) + offset;
            var column = (int)(mousePosition.X / RegionSize);

            var index = line * RectanglesPerLine + column;

            foreach (var region in _regions)
            {
                if (region.start > index || region.end <= index)
                {
                    continue;
                }

                if (region.subHeap == null)
                {
                    // For now, for performance reason, don't hover over empty regions
                    return;
                }

                var start = Math.Max(region.start, offset * RectanglesPerLine);

                for (int i = start; i < region.end; i++)
                {
                    var position = new Point((i % RectanglesPerLine) * RegionSize, ((i / RectanglesPerLine) - offset) * RegionSize);

                    if (position.Y < 0)
                    {
                        continue;
                    }

                    if (position.Y > HoverPanel.Bounds.Width)
                    {
                        return;
                    }

                    var borderThickness = 2.0;

                    var thickness = new Thickness(
                        i == region.start ? borderThickness : 0.0,
                        (i - RectanglesPerLine >= region.start ? 0.0 : borderThickness),
                        i == region.end - 1 ? borderThickness : 0.0,
                        (i + RectanglesPerLine < region.end ? 0.0 : borderThickness));

                    var square = new Border { BorderBrush = Brushes.Black, BorderThickness = thickness };

                    square.Width = RegionSize;
                    square.Height = RegionSize;

                    square.SetValue(Canvas.LeftProperty, position.X);
                    square.SetValue(Canvas.TopProperty, position.Y);

                    HoverPanel.Children.Add(square);

                    if (i == region.start && region.region != null)
                    {
                        square.Child = new TextBlock
                        {
                            Text = region.subHeap!.Index.ToString(),
                            FontFamily = new FontFamily("Segoe UI"),
                            FontSize = 11,
                            FontWeight = FontWeight.Bold,
                            Foreground = Brushes.Black,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center
                        };
                    }
                }

                return;
            }
        }

        private void OnPointerExited(object sender, PointerEventArgs e)
        {
            HoverPanel.Children.Clear();
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            Hover(e.GetPosition(this));
        }

        private void VerticalScrollBar_Scroll(object? sender, ScrollEventArgs e)
        {
            RenderSurface.SetOffset((int)e.NewValue);
        }
    }

    public class TilesRenderSurface : Control
    {
        private const int RegionSize = RegionsGrid.RegionSize;
        protected int RectanglesPerLine => (int)(Bounds.Width / RegionSize);

        private List<(int start, int end, Segment? region, SubHeap? subHeap)> _regions = new();

        private int _offset;

        public void SetRegions(List<(int start, int end, Segment? region, SubHeap? subHeap)> regions)
        {
            _regions = regions;
            InvalidateVisual();
        }

        public void SetOffset(int offset)
        {
            if (offset != _offset)
            {
                _offset = offset;
                InvalidateVisual();
            }
        }

        public override void Render(DrawingContext drawingContext)
        {
            var line = 0;
            var column = 0;
            var rectanglesPerLine = RectanglesPerLine;

            var maxLine = Math.Ceiling(Bounds.Height / RegionSize) + _offset;

            void DrawRectangle(Color color, bool first, bool last, int? heapIndex = null)
            {
                // Can we fit another rectangle on this line?
                if (column >= rectanglesPerLine)
                {
                    line++;
                    column = 0;
                }

                if (line >= _offset)
                {
                    var position = new Point(column * RegionSize, (line - _offset) * RegionSize);

                    var lineColor = Colors.White;

                    drawingContext.DrawRectangle(
                        new SolidColorBrush(color),
                        new Pen(new SolidColorBrush(lineColor), 1.0),
                        new Rect(position, new Size(RegionSize, RegionSize)));
                }

                column++;
            }

            foreach (var (start, end, segment, subHeap) in _regions)
            {
                if (subHeap == null)
                {
                    // Empty memory
                    for (int i = start; i < end; i++)
                    {
                        DrawRectangle(Colors.LightGray, i == start, i == end - 1);

                        if (line > maxLine)
                        {
                            return;
                        }
                    }

                    continue;
                }

                var generation = segment!.Generation;

                if (segment.Kind == GCSegmentKind.Ephemeral)
                {
                    var gen1Size = ToUnits(segment.Generation1.Length);
                    var gen2Size = ToUnits(segment.Generation2.Length);
                    var gen0Size = ToUnits(segment.ReservedMemory.End - segment.Start) - gen1Size - gen2Size;

                    for (int i = 0; i < gen2Size; i++)
                    {
                        DrawRectangle(Region.GetColor(Generation.Generation2), i == 0, i == (int)gen2Size - 1, subHeap.Index);

                        if (line > maxLine)
                        {
                            return;
                        }
                    }

                    for (int i = 0; i < gen1Size; i++)
                    {
                        DrawRectangle(Region.GetColor(Generation.Generation1), i == 0, i == (int)gen1Size - 1, subHeap.Index);

                        if (line > maxLine)
                        {
                            return;
                        }
                    }

                    for (int i = 0; i < gen0Size; i++)
                    {
                        DrawRectangle(Region.GetColor(Generation.Generation0), i == 0, i == (int)gen0Size - 1, subHeap.Index);

                        if (line > maxLine)
                        {
                            return;
                        }
                    }
                }
                else
                {
                    var color = Region.GetColor(generation);

                    if (segment.Flags.HasFlag((ClrSegmentFlags)32))
                    {
                        color = Colors.Red;
                    }

                    for (int i = start; i < end; i++)
                    {
                        DrawRectangle(color, i == start, i == end - 1);

                        if (line > maxLine)
                        {
                            return;
                        }
                    }
                }
            }
        }

        private static double ToUnits(ulong length, bool roundUp = true)
        {
            var value = length / (1024.0 * 1024);
            return roundUp ? Math.Ceiling(value) : Math.Floor(value);
        }
    }
}
