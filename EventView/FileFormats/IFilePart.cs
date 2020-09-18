using System.Threading.Tasks;

namespace EventView.FileFormats
{
    public interface IFilePart
    {
        string Group { get; }
        Task Open();
    }
}