using System.Threading.Tasks;

namespace EventView.FileFormats.EtlPerf
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

        public string Name { get; }
    }
}