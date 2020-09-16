using System.IO;
using Graphs;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace PerfView.PerfViewData
{
    internal class ClrProfilerHeapPerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return "CLR Profiler Heap"; } }
        public override string[] FileExtensions { get { return new string[] { ".gcheap", ".clrprofiler" }; } }

        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            Graph graph = new ClrProfilerMemoryGraph(FilePath);

            // TODO FIX NOW var refGraph = new Experimental.RefGraph(graph);

            log.WriteLine(
                "Opened Graph {0} Bytes: {1:f3}M NumObjects: {2:f3}K  NumRefs: {3:f3}K Types: {4:f3}K RepresentationSize: {5:f1}M",
                FilePath, graph.TotalSize / 1000000.0, (int)graph.NodeIndexLimit / 1000.0,
                graph.TotalNumberOfReferences / 1000.0, (int)graph.NodeTypeIndexLimit / 1000.0,
                graph.SizeOfGraphDescription() / 1000000.0);

            log.WriteLine("Type Histogram > 1% of heap size");
            log.Write(graph.HistogramByTypeXml(graph.TotalSize / 100));

#if false // TODO FIX NOW remove
            using (StreamWriter writer = File.CreateText(Path.ChangeExtension(this.FilePath, ".Clrprof.xml")))
            {
                ((MemoryGraph)graph).DumpNormalized(writer);
            }
#endif
            var ret = new MemoryGraphStackSource(graph, log);
            return ret;
        }
        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            ConfigureAsMemoryWindow(stackSourceName, stackWindow);
        }
    }
}