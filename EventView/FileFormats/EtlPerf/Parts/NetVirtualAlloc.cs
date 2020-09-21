namespace EventView.FileFormats.EtlPerf.Parts
{
    public class NetVirtualAlloc : EtlStackSourceFilePart
    {
        public NetVirtualAlloc() : base("Memory Group", "Net Virtual Alloc")
        {
            
        }
        public override bool IsExist(EtlPerfFileStats stats)
        {
            return stats.HasVirtAllocStacks;
        }
    }
}