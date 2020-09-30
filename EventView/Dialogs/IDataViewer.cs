using System.Threading.Tasks;

namespace EventView.Dialogs
{
    public interface IDataViewer
    {
        string Title { get; }

        Task Initialize();
    }
}