using System.Collections.Generic;

namespace EventView.FileFormats.EtlPerf
{
    public interface IEtlPerfPartFactory
    {
        IEnumerable<IEtlFilePart> GetSupportedPart();
    }
}