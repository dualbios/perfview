namespace EventView.FileFormats.EtlPerf.Parts
{
    public class ProcessesFilesRegistryFilePart : EtlStackSourceFilePart
    {
        public ProcessesFilesRegistryFilePart() : base("Processes / Files / Registry")
        {
        }

        public override bool IsExist(EtlPerfFileStats stats)
        {
            return true;
        }
    }
}