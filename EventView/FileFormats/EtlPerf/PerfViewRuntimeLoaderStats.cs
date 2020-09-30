using System.Threading.Tasks;
using EventView.Dialogs;

namespace EventView.FileFormats.EtlPerf
{
    internal class PerfViewRuntimeLoaderStats : IFilePart
    {
        private ETLPerfFileFormat eTLPerfFileFormat;

        public PerfViewRuntimeLoaderStats(ETLPerfFileFormat eTLPerfFileFormat)
        {
            this.eTLPerfFileFormat = eTLPerfFileFormat;
        }

        public string Group { get; }

        public Task Open(IDialogPlaceHolder dialogPlaceHolder)
        {
            throw new System.NotImplementedException();
        }

        public string Name { get; }
    }
}