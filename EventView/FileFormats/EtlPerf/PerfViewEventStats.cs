using System.Threading.Tasks;
using EventView.Dialogs;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace EventView.FileFormats.EtlPerf
{
    internal class PerfViewEventStats : IEtlFilePart
    {
        public PerfViewEventStats()
        {
        }

        public IFileFormat FileFormat => throw new System.NotImplementedException();
        public string Group { get; } = "Advanced Group";

        public string Name { get; } = "PerfViewEventStats";

        public Task Init(TraceLog traceLog)
        {
            return Task.CompletedTask;
        }

        public Task Init(IFileFormat fileFormat, TraceLog traceLog)
        {
            return Task.CompletedTask;
        }

        public bool IsExist(EtlPerfFileStats stats)
        {
            return true;
        }

        public Task Open(IDialogPlaceHolder dialogPlaceHolder, ITabHolder tabHolder)
        {
            throw new System.NotImplementedException();
        }
    }
}