using System.Threading.Tasks;
using EventView.Dialogs;
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

        public Task Open(IDialogPlaceHolder dialogPlaceHolder, ITabHolder tabHolder)
        {
            throw new System.NotImplementedException();
        }

        public string Name { get; } = "PerfViewProcesses";

        public IFileFormat FileFormat => throw new System.NotImplementedException();

        public Task Init(TraceLog traceLog)
        {
            return Task.CompletedTask;
        }

        public bool IsExist(EtlPerfFileStats stats)
        {
            return true;
        }

        public Task Init(IFileFormat fileFormat, TraceLog traceLog)
        {
            return Task.CompletedTask;
        }
    }
}