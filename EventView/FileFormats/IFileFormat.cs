using System.Threading.Tasks;

namespace EventView.FileFormats
{
    internal interface IFileFormat
    {
        Task Open(string fileName);
    }
}