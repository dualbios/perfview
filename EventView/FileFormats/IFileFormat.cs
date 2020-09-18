using System.Threading.Tasks;

namespace EventView.FileFormats
{
    public interface IFileFormat
    {
        Task ParseAsync(string fileName);
        string FormatName { get; }
        string[] FileExtensions { get; }
    }
}