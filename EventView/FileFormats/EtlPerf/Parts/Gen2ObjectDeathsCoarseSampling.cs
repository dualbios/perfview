namespace EventView.FileFormats.EtlPerf.Parts
{
    public class Gen2ObjectDeathsCoarseSampling : EtlStackSourceFilePart

    {
        public Gen2ObjectDeathsCoarseSampling() : base("Memory Group", "Gen 2 Object Deaths (Coarse Sampling)")
        {
        }

        public override bool IsExist(EtlPerfFileStats stats)
        {
            return stats.HasGCAllocationTicks && stats.HasCSwitchStacks;
        }
    }
}