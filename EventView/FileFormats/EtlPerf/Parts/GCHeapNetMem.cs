namespace EventView.FileFormats.EtlPerf.Parts
{
    public class GCHeapNetMem : EtlStackSourceFilePart
    {
        public GCHeapNetMem() : base ("Memory Group", "GC Heap Net Mem")
        {
            
        }
        public override bool IsExist(EtlPerfFileStats stats)
        {
            return stats.HasMemAllocStacks;
        }
    }
}