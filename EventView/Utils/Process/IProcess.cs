using System;

namespace PerfEventView.Utils.Process
{
    public interface IProcess : IComparable<IProcess>
    {
        string CommandLine { get; }
        double CPUTimeMSec { get; }
        string Duration { get; }
        DateTime EndTime { get; }
        string Name { get; }
        int ParentID { get; }
        int ProcessID { get; }
        DateTime StartTime { get; }
    }
}