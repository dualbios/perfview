using System.Threading.Tasks;

namespace EventView.FileFormats.EtlPerf
{
    internal class PerfViewRuntimeLoaderStats : IFilePart
    {
        private ETLPerfFileFormat eTLPerfFileFormat;

        public PerfViewRuntimeLoaderStats(ETLPerfFileFormat eTLPerfFileFormat)
        {
            this.eTLPerfFileFormat = eTLPerfFileFormat;
        }

        public string Group { get; }

        public Task Open()
        {
            throw new System.NotImplementedException();
        }

        public string Name { get; }
    }
}