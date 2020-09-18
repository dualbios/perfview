using System.Collections.Generic;

namespace EventView.FileFormats.EtlPerf
{
    public class EtlPerfPartFactory : IEtlPerfPartFactory
    {
        public IEnumerable<IEtlFilePart> GetSupportedPart()
        {
            yield return new PerfViewTraceInfo();
            yield return new PerfViewProcesses();
            yield return new PerfViewEventStats();
            yield return new PerfViewEventSource();
        }
    }
}