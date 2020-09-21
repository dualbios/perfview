namespace EventView.FileFormats.EtlPerf.Parts
{
    internal class FileIOFilePart : EtlStackSourceFilePart
    {
        public FileIOFilePart() : base("File I/O")
        {
        }

        public override bool IsExist(EtlPerfFileStats stats)
        {
            return stats.HasFileStacks;
        }
    }
}