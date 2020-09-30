using System.Threading.Tasks;
using EventView.Dialogs;
using EventView.FileFormats.EtlPerf;

namespace EventView.FileFormats
{
    public class MemoryAnalyzer : IFilePart
    {
        public MemoryAnalyzer(ETLPerfFileFormat eTLPerfFileFormat)
        {
        }

        public string Group { get; } = "Memory Group";
        public Task Open(IDialogPlaceHolder dialogPlaceHolder, ITabHolder tabHolder)
        {
            throw new System.NotImplementedException();
        }

        public string Name { get; }
    }
}