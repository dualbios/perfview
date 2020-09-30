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

        public override async Task Open(IDialogPlaceHolder dialogPlaceHolder, ITabHolder tabHolder)
        {
            ProcessListDialogViewModel dialog = base.GetProcessDialog();
            await dialogPlaceHolder.Show(dialog, async d =>
                {
                    IEnumerable<IProcess> processes = dialog.GetSelectedProcesses();
                    IDataViewer dataViewer = CreateDataViewer();

                    tabHolder.Add(dataViewer);

                    await dataViewer.Initialize();
                }
            );
        }

        private IDataViewer CreateDataViewer()
        {
            return new SimpleDataViewer();
        }
    }
}