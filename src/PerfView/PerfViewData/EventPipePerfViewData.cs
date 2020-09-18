using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Windows;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.EventPipe;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Utilities;
using Utilities;
using EventSource = EventSources.EventSource;

namespace PerfView.PerfViewData
{
    public partial class EventPipePerfViewData : PerfViewFile
    {
        public override string FormatName => "EventPipe";

        public override string[] FileExtensions => new string[] { ".netperf", ".netperf.zip", ".nettrace" };

        private string m_extraTopStats;

        protected internal override EventSource OpenEventSourceImpl(TextWriter log)
        {
            var traceLog = GetTraceLog(log);
            return new ETWEventSource(traceLog);
        }

        public override Action<Action> OpenImpl(Window parentWindow, StatusBar worker)
        {
            // Open the file.
            m_traceLog = GetTraceLog(worker.LogWriter, delegate (bool truncated, int numberOfLostEvents, int eventCountAtTrucation)
            {
                if (!m_notifiedAboutLostEvents)
                {
                    HandleLostEvents(parentWindow, truncated, numberOfLostEvents, eventCountAtTrucation, worker);
                    m_notifiedAboutLostEvents = true;
                }
            });

            bool hasGC = false;
            bool hasJIT = false;
            bool hasAnyStacks = false;
            bool hasDotNetHeapDumps = false;
            bool hasGCAllocationTicks = false;
            bool hasObjectUpdate = false;
            bool hasMemAllocStacks = false;
            bool hasTypeLoad = false;
            bool hasAssemblyLoad = false;
            if (m_traceLog != null)
            {
                foreach (TraceEventCounts eventStats in m_traceLog.Stats)
                {
                    if (eventStats.StackCount > 0)
                    {
                        hasAnyStacks = true;
                    }

                    if (eventStats.EventName.StartsWith("GC/Start"))
                    {
                        hasGC = true;
                    }
                    else if (eventStats.EventName.StartsWith("Method/JittingStarted"))
                    {
                        hasJIT = true;
                    }
                    else if (eventStats.EventName.StartsWith("GC/BulkNode"))
                    {
                        hasDotNetHeapDumps = true;
                    }
                    else if (eventStats.EventName.StartsWith("GC/AllocationTick"))
                    {
                        hasGCAllocationTicks = true;
                    }
                    if (eventStats.EventName.StartsWith("GC/BulkSurvivingObjectRanges") || eventStats.EventName.StartsWith("GC/BulkMovedObjectRanges"))
                    {
                        hasObjectUpdate = true;
                    }
                    if (eventStats.EventName.StartsWith("GC/SampledObjectAllocation"))
                    {
                        hasMemAllocStacks = true;
                    }
                    else if (eventStats.EventName.StartsWith("TypeLoad/Start"))
                    {
                        hasTypeLoad = true;
                    }
                    else if (eventStats.EventName.StartsWith("Loader/AssemblyLoad"))
                    {
                        hasAssemblyLoad = true;
                    }
                }
            }

            m_Children = new List<PerfViewTreeItem>();
            var advanced = new PerfViewTreeGroup("Advanced Group");
            var memory = new PerfViewTreeGroup("Memory Group");

            if (m_traceLog != null)
            {
                m_Children.Add(new PerfViewEventSource(this));
                m_Children.Add(new PerfViewEventStats(this));

                if (hasAnyStacks)
                {
                    m_Children.Add(new PerfViewStackSource(this, "Thread Time (with StartStop Activities)"));
                    m_Children.Add(new PerfViewStackSource(this, "Any"));
                }

                if (hasGC)
                {
                    memory.AddChild(new PerfViewGCStats(this));
                }

                if (hasGCAllocationTicks)
                {
                    if (hasObjectUpdate)
                    {
                        memory.Children.Add(new PerfViewStackSource(this, "GC Heap Net Mem (Coarse Sampling)"));
                        memory.Children.Add(new PerfViewStackSource(this, "Gen 2 Object Deaths (Coarse Sampling)"));
                    }
                    memory.Children.Add(new PerfViewStackSource(this, "GC Heap Alloc Ignore Free (Coarse Sampling)"));
                }
                if (hasMemAllocStacks)
                {
                    memory.Children.Add(new PerfViewStackSource(this, "GC Heap Net Mem"));
                    memory.Children.Add(new PerfViewStackSource(this, "GC Heap Alloc Ignore Free"));
                    memory.Children.Add(new PerfViewStackSource(this, "Gen 2 Object Deaths"));
                }

                if (hasDotNetHeapDumps)
                    memory.AddChild(new PerfViewHeapSnapshots(this));

                if (hasJIT)
                {
                    advanced.AddChild(new PerfViewJitStats(this));
                }

                if (hasJIT || hasTypeLoad || hasAssemblyLoad)
                {
                    advanced.AddChild(new PerfViewRuntimeLoaderStats(this));
                }
            }

            if (memory.Children.Count > 0)
            {
                m_Children.Add(memory);
            }

            if (advanced.Children.Count > 0)
            {
                m_Children.Add(advanced);
            }

            return null;
        }

