using System.Collections.Generic;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace EventView.FileFormats.EtlPerf
{
    public interface IEtlPerfPartFactory
    {
        EtlPerfFileStats CreateStats(TraceEventStats stats);

        IEnumerable<IEtlFilePart> GetParts(EtlPerfFileStats stats);

        IEnumerable<IEtlFilePart> GetSupportedPart();
    }
}