using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace EventView.FileFormats.EtlPerf
{
    public interface IEtlFilePart : IFilePart
    {
        Task Init(IFileFormat fileFormat, TraceLog traceLog);
        bool IsExist(EtlPerfFileStats stats);
        IFileFormat FileFormat { get; }
    }
}