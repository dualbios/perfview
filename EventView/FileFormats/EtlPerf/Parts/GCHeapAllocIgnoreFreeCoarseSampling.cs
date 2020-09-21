namespace EventView.FileFormats.EtlPerf.Parts
{
    public class GCHeapAllocIgnoreFreeCoarseSampling : EtlStackSourceFilePart
    {
        public GCHeapAllocIgnoreFreeCoarseSampling() : base("Memory Group", "GC Heap Alloc Ignore Free (Coarse Sampling)")
        {
        }

        public override bool IsExist(EtlPerfFileStats stats)
        {
            return stats.HasGCAllocationTicks;
        }
    }
}