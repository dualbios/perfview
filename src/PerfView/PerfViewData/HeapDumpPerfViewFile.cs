using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Graphs;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace PerfView.PerfViewData
{
    internal class HeapDumpPerfViewFile : PerfViewFile
    {
        internal const string Gen0WalkableObjectsViewName = "Gen 0 Walkable Objects";
        internal const string Gen1WalkableObjectsViewName = "Gen 1 Walkable Objects";

        public override string FormatName { get { return "CLR Heap Dump"; } }
        public override string[] FileExtensions { get { return new string[] { ".gcDump", ".gcDump.xml" }; } }

        public const string DiagSessionIdentity = "Microsoft.Diagnostics.GcDump";

        public override string DefaultStackSourceName { get { return "Heap"; } }

        public GCHeapDump GCDump { get { return m_gcDump; } }

        protected internal override StackSource OpenStackSourceImpl(string streamName, TextWriter log, double startRelativeMSec, double endRelativeMSec, Predicate<TraceEvent> predicate)
        {
            OpenDump(log);

            Graph graph = m_gcDump.MemoryGraph;
            GCHeapDump gcDump = m_gcDump;

#if false  // TODO FIX NOW remove
            using (StreamWriter writer = File.CreateText(Path.ChangeExtension(this.FilePath, ".heapDump.xml")))
            {
                ((MemoryGraph)graph).DumpNormalized(writer);
            }
#endif
            int gen = -1;
            if (streamName == Gen0WalkableObjectsViewName)
            {
                Debug.Assert(m_gcDump.DotNetHeapInfo != null);
                gen = 0;
            }
            else if (streamName == Gen1WalkableObjectsViewName)
            {
                Debug.Assert(m_gcDump.DotNetHeapInfo != null);
                gen = 1;
            }

            var ret = GenerationAwareMemoryGraphBuilder.CreateStackSource(m_gcDump, log, gen);

#if false // TODO FIX NOW: support post collection filtering?   
            // Set the sampling ratio so that the number of objects does not get too far out of control.  
            if (2000000 <= (int)graph.NodeIndexLimit)
            {
                ret.SamplingRate = ((int)graph.NodeIndexLimit / 1000000);
                log.WriteLine("Setting the sampling rate to {0}.", ret.SamplingRate);
                MessageBox.Show("The graph has more than 2M Objects.  " +
                    "The sampling rate has been set " + ret.SamplingRate.ToString() + " to keep the GUI responsive.");
            }
#endif
            m_extraTopStats = "";

            double unreachableMemory;
            double totalMemory;
            ComputeUnreachableMemory(ret, out unreachableMemory, out totalMemory);

            if (unreachableMemory != 0)
            {
                m_extraTopStats += string.Format(" Unreachable Memory: {0:n3}MB ({1:f1}%)",
                    unreachableMemory / 1000000.0, unreachableMemory * 100.0 / totalMemory);
            }

            if (gcDump.CountMultipliersByType != null)
            {
                m_extraTopStats += string.Format(" Heap Sampled: Mean Count Multiplier {0:f2} Mean Size Multiplier {1:f2}",
                    gcDump.AverageCountMultiplier, gcDump.AverageSizeMultiplier);
            }

            log.WriteLine("Type Histogram > 1% of heap size");
            log.Write(graph.HistogramByTypeXml(graph.TotalSize / 100));
            return ret;
        }

        public override Action<Action> OpenImpl(Window parentWindow, StatusBar worker)
        {
            if (AppLog.InternalUser)
            {
                OpenDump(worker.LogWriter);

                var advanced = new PerfViewTreeGroup("Advanced Group");

                m_Children = new List<PerfViewTreeItem>(2);

                var defaultSource = new PerfViewStackSource(this, DefaultStackSourceName);
                defaultSource.IsSelected = true;
                m_Children.Add(defaultSource);

                if (m_gcDump.InteropInfo != null)
                {
                    // TODO FIX NOW.   This seems to be broken right now  hiding it for now.  
                    // advanced.Children.Add(new HeapDumpInteropObjects(this));
                }

                if (m_gcDump.DotNetHeapInfo != null)
                {
                    advanced.Children.Add(new PerfViewStackSource(this, Gen0WalkableObjectsViewName));
                    advanced.Children.Add(new PerfViewStackSource(this, Gen1WalkableObjectsViewName));
                }

                if (advanced.Children.Count > 0)
                {
                    m_Children.Add(advanced);
                }

                return null;
            }
            return delegate (Action doAfter)
            {
                // By default we have a singleton source (which we dont show on the GUI) and we immediately open it
                m_singletonStackSource = new PerfViewStackSource(this, "");
                m_singletonStackSource.Open(parentWindow, worker);
                doAfter?.Invoke();
            };
        }

        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            ConfigureAsMemoryWindow(stackSourceName, stackWindow);
            stackWindow.ExtraTopStats = m_extraTopStats;

            if (stackSourceName.Equals(Gen0WalkableObjectsViewName) || stackSourceName.Equals(Gen1WalkableObjectsViewName))
            {
                stackWindow.CallTreeTab.IsSelected = true;      // start with the call tree view
            }
        }

        #region private

        protected internal void OpenDump(TextWriter log)
        {
            if (m_gcDump == null)
            {
                // TODO this is kind of backwards.   The super class should not know about the subclasses.  
                var asSnapshot = this as PerfViewHeapSnapshot;
                if (asSnapshot != null)
                {
                    DotNetHeapInfo dotNetHeapInfo = null;
                    var etlFile = FilePath;
                    CommandProcessor.UnZipIfNecessary(ref etlFile, log);

                    MemoryGraph memoryGraph = null;
                    if (asSnapshot.Kind == "JS")
                    {
                        var dumper = new JavaScriptDumpGraphReader(log);
                        memoryGraph = dumper.Read(etlFile, asSnapshot.m_processId, asSnapshot.m_timeRelativeMSec);
                    }
                    else if (asSnapshot.Kind == ".NET")
                    {
                        var dumper = new DotNetHeapDumpGraphReader(log);
                        dumper.DotNetHeapInfo = dotNetHeapInfo = new DotNetHeapInfo();
                        memoryGraph = dumper.Read(etlFile, asSnapshot.m_processId.ToString(), asSnapshot.m_timeRelativeMSec);
                        var resolver = new TypeNameSymbolResolver(FilePath, log);
                        memoryGraph.ResolveTypeName = resolver.ResolveTypeName;
                    }

                    if (memoryGraph == null)
                    {
                        log.WriteLine("Error Unknown dump kind {0} found, ", asSnapshot.Kind);
                        return;
                    }
                    m_gcDump = new GCHeapDump(memoryGraph);
                    m_gcDump.DotNetHeapInfo = dotNetHeapInfo;
                }
                else
                {

                    if (FilePath.EndsWith(".gcDump.xml", StringComparison.OrdinalIgnoreCase))
                    {
                        m_gcDump = XmlGcHeapDump.ReadGCHeapDumpFromXml(FilePath);
                    }
                    else
                    {
                        m_gcDump = new GCHeapDump(FilePath);

                        // set it up so we resolve any types 
                        var resolver = new TypeNameSymbolResolver(FilePath, log);
                        m_gcDump.MemoryGraph.ResolveTypeName = resolver.ResolveTypeName;
                    }
                }


                if (m_gcDump.TimeCollected.Ticks != 0)
                {
                    log.WriteLine("GCDump collected on {0}", m_gcDump.TimeCollected);
                }
                else
                {
                    log.WriteLine("GCDump collected from a DMP file no time/machine/process info");
                }

                if (m_gcDump.MachineName != null)
                {
                    log.WriteLine("GCDump collected on Machine {0}", m_gcDump.MachineName);
                }

                if (m_gcDump.ProcessName != null)
                {
                    log.WriteLine("GCDump collected on Process {0} ({1})", m_gcDump.MachineName, m_gcDump.ProcessName, m_gcDump.ProcessID);
                }

                if (m_gcDump.TotalProcessCommit != 0)
                {
                    log.WriteLine("Total Process CommitSize {0:n1} MB Working Set {1:n1} MB", m_gcDump.TotalProcessCommit / 1000000.0, m_gcDump.TotalProcessWorkingSet / 1000000.0);
                }

                if (m_gcDump.CollectionLog != null)
                {
                    log.WriteLine("******************** START OF LOG FILE FROM TIME OF COLLECTION **********************");
                    log.Write(m_gcDump.CollectionLog);
                    log.WriteLine("********************  END OF LOG FILE FROM TIME OF COLLECTION  **********************");
                }

#if false // TODO FIX NOW REMOVE
                using (StreamWriter writer = File.CreateText(Path.ChangeExtension(FilePath, ".rawGraph.xml")))
                {
                    m_gcDump.MemoryGraph.WriteXml(writer);
                }
#endif
            }

            MemoryGraph graph = m_gcDump.MemoryGraph;
            log.WriteLine(
                "Opened Graph {0} Bytes: {1:f3}M NumObjects: {2:f3}K  NumRefs: {3:f3}K Types: {4:f3}K RepresentationSize: {5:f1}M",
                FilePath, graph.TotalSize / 1000000.0, (int)graph.NodeIndexLimit / 1000.0,
                graph.TotalNumberOfReferences / 1000.0, (int)graph.NodeTypeIndexLimit / 1000.0,
                graph.SizeOfGraphDescription() / 1000000.0);
        }

        /// <summary>
        /// These hold stacks which we know they either have an '[not reachable from roots]' or not
        /// </summary>
        private struct UnreachableCacheEntry
        {
            public StackSourceCallStackIndex stack;
            public bool unreachable;
            public bool valid;
        };

        /// <summary>
        /// Returns true if 'stackIdx' is reachable from the roots (that is, it does not have '[not reachable from roots]' as one
        /// of its parent nodes.    'cache' is simply an array used to speed up this process because it remembers the answers for
        /// nodes up the stack that are likely to be used for the next index.   
        /// </summary>
        private static bool IsUnreachable(StackSource memoryStackSource, StackSourceCallStackIndex stackIdx, UnreachableCacheEntry[] cache, int depth)
        {
            if (stackIdx == StackSourceCallStackIndex.Invalid)
            {
                return false;
            }

            int entryIdx = ((int)stackIdx) % cache.Length;
            UnreachableCacheEntry entry = cache[entryIdx];
            if (stackIdx != entry.stack || !entry.valid)
            {
                var callerIdx = memoryStackSource.GetCallerIndex(stackIdx);
                if (callerIdx == StackSourceCallStackIndex.Invalid)
                {
                    var frameIdx = memoryStackSource.GetFrameIndex(stackIdx);
                    var name = memoryStackSource.GetFrameName(frameIdx, false);
                    entry.unreachable = string.Compare(name, "[not reachable from roots]", StringComparison.OrdinalIgnoreCase) == 0;
                }
                else
                {
                    entry.unreachable = IsUnreachable(memoryStackSource, callerIdx, cache, depth + 1);
                }

                entry.stack = stackIdx;
                entry.valid = true;
                cache[entryIdx] = entry;
            }
            return entry.unreachable;
        }

        private static void ComputeUnreachableMemory(StackSource memoryStackSource, out double unreachableMemoryRet, out double totalMemoryRet)
        {
            double unreachableMemory = 0;
            double totalMemory = 0;

            // Make the cache roughly hit every 7 tries.  This keeps memory under control for large heaps
            // but the slowdown because of misses will not be too bad.  
            var cache = new UnreachableCacheEntry[memoryStackSource.SampleIndexLimit / 7 + 1001];
            memoryStackSource.ForEach(delegate (StackSourceSample sample)
            {
                totalMemory += sample.Metric;
                if (IsUnreachable(memoryStackSource, sample.StackIndex, cache, 0))
                {
                    unreachableMemory += sample.Metric;
                }
            });

            unreachableMemoryRet = unreachableMemory;
            totalMemoryRet = totalMemory;
        }

        protected internal GCHeapDump m_gcDump;
        private string m_extraTopStats;
        #endregion
    }
}