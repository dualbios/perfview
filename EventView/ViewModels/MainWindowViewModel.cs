using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using EventView.Dialogs;
using EventView.FileFormats;
using Microsoft.Win32;

namespace EventView.ViewModels
{
    public class MainWindowViewModel : ViewModelBase, IDialogPlaceHolder
    {
        private readonly IFileFormatFactory _fileFormatFactory;
        private ICommand _cancelCommand;
        private IDialog _currentDialog = null;
        private IDialog _dialogContainer;
        private IEnumerable<IFilePart> _fileParts;
        private string _filePath;
        private bool _isFileOpened;
        private bool _isFileOpening;
        private Action<IDialog> _okAction = null;
        private RelayCommand _okCommand = null;
        private RelayCommand _openCommand;

        private ICommand _openPartCommand = null;

        public MainWindowViewModel(IFileFormatFactory fileFormatFactory)
        {
            _fileFormatFactory = fileFormatFactory;
        }

        public ICommand CancelCommand
        {
            get
            {
                if (_cancelCommand == null)
                {
                    _cancelCommand = new RelayCommand(c => CancelAction(), c => DialogContainer != null);
                }
                return _cancelCommand;
            }
        }

        public IDialog DialogContainer
        {
            get => _dialogContainer;
            set => SetProperty(ref _dialogContainer, value);
        }

        public IEnumerable<IFilePart> FileParts
        {
            get => _fileParts;
            private set => SetProperty(ref _fileParts, value);
        }

        public string FilePath
        {
            get => _filePath;
            set => SetProperty(ref _filePath, value);
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

        public ICommand OkCommand
        {
            get
            {
                if (_okCommand == null)
                {
                    _okCommand = new RelayCommand(o => OkAction(), o => DialogContainer != null);
                }
                return _okCommand;
            }
        }

        public ICommand OpenCommand => _openCommand ?? (_openCommand = new RelayCommand(async (o) => await OpenFile(), o => !IsFileOpened));

        public ICommand OpenPartCommand
        {
            get
            {
                if (_openPartCommand == null)
                {
                    _openPartCommand = new RelayCommand(async x => await OpenPart(x));
                }

                return _openPartCommand;
            }
        }

        public void Show(IDialog dialog, Action<IDialog> okAction)
        {
            DialogContainer = dialog;
            _currentDialog = dialog;
            _okAction = okAction;
        }

        private void CancelAction()
        {
            _currentDialog = null;
            DialogContainer = null;
        }

        private void OkAction()
        {
            _okAction(_currentDialog);
            _currentDialog = null;
            DialogContainer = null;
        }

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
                    FileParts = fileFormat.FileParts;
                    FilePath = ofd.FileName;
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

        private async Task OpenPart(object o)
        {
            IFilePart filePart = (o as IFilePart);
            if (filePart != null)
            {
                await filePart.Open(this);
            }
        }
    }
}