namespace EventView.FileFormats.EtlPerf.Parts
{
    public class ManagedLoad : EtlStackSourceFilePart
    {
        public ManagedLoad() : base("Managed Load")
        {
        }

        public override bool IsExist(EtlPerfFileStats stats)
        {
            return stats.HasManagedLoads;
        }
    }
}