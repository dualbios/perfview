using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace EventView.FileFormats.EtlPerf
{
    internal class PerfViewTraceInfo : IEtlFilePart
    {

        public PerfViewTraceInfo()
        {
        }

        public string Group { get; }

        public Task Open()
        {
            throw new System.NotImplementedException();
        }

        public string Name { get; }
        public Task Init(TraceLog traceLog)
        {
            throw new System.NotImplementedException();
        }
    }
}