using System.Threading.Tasks;

namespace EventView.FileFormats
{
    internal class PerfViewAspNetStats : IFilePart
    {
        private ETLPerfFileFormat eTLPerfFileFormat;

        public PerfViewAspNetStats(ETLPerfFileFormat eTLPerfFileFormat)
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