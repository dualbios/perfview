using System.Threading.Tasks;

namespace EventView.FileFormats
{
    public class PerfViewStackSourceFilePart_Temp : IFilePart
    {
        public PerfViewStackSourceFilePart_Temp(ETLPerfFileFormat eTLPerfFileFormat, string name)
        {
            ETLPerfFileFormat = eTLPerfFileFormat;
            Name = name;
        }

        public PerfViewStackSourceFilePart_Temp(ETLPerfFileFormat eTLPerfFileFormat, string groupName, string name) : this(eTLPerfFileFormat, name)
        {
            Group = groupName;
        }

        public ETLPerfFileFormat ETLPerfFileFormat { get; }
        public string Name { get; }
        public bool SkipSelectProcess { get; internal set; }

        public string Group { get; } = "Advanced Group";

        public Task Open()
        {
            throw new System.NotImplementedException();
        }
    }
}