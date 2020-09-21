namespace EventView.FileFormats.EtlPerf.Parts
{
    public class Exceptions : EtlStackSourceFilePart
    {
        public Exceptions() : base("Exceptions")
        {
        }

        public override bool IsExist(EtlPerfFileStats stats)
        {
            return stats.HasExceptions;
        }
    }
}