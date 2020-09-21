using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace EventView.FileFormats.EtlPerf
{
    public abstract class EtlStackSourceFilePart : IEtlFilePart
    {
        public EtlStackSourceFilePart()
        {
        }

        public EtlStackSourceFilePart(string name)
        {
            Name = name;
        }

        public EtlStackSourceFilePart(string group, string name) : this(name)
        {
            Group = group;
        }

        public string Group { get; }
        public string Name { get; }
        public TraceLog TraceLog { get; private set; }

        public IFileFormat FileFormat { get; private set; }

        public virtual Task Init(IFileFormat fileFormat, TraceLog traceLog)
        {
            FileFormat = fileFormat;
            TraceLog = traceLog;

            return Task.CompletedTask;
        }


        public abstract bool IsExist(EtlPerfFileStats stats);

        public virtual Task Open()
        {
            
        }
    }
}