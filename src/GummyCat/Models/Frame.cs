using System.Collections.Generic;

namespace GummyCat.Models;

public class Frame
{
    public double PrivateMemoryMb { get; set; }

    public IReadOnlyList<SubHeap> SubHeaps { get; set; }

    public int GcNumber { get; set; }
}