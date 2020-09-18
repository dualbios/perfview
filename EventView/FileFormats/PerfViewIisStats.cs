using System.Threading.Tasks;

namespace EventView.FileFormats
{
    internal class PerfViewIisStats : IFilePart
    {
        private ETLPerfFileFormat eTLPerfFileFormat;

        public PerfViewIisStats(ETLPerfFileFormat eTLPerfFileFormat)
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