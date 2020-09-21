namespace EventView.FileFormats.EtlPerf.Parts
{
    public class GCHeapNetMemCoarseSampling : EtlStackSourceFilePart
    {
        public GCHeapNetMemCoarseSampling() : base("Memory Group", "GC Heap Net Mem (Coarse Sampling)")
        {
            
        }
        public override bool IsExist(EtlPerfFileStats stats)
        {
            return stats.HasGCAllocationTicks && stats.HasCSwitchStacks;
        }
    }
}