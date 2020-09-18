using System.Threading.Tasks;

namespace EventView.FileFormats
{
    public class PerfViewTreeGroupFilePart_Temp : IFilePart
    {
        public string Group { get; }

        public Task Open()
        {
            throw new System.NotImplementedException();
        }
    }
}