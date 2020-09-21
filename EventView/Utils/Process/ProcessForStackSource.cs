using System;

namespace PerfEventView.Utils.Process
{
    internal class ProcessForStackSource : IProcess
    {
        internal ProcessForStackSource(string name)
        {
            Name = name;
            StartTime = DateTime.MaxValue;
            CommandLine = "";
        }

        public string CommandLine { get; internal set; }
        public double CPUTimeMSec { get; internal set; }

        public string Duration
        {
            get
            {
                double duration = (EndTime - StartTime).TotalSeconds;
                if (duration < 60)
                {
                    return duration.ToString("f2") + " sec";
                }

                duration /= 60;
                if (duration < 60)
                {
                    return duration.ToString("f2") + " min";
                }

                duration /= 60;
                if (duration < 60)
                {
                    return duration.ToString("f2") + " hr";
                }

                duration /= 24;
                if (duration < 365)
                {
                    return duration.ToString("f2") + " days";
                }

                duration /= 365;
                return duration.ToString("f2") + " yr";
            }
        }

        public DateTime EndTime { get; internal set; }
        public string Name { get; private set; }
        public int ParentID { get; internal set; }
        public int ProcessID { get; internal set; }
        public DateTime StartTime { get; internal set; }

        public int CompareTo(IProcess other)
        {
            // Choose largest CPU time first.
            var ret = -CPUTimeMSec.CompareTo(other.CPUTimeMSec);
            if (ret != 0)
            {
                return ret;
            }
            // Otherwise go by date (reversed)
            return -StartTime.CompareTo(other.StartTime);
        }

        public override string ToString()
        {
            return "<Process Name=\"" + Name +
                   "\" CPUTimeMSec=\"" + CPUTimeMSec +
                   "\" Duration=\"" + Duration +
                   "\">";
        }
    }
}