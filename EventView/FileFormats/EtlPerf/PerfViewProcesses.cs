using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace EventView.FileFormats.EtlPerf
{
    internal class PerfViewProcesses : IEtlFilePart
    {
        private ETLPerfFileFormat eTLPerfFileFormat;

        public PerfViewProcesses()
        {
        }

        public string Group { get; }

        public Task Open()
        {
            throw new System.NotImplementedException();
        }

        public string Name { get; } = "PerfViewProcesses";
        public Task Init(TraceLog traceLog)
        {
            return Task.CompletedTask;
        }

        public bool IsExist(EtlPerfFileStats stats)
        {
            return true;
        }
    }
}