﻿using System;

namespace PerfEventView.Utils.Process
{
    internal class ProcessForProcessInfo : IProcess
    {
        public ProcessForProcessInfo(ProcessInfo process)
        {
            Process = process;
        }

        public string CommandLine { get { return Process.CommandLine; } }
        public double CPUTimeMSec { get { return Process.CpuTime100ns / 10000.0; } }

        public string Duration
        {
            get
            {
                double duration = (DateTime.Now - Process.CreationDate).TotalSeconds;
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

        public DateTime EndTime { get { return DateTime.Now; } }
        public string Name { get { return new string(' ', ParentDepth(Process)) + Process.Name; } }
        public int ParentID { get { if (Process.Parent == null) { return 0; } return Process.ParentProcessID; } }
        public ProcessInfo Process { get; private set; }

        public int ProcessID { get { return Process.ProcessID; } }
        public DateTime StartTime { get { return Process.CreationDate; } }

        public int CompareTo(IProcess other)
        {
            var traceProcess1 = Process;
            var traceProcess2 = ((ProcessForProcessInfo)other).Process;
            return Compare(traceProcess1, ParentDepth(traceProcess1), traceProcess2, ParentDepth(traceProcess2));
        }

        public override string ToString()
        {
            return Process.ToString();
        }

        #region private

        private static int Compare(ProcessInfo process1, int depth1, ProcessInfo process2, int depth2)
        {
            if (process1 == process2)
            {
                return 0;
            }

            int ret;
            if (depth1 > depth2)
            {
                ret = Compare(process1.Parent, depth1 - 1, process2, depth2);
                if (ret == 0)
                {
                    ret = 1;
                }

                return ret;
            }
            if (depth2 > depth1)
            {
                ret = Compare(process1, depth1, process2.Parent, depth2 - 1);
                if (ret == 0)
                {
                    ret = -1;
                }

                return ret;
            }
            if (depth1 > 0)
            {
                ret = Compare(process1.Parent, depth1 - 1, process2.Parent, depth2 - 1);
                if (ret != 0)
                {
                    return ret;
                }
            }

            // If parents are the same, we sort by time. youngest first
            ret = -process1.CreationDate.CompareTo(process2.CreationDate);
            if (ret != 0)
            {
                return ret;
            }

            // If times are the same, sort by process ID (decending)
            return -process1.ProcessID.CompareTo(process2.ProcessID);
        }

        /// <summary>
        /// Find the depth of a process (0 means I have no parent).
        /// </summary>
        private static int ParentDepth(ProcessInfo process)
        {
            int ret = 0;
            while (process.Parent != null && process.ProcessID != 0)
            {
                process = process.Parent;
                ret++;
                if (ret > 1000)            // Trivial loop prevention.  TODO do better.
                {
                    return 0;
                }
            }
            return ret;
        }

        #endregion private
    }
}