using System.Threading.Tasks;
using EventView.Dialogs;

namespace EventView.ViewModels
{
    public class SimpleDataViewer : IDataViewer
    {
        public string Title { get; } = "Simpla data Viewer";

        public Task Initialize()
        {
            return Task.Delay(1500);
        }
    }
}