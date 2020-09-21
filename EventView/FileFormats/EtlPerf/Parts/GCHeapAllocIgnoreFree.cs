namespace EventView.FileFormats.EtlPerf.Parts
{
    public class GCHeapAllocIgnoreFree : EtlStackSourceFilePart
    {
        public GCHeapAllocIgnoreFree() : base("Memory Group", "GC Heap Alloc Ignore Free")
        {
            
        }
        public override bool IsExist(EtlPerfFileStats stats)
        {
            return stats.HasMemAllocStacks;
        }
    }
}