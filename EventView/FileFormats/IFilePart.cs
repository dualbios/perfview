using System.Threading.Tasks;
using EventView.Dialogs;

namespace EventView.FileFormats
{
    public interface IFilePart
    {
        string Group { get; }
        Task Open(IDialogPlaceHolder dialogPlaceHolder);
        string Name { get; }
    }
}