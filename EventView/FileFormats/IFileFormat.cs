using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using EventView.Dialogs;
using PerfEventView.Utils.Process;

namespace EventView.FileFormats
{
    public interface IFileFormat
    {
        Task ParseAsync(string fileName);
        string FormatName { get; }
        string[] FileExtensions { get; }
        IList<IFilePart> FileParts { get; }
        Task<List<IProcess>> GetProcesses();
    }
}