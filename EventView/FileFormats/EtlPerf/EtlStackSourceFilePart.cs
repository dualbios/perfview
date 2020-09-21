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

        public Task Init(TraceLog traceLog)
        {
            throw new System.NotImplementedException();
        }

        public abstract bool IsExist(EtlPerfFileStats stats);

        public Task Open()
        {
            throw new System.NotImplementedException();
        }
    }
}