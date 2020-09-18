using System.Threading.Tasks;

namespace EventView.FileFormats
{
    internal class PerfViewEventStats : IFilePart
    {
        private ETLPerfFileFormat eTLPerfFileFormat;

        public PerfViewEventStats(ETLPerfFileFormat eTLPerfFileFormat)
        {
            this.eTLPerfFileFormat = eTLPerfFileFormat;
        }

        public string Group { get; } = "Advanced Group";

        public Task Open()
        {
            throw new System.NotImplementedException();
        }
    }
}