namespace EventView.FileFormats.EtlPerf.Parts
{
    public class Gen2ObjectDeaths : EtlStackSourceFilePart
    {
        public Gen2ObjectDeaths() : base("Memory Group", "Gen 2 Object Deaths")
        {
            
        }
        public override bool IsExist(EtlPerfFileStats stats)
        {
            return stats.HasMemAllocStacks;
        }
    }
}