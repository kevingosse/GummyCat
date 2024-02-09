using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using GummyCat.Models;
using Microsoft.Diagnostics.Runtime;

namespace GummyCat
{
    public partial class Region : UserControl
    {
        private bool _showReservedMemory;
        private SolidColorBrush _mainColor;

        public Region()
        {
            InitializeComponent();

            _mainColor = (SolidColorBrush)MainRectangle.Fill!;
        }

        public Region(Segment segment, int heap, bool showReservedMemory)
        {
            _showReservedMemory = showReservedMemory;
            Segment = segment;

            InitializeComponent();

            _mainColor = (SolidColorBrush)MainRectangle.Fill!;

            ApplySegment(segment, null, heap);
        }

        public ulong Address => CustomMemoryRange?.Start ?? Segment.Start;

        public bool IsDeleted { get; private set; }

        public MemoryRange? CustomMemoryRange { get; set; }

        public Segment Segment { get; private set; }

        public void Update(Segment newSegment, int heap, bool showReservedMemory)
        {
            _showReservedMemory = showReservedMemory;
            ApplySegment(newSegment, Segment, heap);
            Segment = newSegment;
        }

        public void Delete()
        {
            IsDeleted = true;
            Width = 0;
        }

        private void ApplySegment(Segment segment, Segment? previousSegment, int heap)
        {
            if (segment.Kind == GCSegmentKind.Ephemeral)
            {
                ApplySegmentEphemeral(segment, previousSegment, heap);
                return;
            }

            TextHeap.Text = heap.ToString();

            var size = (long)ToMB(SegmentSize(segment, _showReservedMemory));
            Width = size * 10;
            Height = 40;
            Margin = new Thickness(Width <= 1 ? 0 : 1);

            var generation = segment.Generation;
            var color = GetColor(generation);

            if (segment.Flags.HasFlag((ClrSegmentFlags)32))
            {
                color = Colors.Red;
            }

            _mainColor.Color = color;
            FillRectangle.Width = 1;
            FillRectangle.SetValue(Rectangle.RenderTransformProperty, TransformOperations.Parse($"scaleX({Width * GetFillFactor(segment)})"));
            //FillRectangle.Width = Width * GetFillFactor(segment);
        }

        private void ApplySegmentEphemeral(Segment segment, Segment? previousSegment, int heap)
        {
            if (!Gen0Rectangle.IsVisible)
            {
                Gen0Rectangle.IsVisible = true;
                Gen1Rectangle.IsVisible = true;
                Gen2Rectangle.IsVisible = true;

                Gen0Rectangle.Fill = new SolidColorBrush(GetColor(Generation.Generation0));
                Gen1Rectangle.Fill = new SolidColorBrush(GetColor(Generation.Generation1));
                Gen2Rectangle.Fill = new SolidColorBrush(GetColor(Generation.Generation2));
            }

            TextHeap.Text = heap.ToString();

            var size = SegmentSize(segment, _showReservedMemory);
            var sizeInMb = (long)ToMB(size);
            Width = sizeInMb * 10;
            Height = 40;

            Margin = new Thickness(1);

            if (previousSegment == null)
            {
                _mainColor.Color = Colors.LightGray;
            }

            Gen2Rectangle.Width = ((double)segment.Generation2.Length / size) * Width;

            Gen1Rectangle.Width = ((double)segment.Generation1.Length / size) * Width;
            Gen1Rectangle.Margin = new Thickness(Gen2Rectangle.Width, 0, 0, 0);

            Gen0Rectangle.Width = ((double)segment.Generation0.Length / size) * Width;
            Gen0Rectangle.Margin = new Thickness(Gen2Rectangle.Width + Gen1Rectangle.Width, 0, 0, 0);
        }

        private double GetFillFactor(Segment segment)
        {
            return (double)segment.ObjectRange.Length / SegmentSize(segment, _showReservedMemory);
        }

        public static ulong SegmentSize(Segment segment, bool showReservedMemory)
        {
            if (showReservedMemory)
            {
                return segment.ReservedMemory.Length + segment.CommittedMemory.Length;
            }

            return segment.CommittedMemory.Length;
        }

        public static double ToMB(ulong length)
        {
            return Math.Round(length / (1024.0 * 1024), 2);
        }

        public static Color GetColor(Generation generation)
        {
            return generation switch
            {
                Generation.Generation0 => Colors.PowderBlue,
                Generation.Generation1 => Colors.SkyBlue,
                Generation.Generation2 => Colors.CornflowerBlue,
                Generation.Large => Colors.Orange,
                Generation.Pinned => Colors.Pink,
                Generation.Frozen => Colors.Gray,
                Generation.Unknown => Colors.Red,
            };
        }
    }
}
