using System.Collections.Generic;
using System.Threading.Tasks;
using EventView.Dialogs;

namespace EventView.FileFormats
{
    public interface IFileFormat
    {
        Task ParseAsync(string fileName);
        string FormatName { get; }
        string[] FileExtensions { get; }
        IList<IFilePart> FileParts { get; }

        void Init(IDialogPlaceHolder dialogPlaceHolder);
    }
}