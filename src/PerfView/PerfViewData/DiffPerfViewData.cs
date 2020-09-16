using System.IO;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace PerfView.PerfViewData
{
    internal class DiffPerfViewData : PerfViewStackSource
    {
        public DiffPerfViewData(PerfViewStackSource data, PerfViewStackSource baseline)
            : base(data.DataFile, data.SourceName)
        {
            m_baseline = baseline;
            m_data = data;
            Name = string.Format("Diff {0} baseline {1}", data.Name, baseline.Name);
        }

        public override string Title
        {
            get
            {
                // TODO do better. 
                return Name;
            }
        }
        public override StackSource GetStackSource(TextWriter log, double startRelativeMSec = 0, double endRelativeMSec = double.PositiveInfinity)
        {
            return InternStackSource.Diff(m_data.GetStackSource(log, startRelativeMSec, endRelativeMSec), m_baseline.GetStackSource(log, startRelativeMSec, endRelativeMSec));
        }

        #region private
        private PerfViewStackSource m_data;
        private PerfViewStackSource m_baseline;
        #endregion
    }
}