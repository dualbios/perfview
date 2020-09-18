namespace EventView.FileFormats
{
    internal class PerfViewEventSource : IFilePart
    {
        private ETLPerfFileFormat eTLPerfFileFormat;

        public PerfViewEventSource(ETLPerfFileFormat eTLPerfFileFormat)
        {
            this.eTLPerfFileFormat = eTLPerfFileFormat;
        }

        public string Group { get; }
    }
}