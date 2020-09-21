namespace EventView.FileFormats.EtlPerf.Parts
{
    public class NetOSHeapAlloc : EtlStackSourceFilePart
    {
        public NetOSHeapAlloc() : base("Memory Group", "Net OS Heap Alloc")
        {
        }

        public override bool IsExist(EtlPerfFileStats stats)
        {
            return stats.HasHeapStacks;
        }
    }
}