using System.Collections.Generic;
using System.Threading.Tasks;
using EventView.Dialogs;
using EventView.ViewModels;
using PerfEventView.Utils.Process;

namespace EventView.FileFormats.EtlPerf.Parts
{
    public class GCHeapAllocIgnoreFreeCoarseSampling : EtlStackSourceFilePart
    {
        private readonly IDialogPlaceHolder _dialogPlaceHolder;

        public GCHeapAllocIgnoreFreeCoarseSampling(IDialogPlaceHolder dialogPlaceHolder) : base("Memory Group", "GC Heap Alloc Ignore Free (Coarse Sampling)")
        {
            _dialogPlaceHolder = dialogPlaceHolder;
        }

        public override bool IsExist(EtlPerfFileStats stats)
        {
            //return stats.HasGCAllocationTicks;
            return true;
        }

        public override async Task Open()
        {
            ProcessListDialogViewModel dialog = base.GetProcessDialog();
            _dialogPlaceHolder.Show(dialog, d =>
                {
                    IEnumerable<IProcess> processes = dialog.GetSelectedProcesses();
                }
            );
        }
    }
}