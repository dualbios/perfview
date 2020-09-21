namespace EventView.FileFormats.EtlPerf.Parts
{
    public class Pinning : EtlStackSourceFilePart
    {
        public Pinning() : base("Pinning")
        {
        }

        public override bool IsExist(EtlPerfFileStats stats)
        {
            return stats.HasGCHandleStacks;
        }
    }
}