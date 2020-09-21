namespace EventView.FileFormats.EtlPerf.Parts
{
    public class ImageLoad : EtlStackSourceFilePart
    {
        public ImageLoad() : base("Image Load")
        {
        }

        public override bool IsExist(EtlPerfFileStats stats)
        {
            return stats.HasDllStacks;
        }
    }
}