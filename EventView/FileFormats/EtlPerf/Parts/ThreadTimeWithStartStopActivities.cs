namespace EventView.FileFormats.EtlPerf.Parts
{
    public class ThreadTimeWithStartStopActivities : EtlStackSourceFilePart
    {
        public ThreadTimeWithStartStopActivities() : base("Thread Time (with StartStop Activities)")
        {
            
        }
        public override bool IsExist(EtlPerfFileStats stats)
        {
            return stats.HasCSwitchStacks && stats.HasTplStacks;
        }
    }
}