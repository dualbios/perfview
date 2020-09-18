using System.Threading.Tasks;

namespace EventView.FileFormats
{
    internal class PerfViewJitStats : IFilePart
    {
        private ETLPerfFileFormat eTLPerfFileFormat;

        public PerfViewJitStats(ETLPerfFileFormat eTLPerfFileFormat)
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