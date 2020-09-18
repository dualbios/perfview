using System.Threading.Tasks;

namespace EventView.FileFormats
{
    internal class PerfViewHeapSnapshots : IFilePart
    {
        private ETLPerfFileFormat eTLPerfFileFormat;

        public PerfViewHeapSnapshots(ETLPerfFileFormat eTLPerfFileFormat)
        {
            this.eTLPerfFileFormat = eTLPerfFileFormat;
        }

        public string Group { get; } = "Memory Group";

        public Task Open()
        {
            throw new System.NotImplementedException();
        }
    }
}