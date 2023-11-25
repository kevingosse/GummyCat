using Microsoft.Diagnostics.Runtime;

namespace GummyCat.Models;

public class Segment
{
    public Segment()
    {

    }

    public Segment(ClrSegment segment)
    {
        Start = segment.Start;
        if (segment.Kind != GCSegmentKind.Ephemeral)
        {
            Start -= 40; // all regions have a "header" of 40 bytes (plug)
        }
        Flags = segment.Flags;
        Address = segment.Address;
        Generation = segment.GetGeneration(segment.Start);
        Kind = segment.Kind;
        ReservedMemory = segment.ReservedMemory;
        CommittedMemory = segment.CommittedMemory;
        ObjectRange = segment.ObjectRange;
        Generation0 = segment.Generation0;
        Generation1 = segment.Generation1;
        Generation2 = segment.Generation2;
    }

    public ulong Start { get; set; }

    public ulong Address { get; set; }

    public ClrSegmentFlags Flags { get; set; }

    public Generation Generation { get; set; }

    public GCSegmentKind Kind { get; set; }

    public MemoryRange ReservedMemory { get; set; }

    public MemoryRange CommittedMemory { get; set; }

    public MemoryRange ObjectRange { get; set; }

    public MemoryRange Generation0 { get; set; }

    public MemoryRange Generation1 { get; set; }

    public MemoryRange Generation2 { get; set; }
}