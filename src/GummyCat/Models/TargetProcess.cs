using System.Diagnostics;

namespace GummyCat.Models;

public class TargetProcess
{
    public TargetProcess(Process process)
    {
        Name = process.ProcessName;
        Pid = process.Id;

        try
        {
            StartTime = process.StartTime;
        }
        catch
        {
        }
    }

    public string Name { get; set; }

    public int Pid { get; set; }

    public DateTime? StartTime { get; set; }
}
