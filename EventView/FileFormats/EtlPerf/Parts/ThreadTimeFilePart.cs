namespace EventView.FileFormats.EtlPerf.Parts
{
    public class ThreadTimeFilePart : EtlStackSourceFilePart
    {
        public ThreadTimeFilePart() : base("Thread Time")
        {
        }

        public override bool IsExist(EtlPerfFileStats stats)
        {
            return stats.HasCSwitchStacks;
        }
    }
}