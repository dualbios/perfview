namespace EventView.FileFormats.EtlPerf.Parts
{
    public class ThreadTimeWithStartStopActivitiesCPUONLY : EtlStackSourceFilePart
    {
        public ThreadTimeWithStartStopActivitiesCPUONLY() : base("Thread Time (with StartStop Activities) (CPU ONLY)")
        {
        }

        public override bool IsExist(EtlPerfFileStats stats)
        {
            return stats.HasCSwitchStacks == false && stats.HasCPUStacks && stats.HasTplStacks;
        }
    }
}