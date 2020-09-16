namespace PerfView.PerfViewData
{
    /// <summary>
    /// Represents a single heap snapshot in a ETL file (currently only JScript).  
    /// </summary>
    internal class PerfViewHeapSnapshot : HeapDumpPerfViewFile
    {
        /// <summary>
        /// snapshotKinds should be .NET or JS
        /// </summary>
        public PerfViewHeapSnapshot(PerfViewFile file, int processId, string processName, double timeRelativeMSec, string snapshotKind)
        {
            m_snapshotKind = snapshotKind;
            m_timeRelativeMSec = timeRelativeMSec;
            m_filePath = file.FilePath;
            Kind = snapshotKind;
            m_processId = processId;
            Name = snapshotKind + " Heap Snapshot " + processName + "(" + processId + ") at " + timeRelativeMSec.ToString("n3") + " MSec";
        }
        public override string HelpAnchor { get { return "JSHeapSnapshot"; } }
        public string Kind { get; private set; }

        internal string m_snapshotKind;
        internal double m_timeRelativeMSec;
        internal int m_processId;
    };
}