using System.Threading.Tasks;
using EventView.Dialogs;

namespace EventView.FileFormats
{
    public interface IFilePart
    {
        string Group { get; }
        Task Open(IDialogPlaceHolder dialogPlaceHolder, ITabHolder tabHolder);
        string Name { get; }
    }
}