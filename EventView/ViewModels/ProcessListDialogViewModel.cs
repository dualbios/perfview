using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using EventView.Dialogs;
using EventView.FileFormats;
using PerfEventView.Utils.Process;

namespace EventView.ViewModels
{
    public class ProcessListDialogViewModel : ViewModelBase, IDialog
    {
        private readonly IFileFormat _fileFormat;

        private IEnumerable _processList;

        private IProcess _selectedProcess;

        public ProcessListDialogViewModel(IFileFormat fileFormat)
        {
            _fileFormat = fileFormat;
        }

        public IEnumerable ProcessList
        {
            get => _processList;
            set => SetProperty(ref _processList, value);
        }

        public IProcess SelectedProcess
        {
            get => _selectedProcess;
            set => SetProperty(ref _selectedProcess, value);
        }

        public string Title { get; } = "Process select dialog";

        public IEnumerable<IProcess> GetSelectedProcesses()
        {
            return new IProcess[] { SelectedProcess };
        }

        public async Task Initialize()
        {
            ProcessList = await _fileFormat.GetProcesses();
        }
    }
}