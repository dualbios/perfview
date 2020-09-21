namespace EventView.FileFormats.EtlPerf.Parts
{
    public class ThreadTimeWithTasksFilePart : EtlStackSourceFilePart
    {
        public ThreadTimeWithTasksFilePart() : base("Thread Time(with Tasks)")
        {
        }

        public override bool IsExist(EtlPerfFileStats stats)
        {
            return stats.HasCSwitchStacks && stats.HasTplStacks;
        }
    }
}