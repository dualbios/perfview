using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace EventView.FileFormats.EtlPerf
{
    internal class PerfViewEventSource : IEtlFilePart
    {

        public PerfViewEventSource()
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