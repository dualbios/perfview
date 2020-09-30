using System.Threading.Tasks;
using EventView.Dialogs;

namespace EventView.FileFormats.EtlPerf
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

        public Task Open(IDialogPlaceHolder dialogPlaceHolder)
        {
            throw new System.NotImplementedException();
        }
    }
}