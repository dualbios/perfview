using System.Collections.Generic;

namespace EventView.FileFormats
{
    public interface IFileFormatFactory
    {
        IFileFormat Get(string fileName);
    }
}