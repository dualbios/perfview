using System.Threading.Tasks;
using EventView.Dialogs;

namespace EventView.FileFormats.EtlPerf
{
    internal class PerfViewIisStats : IFilePart
    {
        private ETLPerfFileFormat eTLPerfFileFormat;

        public PerfViewIisStats(ETLPerfFileFormat eTLPerfFileFormat)
        {
            this.eTLPerfFileFormat = eTLPerfFileFormat;
        }

        public string Group { get; } = "Advanced Group";

        public Task Open(IDialogPlaceHolder dialogPlaceHolder, ITabHolder tabHolder)
        {
            throw new System.NotImplementedException();
        }

        public string Name { get; }
    }
}