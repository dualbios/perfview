using System.Collections.Generic;
using EventView.Dialogs;

namespace EventView.FileFormats
{
    public interface IFileFormatFactory
    {
        IFileFormat Get(string fileName);
        void Init(IDialogPlaceHolder dialogPlaceHolder);
    }
}