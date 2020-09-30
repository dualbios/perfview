using System.Collections.Generic;
using EventView.Dialogs;
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