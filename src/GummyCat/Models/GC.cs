using Microsoft.Diagnostics.Tracing.Analysis.GC;

namespace GummyCat.Models;

public class Gc
{
    public Gc()
    {

    }

    public Gc(TraceGC gc)
    {
        Number = gc.Number;
        Reason = gc.Reason.ToString();
        Type = gc.Type.ToString();
        Generation = gc.Generation.ToString();
    }

    public int Number { get; set; }

    public string Reason { get; set; }

    public string Type { get; set; }

    public string Generation { get; set; }
}