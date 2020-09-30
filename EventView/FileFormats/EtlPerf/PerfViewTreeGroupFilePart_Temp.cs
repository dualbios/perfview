using System.Threading.Tasks;
using EventView.Dialogs;

namespace EventView.FileFormats.EtlPerf
{
    public class PerfViewTreeGroupFilePart_Temp : IFilePart
    {
        public string Group { get; }

        public Task Open(IDialogPlaceHolder dialogPlaceHolder)
        {
            throw new System.NotImplementedException();
        }

        public string Name { get; }
    }
}