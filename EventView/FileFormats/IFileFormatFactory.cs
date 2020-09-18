namespace EventView.FileFormats
{
    internal interface IFileFormatFactory
    {
        IFileFormat Get(string fileName);
    }
}