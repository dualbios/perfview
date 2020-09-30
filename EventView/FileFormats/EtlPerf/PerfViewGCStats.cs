using System.Threading.Tasks;
using EventView.Dialogs;

namespace EventView.FileFormats.EtlPerf
{
    internal class PerfViewGCStats : IFilePart
    {
        private ETLPerfFileFormat eTLPerfFileFormat;

        public PerfViewGCStats(ETLPerfFileFormat eTLPerfFileFormat)
        {
            this.eTLPerfFileFormat = eTLPerfFileFormat;
        }

        public string Group { get; } = "Memory Group";

        public Task Open(IDialogPlaceHolder dialogPlaceHolder)
        {
            throw new System.NotImplementedException();
        }

        public string Name { get; }
    }
}