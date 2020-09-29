using System.Collections;
using System.Collections.Generic;
using EventView.Dialogs;
using PerfEventView.Utils.Process;

namespace EventView.ViewModels
{
    public class ProcessListDialogViewModel : ViewModelBase, IDialog
    {
        private IProcess _selectedProcess;
        public IEnumerable PeocessList { get; }

        public IProcess SelectedProcess
        {
            get => _selectedProcess;
            set => SetProperty(ref _selectedProcess, value);
        }

        public IEnumerable<IProcess> GetSelectedProcesses()
        {
            return new IProcess[] { SelectedProcess };
        }
    }
}