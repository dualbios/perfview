using System.Threading.Tasks;

namespace EventView.Dialogs
{
    public interface IDialog
    {
        string Title { get; }

        Task Initialize();
    }
}