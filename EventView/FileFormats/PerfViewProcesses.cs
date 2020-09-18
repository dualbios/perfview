using System.Threading.Tasks;

namespace EventView.FileFormats
{
    internal class PerfViewProcesses : IFilePart
    {
        private ETLPerfFileFormat eTLPerfFileFormat;

        public PerfViewProcesses(ETLPerfFileFormat eTLPerfFileFormat)
        {
            this.eTLPerfFileFormat = eTLPerfFileFormat;
        }

        public string Group { get; }

        public Task Open()
        {
            throw new System.NotImplementedException();
        }
    }
}