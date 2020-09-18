using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using EventView.FileFormats;
using Microsoft.Win32;

namespace EventView.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly IFileFormatFactory _fileFormatFactory;
        private bool _isFileOpened;
        private bool _isFileOpening;
        private RelayCommand _openCommand;

        public MainWindowViewModel(IFileFormatFactory fileFormatFactory)
        {
            _fileFormatFactory = fileFormatFactory;
        }

        public bool IsFileOpened
        {
            get => _isFileOpened;
            set => SetProperty(ref _isFileOpened, value);
        }

        public bool IsFileOpening
        {
            get => _isFileOpening;
            set => SetProperty(ref _isFileOpening, value);
        }

        public ICommand OpenCommand => _openCommand ?? (_openCommand = new RelayCommand(async (o) => await OpenFile(), o => !IsFileOpened));

        private async Task OpenFile()
        {
            OpenFileDialog ofd = new OpenFileDialog();
            if (ofd.ShowDialog() == true)
            {
                IFileFormat fileFormat = _fileFormatFactory.Get(ofd.FileName);
                if (fileFormat == null)
                {
                    MessageBox.Show($"Unknown file format for '{ofd.FileName}'");
                    return;
                }

                try
                {
                    IsFileOpening = true;
                    await fileFormat.ParseAsync(ofd.FileName);
                    IsFileOpened = true;
                }
                catch (Exception e)
                {
                    MessageBox.Show($"Exception during opening file '{e.Message}'");
                }
                finally
                {
                    IsFileOpening = false;
                }
            }
        }
    }
}