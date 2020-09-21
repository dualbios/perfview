namespace EventView.FileFormats.EtlPerf.Parts
{
    public class NetVirtualReserve : EtlStackSourceFilePart
    {
        public NetVirtualReserve() : base("Memory Group", "Net Virtual Reserve")
        {
        }

        public override bool IsExist(EtlPerfFileStats stats)
        {
            return stats.HasVirtAllocStacks;
        }
    }
}