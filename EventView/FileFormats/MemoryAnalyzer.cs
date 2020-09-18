using System.Threading.Tasks;

namespace EventView.FileFormats
{
    public class MemoryAnalyzer : IFilePart
    {
        public MemoryAnalyzer(ETLPerfFileFormat eTLPerfFileFormat)
        {
        }

        public string Group { get; } = "Memory Group";
        public Task Open()
        {
            throw new System.NotImplementedException();
        }
    }
}