        protected internal override StackSource OpenStackSourceImpl(string streamName, TextWriter log, double startRelativeMSec = 0, double endRelativeMSec = double.PositiveInfinity, Predicate<TraceEvent> predicate = null)
        {
            switch (streamName)
            {
                case "Any":
                {
                    var eventLog = GetTraceLog(log);

                    var stackSource = new MutableTraceEventStackSource(eventLog);
                    // EventPipe currently only has managed code stacks.
                    stackSource.OnlyManagedCodeStacks = true;

                    stackSource.ShowUnknownAddresses = App.CommandLineArgs.ShowUnknownAddresses;
                    stackSource.ShowOptimizationTiers = App.CommandLineArgs.ShowOptimizationTiers;

                    TraceEvents events = eventLog.Events;

                    if (startRelativeMSec != 0 || endRelativeMSec != double.PositiveInfinity)
                    {
                        events = events.FilterByTime(startRelativeMSec, endRelativeMSec);
                    }

                    var eventSource = events.GetSource();
                    var sample = new StackSourceSample(stackSource);

                    eventSource.AllEvents += (data) =>
                    {
                        var callStackIdx = data.CallStackIndex();
                        if (callStackIdx != CallStackIndex.Invalid)
                        {
                            StackSourceCallStackIndex stackIndex = stackSource.GetCallStack(callStackIdx, data);

                            var asClrThreadSample = data as ClrThreadSampleTraceData;
                            if (asClrThreadSample != null)
                            {
                                stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern("Type: " + asClrThreadSample.Type), stackIndex);
                                goto ADD_EVENT_FRAME;
                            }

                            var asAllocTick = data as GCAllocationTickTraceData;
                            if (asAllocTick != null)
                            {
                                var frameIdx = stackSource.Interner.FrameIntern("EventData Kind " + asAllocTick.AllocationKind);
                                stackIndex = stackSource.Interner.CallStackIntern(frameIdx, stackIndex);

                                frameIdx = stackSource.Interner.FrameIntern("EventData Size " + asAllocTick.AllocationAmount64);
                                stackIndex = stackSource.Interner.CallStackIntern(frameIdx, stackIndex);

                                var typeName = asAllocTick.TypeName;
                                if (string.IsNullOrEmpty(typeName))
                                {
                                    typeName = "TypeId 0x" + asAllocTick.TypeID;
                                }

                                frameIdx = stackSource.Interner.FrameIntern("EventData TypeName " + typeName);
                                stackIndex = stackSource.Interner.CallStackIntern(frameIdx, stackIndex);
                                goto ADD_EVENT_FRAME;
                            }

                            // Tack on event nam
                            ADD_EVENT_FRAME:
                            var eventNodeName = "Event " + data.ProviderName + "/" + data.EventName;
                            stackIndex = stackSource.Interner.CallStackIntern(stackSource.Interner.FrameIntern(eventNodeName), stackIndex);
                            // Add sample
                            sample.StackIndex = stackIndex;
                            sample.TimeRelativeMSec = data.TimeStampRelativeMSec;
                            sample.Metric = 1;
                            stackSource.AddSample(sample);
                        }
                    };
                    eventSource.Process();

                    stackSource.DoneAddingSamples();
                    return stackSource;
                }
                case "Thread Time (with StartStop Activities)":
                {
                    var eventLog = GetTraceLog(log);

                    var startStopSource = new MutableTraceEventStackSource(eventLog);
                    // EventPipe currently only has managed code stacks.
                    startStopSource.OnlyManagedCodeStacks = true;

                    var computer = new SampleProfilerThreadTimeComputer(eventLog, App.GetSymbolReader(eventLog.FilePath));
                    computer.GenerateThreadTimeStacks(startStopSource);

                    return startStopSource;
                }
                case "GC Heap Alloc Ignore Free":
                {
                    var eventLog = GetTraceLog(log);
                    var eventSource = eventLog.Events.GetSource();
                    var stackSource = new MutableTraceEventStackSource(eventLog);
                    var sample = new StackSourceSample(stackSource);

                    var gcHeapSimulators = new GCHeapSimulators(eventLog, eventSource, stackSource, log);
                    gcHeapSimulators.OnNewGCHeapSimulator = delegate (GCHeapSimulator newHeap)
                    {
                        newHeap.OnObjectCreate += delegate (UInt64 objAddress, GCHeapSimulatorObject objInfo)
                        {
                            sample.Metric = objInfo.RepresentativeSize;
                            sample.Count = objInfo.RepresentativeSize / objInfo.Size;                                               // We guess a count from the size.  
                            sample.TimeRelativeMSec = objInfo.AllocationTimeRelativeMSec;
                            sample.StackIndex = stackSource.Interner.CallStackIntern(objInfo.ClassFrame, objInfo.AllocStack);        // Add the type as a pseudo frame.  
                            stackSource.AddSample(sample);
                            return true;
                        };
                    };
                    eventSource.Process();
                    stackSource.DoneAddingSamples();

                    return stackSource;
                }
                default:
                {
                    var eventLog = GetTraceLog(log);
                    var eventSource = eventLog.Events.GetSource();
                    var stackSource = new MutableTraceEventStackSource(eventLog);
                    var sample = new StackSourceSample(stackSource);

                    if (streamName.StartsWith("GC Heap Net Mem"))
                    {
                        var gcHeapSimulators = new GCHeapSimulators(eventLog, eventSource, stackSource, log);
                        if (streamName == "GC Heap Net Mem (Coarse Sampling)")
                        {
                            gcHeapSimulators.UseOnlyAllocTicks = true;
                            m_extraTopStats = "Sampled only 100K bytes";
                        }

                        gcHeapSimulators.OnNewGCHeapSimulator = delegate (GCHeapSimulator newHeap)
                        {
                            newHeap.OnObjectCreate += delegate (UInt64 objAddress, GCHeapSimulatorObject objInfo)
                            {
                                sample.Metric = objInfo.RepresentativeSize;
                                sample.Count = objInfo.RepresentativeSize / objInfo.Size;                                                // We guess a count from the size.  
                                sample.TimeRelativeMSec = objInfo.AllocationTimeRelativeMSec;
                                sample.StackIndex = stackSource.Interner.CallStackIntern(objInfo.ClassFrame, objInfo.AllocStack);        // Add the type as a pseudo frame.  
                                stackSource.AddSample(sample);
                                return true;
                            };
                            newHeap.OnObjectDestroy += delegate (double time, int gen, UInt64 objAddress, GCHeapSimulatorObject objInfo)
                            {
                                sample.Metric = -objInfo.RepresentativeSize;
                                sample.Count = -(objInfo.RepresentativeSize / objInfo.Size);                                            // We guess a count from the size.  
                                sample.TimeRelativeMSec = time;
                                sample.StackIndex = stackSource.Interner.CallStackIntern(objInfo.ClassFrame, objInfo.AllocStack);       // We remove the same stack we added at alloc.  
                                stackSource.AddSample(sample);
                            };

                            newHeap.OnGC += delegate (double time, int gen)
                            {
                                sample.Metric = float.Epsilon;
                                sample.Count = 1;
                                sample.TimeRelativeMSec = time;
                                StackSourceCallStackIndex processStack = stackSource.GetCallStackForProcess(newHeap.Process);
                                StackSourceFrameIndex gcFrame = stackSource.Interner.FrameIntern("GC Occured Gen(" + gen + ")");
                                sample.StackIndex = stackSource.Interner.CallStackIntern(gcFrame, processStack);
                                stackSource.AddSample(sample);
                            };
                        };
                        eventSource.Process();
                        stackSource.DoneAddingSamples();
                    }
                    else if (streamName.StartsWith("Gen 2 Object Deaths"))
                    {
                        var gcHeapSimulators = new GCHeapSimulators(eventLog, eventSource, stackSource, log);

                        if (streamName == "Gen 2 Object Deaths (Coarse Sampling)")
                        {
                            gcHeapSimulators.UseOnlyAllocTicks = true;
                            m_extraTopStats = "Sampled only 100K bytes";
                        }

                        gcHeapSimulators.OnNewGCHeapSimulator = delegate (GCHeapSimulator newHeap)
                        {
                            newHeap.OnObjectDestroy += delegate (double time, int gen, UInt64 objAddress, GCHeapSimulatorObject objInfo)
                            {
                                if (2 <= gen)
                                {
                                    sample.Metric = objInfo.RepresentativeSize;
                                    sample.Count = (objInfo.RepresentativeSize / objInfo.Size);                                         // We guess a count from the size.  
                                    sample.TimeRelativeMSec = objInfo.AllocationTimeRelativeMSec;
                                    sample.StackIndex = stackSource.Interner.CallStackIntern(objInfo.ClassFrame, objInfo.AllocStack);
                                    stackSource.AddSample(sample);
                                }
                            };

                            newHeap.OnGC += delegate (double time, int gen)
                            {
                                sample.Metric = float.Epsilon;
                                sample.Count = 1;
                                sample.TimeRelativeMSec = time;
                                StackSourceCallStackIndex processStack = stackSource.GetCallStackForProcess(newHeap.Process);
                                StackSourceFrameIndex gcFrame = stackSource.Interner.FrameIntern("GC Occured Gen(" + gen + ")");
                                sample.StackIndex = stackSource.Interner.CallStackIntern(gcFrame, processStack);
                                stackSource.AddSample(sample);
                            };
                        };

                        eventSource.Process();
                        stackSource.DoneAddingSamples();
                    }
                    else if (streamName == "GC Heap Alloc Ignore Free (Coarse Sampling)")
                    {
                        TypeNameSymbolResolver typeNameSymbolResolver = new TypeNameSymbolResolver(FilePath, log);

                        bool seenBadAllocTick = false;

                        eventSource.Clr.GCAllocationTick += delegate (GCAllocationTickTraceData data)
                        {
                            sample.TimeRelativeMSec = data.TimeStampRelativeMSec;

                            var stackIndex = stackSource.GetCallStack(data.CallStackIndex(), data);

                            var typeName = data.TypeName;
                            if (string.IsNullOrEmpty(typeName))
                            {
                                // Attempt to resolve the type name.
                                TraceLoadedModule module = data.Process().LoadedModules.GetModuleContainingAddress(data.TypeID, data.TimeStampRelativeMSec);
                                if (module != null)
                                {
                                    // Resolve the type name.
                                    typeName = typeNameSymbolResolver.ResolveTypeName((int)(data.TypeID - module.ModuleFile.ImageBase), module.ModuleFile, TypeNameSymbolResolver.TypeNameOptions.StripModuleName);
                                }
                            }

                            if (typeName != null && typeName.Length > 0)
                            {
                                var nodeIndex = stackSource.Interner.FrameIntern("Type " + typeName);
                                stackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackIndex);
                            }

                            sample.Metric = data.GetAllocAmount(ref seenBadAllocTick);

                            if (data.AllocationKind == GCAllocationKind.Large)
                            {

                                var nodeIndex = stackSource.Interner.FrameIntern("LargeObject");
                                stackIndex = stackSource.Interner.CallStackIntern(nodeIndex, stackIndex);
                            }

                            sample.StackIndex = stackIndex;
                            stackSource.AddSample(sample);
                        };
                        eventSource.Process();
                        m_extraTopStats = "Sampled only 100K bytes";
                    }
                    else
                    {
                        return null;
                    }

                    return stackSource;
                }
            }
        }

        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            ConfigureAsEtwStackWindow(stackWindow, false, true, true, false);
            if (stackSourceName.Contains("(with Tasks)") || stackSourceName.Contains("(with StartStop Activities)"))
            {
                var taskFoldPat = "^STARTING TASK";
                stackWindow.FoldRegExTextBox.Items.Add(taskFoldPat);
                stackWindow.FoldRegExTextBox.Text = taskFoldPat;

                var excludePat = "LAST_BLOCK";
                stackWindow.ExcludeRegExTextBox.Items.Add(excludePat);
                stackWindow.ExcludeRegExTextBox.Text = excludePat;
            }

            if (stackSourceName.Contains("Thread Time"))
            {
                stackWindow.ScalingPolicy = ScalingPolicyKind.TimeMetric;
            }

            if (stackSourceName.StartsWith("GC Heap Net Mem") || stackSourceName.StartsWith("GC Heap Alloc Ignore Free"))
            {
                stackWindow.ComputeMaxInTopStats = true;
            }

            if (m_extraTopStats != null)
            {
                stackWindow.ExtraTopStats += " " + m_extraTopStats;
            }
        }

        public override void Close()
        {
            if (m_traceLog != null)
            {
                m_traceLog.Dispose();
                m_traceLog = null;
            }
            base.Close();
        }

        public TraceLog GetTraceLog(TextWriter log, Action<bool, int, int> onLostEvents = null)
        {
            if (m_traceLog != null)
            {
                if (IsUpToDate)
                {
                    return m_traceLog;
                }

                m_traceLog.Dispose();
                m_traceLog = null;
            }
            else if (m_noTraceLogInfo)
            {
                return null;
            }

            var dataFileName = FilePath;
            UnZipIfNecessary(ref dataFileName);

            var options = new TraceLogOptions();
            options.ConversionLog = log;
            if (App.CommandLineArgs.KeepAllEvents)
            {
                options.KeepAllEvents = true;
            }

            options.MaxEventCount = App.CommandLineArgs.MaxEventCount;
            options.ContinueOnError = App.CommandLineArgs.ContinueOnError;
            options.SkipMSec = App.CommandLineArgs.SkipMSec;
            options.OnLostEvents = onLostEvents;
            options.LocalSymbolsOnly = false;
            options.ShouldResolveSymbols = delegate (string moduleFilePath) { return false; };       // Don't resolve any symbols

            // Generate the etlx file path / name.
            string etlxFile = CacheFiles.FindFile(dataFileName, ".etlx");
            bool isCachedEtlx = false;
            if (!File.Exists(etlxFile) || File.GetLastWriteTimeUtc(etlxFile) < File.GetLastWriteTimeUtc(dataFileName))
            {
                FileUtilities.ForceDelete(etlxFile);
                log.WriteLine("Creating ETLX file {0} from {1}", etlxFile, dataFileName);
                try
                {
                    TraceLog.CreateFromEventPipeDataFile(dataFileName, etlxFile, options);
                }
                catch (Exception e)
                {
                    log.WriteLine("Error: Exception EventPipe conversion: {0}", e.ToString());
                    log.WriteLine("[Error: exception while opening EventPipe data.]");

                    Debug.Assert(m_traceLog == null);
                    m_noTraceLogInfo = true;
                    return m_traceLog;
                }
            }
            else
            {
                isCachedEtlx = true;
            }

            var dataFileSize = "Unknown";
            if (File.Exists(dataFileName))
            {
                dataFileSize = ((new System.IO.FileInfo(dataFileName)).Length / 1000000.0).ToString("n3") + " MB";
            }

            log.WriteLine("ETL Size {0} ETLX Size {1:n3} MB", dataFileSize, (new System.IO.FileInfo(etlxFile)).Length / 1000000.0);

            // Open the ETLX file. 
            try
            {
                m_traceLog = new TraceLog(etlxFile);
            }
            catch (Exception)
            {
                if (isCachedEtlx)
                {
                    //  Delete the file and try again.
                    FileUtilities.ForceDelete(etlxFile);
                    if (!File.Exists(etlxFile))
                    {
                        return GetTraceLog(log, onLostEvents);
                    }
                }
                throw;
            }

            m_utcLastWriteAtOpen = File.GetLastWriteTimeUtc(FilePath);
            if (App.CommandLineArgs.UnsafePDBMatch)
            {
                m_traceLog.CodeAddresses.UnsafePDBMatching = true;
            }

            if (m_traceLog.Truncated)   // Warn about truncation.  
            {
                GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    MessageBox.Show("The ETL file was too big to convert and was truncated.\r\nSee log for details", "Log File Truncated", MessageBoxButton.OK);
                });
            }
            return m_traceLog;
        }

        private void UnZipIfNecessary(ref string inputFileName)
        {
            string extension = Path.GetExtension(inputFileName);
            string rest = Path.GetFileNameWithoutExtension(inputFileName);
            if (string.Compare(extension, ".zip", StringComparison.OrdinalIgnoreCase) == 0)
            {
                string subExtension = Path.GetExtension(rest);
                if (subExtension.Length > 0)
                {
                    string unzippedFile = CacheFiles.FindFile(inputFileName, subExtension);
                    if (File.Exists(unzippedFile) && File.GetLastWriteTimeUtc(inputFileName) <= File.GetLastWriteTimeUtc(unzippedFile))
                    {
                        inputFileName = unzippedFile;
                        return;
                    }

                    using (var zipArchive = ZipFile.OpenRead(inputFileName))
                    {
                        int count = zipArchive.Entries.Count;
                        foreach (var entry in zipArchive.Entries)
                        {
                            if (zipArchive.Entries.Count == 1 || entry.FullName.EndsWith(subExtension, StringComparison.OrdinalIgnoreCase))
                            {
                                entry.ExtractToFile(unzippedFile, true);
                                File.SetLastWriteTime(unzippedFile, DateTime.Now); // touch the file. 
                                break;
                            }
                        }
                    }
                    inputFileName = unzippedFile;
                }
            }
        }

        public TraceLog TryGetTraceLog() { return m_traceLog; }

        #region Private

        private void HandleLostEvents(Window parentWindow, bool truncated, int numberOfLostEvents, int eventCountAtTrucation, StatusBar worker)
        {
            string warning;
            if (!truncated)
            {
                warning = "WARNING: There were " + numberOfLostEvents + " lost events in the trace.\r\n" +
                          "Some analysis might be invalid.";
            }
            else
            {
                warning = "WARNING: The ETLX file was truncated at " + eventCountAtTrucation + " events.\r\n" +
                          "This is to keep the ETLX file size under 4GB, however all rundown events are processed.\r\n" +
                          "Use /SkipMSec:XXX after clearing the cache (File->Clear Temp Files) to see the later parts of the file.\r\n" +
                          "See log for more details.";
            }

            MessageBoxResult result = MessageBoxResult.None;
            parentWindow.Dispatcher.BeginInvoke((Action)delegate ()
            {
                result = MessageBox.Show(parentWindow, warning, "Lost Events", MessageBoxButton.OKCancel);
                worker.LogWriter.WriteLine(warning);
                if (result != MessageBoxResult.OK)
                {
                    worker.AbortWork();
                }
            });
        }

        private TraceLog m_traceLog;
        private bool m_noTraceLogInfo;
        private bool m_notifiedAboutLostEvents;
        #endregion
    }
}