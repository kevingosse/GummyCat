using System.Collections.Generic;

namespace GummyCat.Models;

public class Session
{
    public IReadOnlyList<Frame> Frames { get; set; }

    public IReadOnlyList<Gc> GCs { get; set; }
}