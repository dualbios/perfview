namespace EventView.FileFormats.EtlPerf.Parts
{
    internal class DiskIOFilePart : EtlStackSourceFilePart
    {
        public DiskIOFilePart() : base("Disk I/O")
        {
        }

        public override bool IsExist(EtlPerfFileStats stats)
        {
            return stats.HasDiskStacks;
        }
    }
}