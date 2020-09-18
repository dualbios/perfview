using System.Threading.Tasks;
using System.Windows.Input;
using EventView.FileFormats;
using Microsoft.Win32;

namespace EventView.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private IFileFormatFactory _fileFormatFactory;
        private bool _isFileOpened;
        private RelayCommand _openCommand;

        public bool IsFileOpened
        {
            get => _isFileOpened;
            set => SetProperty(ref _isFileOpened, value);
        }

        public ICommand OpenCommand => _openCommand ?? (_openCommand = new RelayCommand(async (o) => await OpenFile(), o => !IsFileOpened));

        private async Task OpenFile()
        {
            OpenFileDialog ofd = new OpenFileDialog();
            if (ofd.ShowDialog() == true)
            {
                IFileFormat fileFormat = _fileFormatFactory.Get(ofd.FileName);
                await fileFormat.Open(ofd.FileName);
                IsFileOpened = true;
            }
        }
    }
}