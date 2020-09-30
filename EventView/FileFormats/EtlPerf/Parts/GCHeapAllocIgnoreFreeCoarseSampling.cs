using System.Collections.Generic;
using System.Threading.Tasks;
using EventView.Dialogs;
using EventView.ViewModels;
using PerfEventView.Utils.Process;

namespace EventView.FileFormats.EtlPerf.Parts
{
    public class GCHeapAllocIgnoreFreeCoarseSampling : EtlStackSourceFilePart
    {
        public GCHeapAllocIgnoreFreeCoarseSampling() : base("Memory Group", "GC Heap Alloc Ignore Free (Coarse Sampling)")
        {
        }

        public override bool IsExist(EtlPerfFileStats stats)
        {
            // FOR TEST
            //return stats.HasGCAllocationTicks;
            return true;
        }

        public override Task Open(IDialogPlaceHolder dialogPlaceHolder)
        {
            ProcessListDialogViewModel dialog = base.GetProcessDialog();
            dialogPlaceHolder.Show(dialog, d =>
                {
                    IEnumerable<IProcess> processes = dialog.GetSelectedProcesses();
                }
            );

            return Task.CompletedTask;
        }
    }
}