namespace EventView.FileFormats.EtlPerf.Parts
{
    public class ThreadTimeWithReadyThread : EtlStackSourceFilePart
    {
        public ThreadTimeWithReadyThread() :base("Thread Time (with ReadyThread)")
        {
            
        }
        public override bool IsExist(EtlPerfFileStats stats)
        {
            return stats.HasCSwitchStacks && stats.HasReadyThreadStacks;
        }
    }
}