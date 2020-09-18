using System.Threading.Tasks;

namespace EventView.FileFormats
{
    internal class PerfViewTraceInfo : IFilePart
    {
        private ETLPerfFileFormat eTLPerfFileFormat;

        public PerfViewTraceInfo(ETLPerfFileFormat eTLPerfFileFormat)
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