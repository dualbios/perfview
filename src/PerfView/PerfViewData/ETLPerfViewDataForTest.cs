using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Utilities;
using Utilities;

namespace PerfView.PerfViewData
{
    class ETLPerfViewDataForTest : PerfViewFile
    {
        public ETLPerfViewDataForTest()
        {
            m_filePath = "C:\\Users\\KOUS\\Downloads\\PerfView\\PerfViewData.etl";
            App.CommandLineArgs = new CommandLineArgs();
        }
        public override string FormatName => throw new NotImplementedException();

        public override string[] FileExtensions => throw new NotImplementedException();

        public override void Close()
        {
            throw new NotImplementedException();
        }

        public override void Open(Window parentWindow, StatusBar worker, Action doAfter = null)
        {
            throw new NotImplementedException();
        }

        public TraceLog GetTraceLog(TextWriter log, Action<bool, int, int> onLostEvents = null)
        {
            //if (m_traceLog != null)
            //{
            //    if (IsUpToDate)
            //    {
            //        return m_traceLog;
            //    }

            //    m_traceLog.Dispose();
            //    m_traceLog = null;
            //}
            var dataFileName = FilePath;
            var options = new TraceLogOptions();
            options.ConversionLog = log;
            if (App.CommandLineArgs.KeepAllEvents)
            {
                options.KeepAllEvents = true;
            }

            var traceEventDispatcherOptions = new TraceEventDispatcherOptions();
            traceEventDispatcherOptions.StartTime = App.CommandLineArgs.StartTime;
            traceEventDispatcherOptions.EndTime = App.CommandLineArgs.EndTime;

            options.MaxEventCount = App.CommandLineArgs.MaxEventCount;
            options.ContinueOnError = App.CommandLineArgs.ContinueOnError;
            options.SkipMSec = App.CommandLineArgs.SkipMSec;
            options.OnLostEvents = onLostEvents;
            options.LocalSymbolsOnly = false;
            options.ShouldResolveSymbols = delegate (string moduleFilePath) { return false; };       // Don't resolve any symbols

            // But if there is a directory called EtwManifests exists, look in there instead. 
            var etwManifestDirPath = Path.Combine(Path.GetDirectoryName(dataFileName), "EtwManifests");
            if (Directory.Exists(etwManifestDirPath))
            {
                options.ExplicitManifestDir = etwManifestDirPath;
            }

            CommandProcessor.UnZipIfNecessary(ref dataFileName, log);

            var etlxFile = dataFileName;
            var cachedEtlxFile = false;
            if (dataFileName.EndsWith(".etl", StringComparison.OrdinalIgnoreCase) || dataFileName.EndsWith(".btl", StringComparison.OrdinalIgnoreCase))
            {
                etlxFile = CacheFiles.FindFile(dataFileName, ".etlx");
                if (!File.Exists(etlxFile))
                {
                    log.WriteLine("Creating ETLX file {0} from {1}", etlxFile, dataFileName);
                    TraceLog.CreateFromEventTraceLogFile(dataFileName, etlxFile, options, traceEventDispatcherOptions);

                    var dataFileSize = "Unknown";
                    if (File.Exists(dataFileName))
                    {
                        dataFileSize = ((new System.IO.FileInfo(dataFileName)).Length / 1000000.0).ToString("n3") + " MB";
                    }

                    log.WriteLine("ETL Size {0} ETLX Size {1:n3} MB", dataFileSize, (new System.IO.FileInfo(etlxFile)).Length / 1000000.0);
                }
                else
                {
                    cachedEtlxFile = true;
                    log.WriteLine("Found a corresponding ETLX file {0}", etlxFile);
                }
            }

            TraceLog m_traceLog = null;
            try
            {
                m_traceLog = new TraceLog(etlxFile);

                // Add some more parser that we would like.  
                new ETWClrProfilerTraceEventParser(m_traceLog);
                new MicrosoftWindowsNDISPacketCaptureTraceEventParser(m_traceLog);
            }
            catch (Exception)
            {
                if (cachedEtlxFile)
                {
                    // Delete the file and try again.  
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


        public Action<Action> OpenImpl(Window parentWindow)
        {
            StringBuilder sb = new StringBuilder();
            TextWriter LogWriter = new StringWriter(sb);
            var tracelog = GetTraceLog(LogWriter, delegate (bool truncated, int numberOfLostEvents, int eventCountAtTrucation)
            {
                //if (!m_notifiedAboutLostEvents)
                //{
                //    HandleLostEvents(parentWindow, truncated, numberOfLostEvents, eventCountAtTrucation, worker);
                //    m_notifiedAboutLostEvents = true;
                //}
            });

            // Warn about possible Win8 incompatibility.  
            //var logVer = tracelog.OSVersion.Major * 10 + tracelog.OSVersion.Minor;
            //if (62 <= logVer)
            //{
            //    var ver = Environment.OSVersion.Version.Major * 10 + Environment.OSVersion.Version.Minor;
            //    if (ver < 62)       // We are decoding on less than windows 8
            //    {
            //        if (!m_notifiedAboutWin8)
            //        {
            //            m_notifiedAboutWin8 = true;
            //            var versionMismatchWarning = "This trace was captured on Window 8 and is being read\r\n" +
            //                                         "on and earlier OS.  If you experience any problems please\r\n" +
            //                                         "read the trace on an Windows 8 OS.";
            //            worker.LogWriter.WriteLine(versionMismatchWarning);
            //            parentWindow.Dispatcher.BeginInvoke((Action)delegate ()
            //            {
            //                MessageBox.Show(parentWindow, versionMismatchWarning, "Log File Version Mismatch", MessageBoxButton.OK);
            //            });
            //        }
            //    }
            //}

            var advanced = new PerfViewTreeGroup("Advanced Group");
            var memory = new PerfViewTreeGroup("Memory Group");
            var obsolete = new PerfViewTreeGroup("Old Group");
            var experimental = new PerfViewTreeGroup("Experimental Group");
            m_Children = new List<PerfViewTreeItem>();

            bool hasCPUStacks = false;
            bool hasDllStacks = false;
            bool hasCSwitchStacks = false;
            bool hasReadyThreadStacks = false;
            bool hasHeapStacks = false;
            bool hasGCAllocationTicks = false;
            bool hasExceptions = false;
            bool hasManagedLoads = false;
            bool hasAspNet = false;
            bool hasIis = false;
            bool hasDiskStacks = false;
            bool hasAnyStacks = false;
            bool hasVirtAllocStacks = false;
            bool hasFileStacks = false;
            bool hasTpl = false;
            bool hasTplStacks = false;
            bool hasGCHandleStacks = false;
            bool hasMemAllocStacks = false;
            bool hasJSHeapDumps = false;
            bool hasDotNetHeapDumps = false;
            bool hasWCFRequests = false;
            bool hasCCWRefCountStacks = false;
            bool hasNetNativeCCWRefCountStacks = false;
            bool hasWindowsRefCountStacks = false;
            bool hasPinObjectAtGCTime = false;
            bool hasObjectUpdate = false;
            bool hasGCEvents = false;
            bool hasProjectNExecutionTracingEvents = false;
            bool hasDefenderEvents = false;
            bool hasTypeLoad = false;
            bool hasAssemblyLoad = false;
            bool hasJIT = false;

            var stackEvents = new List<TraceEventCounts>();
            foreach (var counts in tracelog.Stats)
            {
                var name = counts.EventName;
                if (!hasCPUStacks && name.StartsWith("PerfInfo"))
                {
                    hasCPUStacks = true;                // Even without true stacks we can display something in the stack viewer.  
                }

                if (!hasAspNet && name.StartsWith("AspNetReq"))
                {
                    hasAspNet = true;
                }

                if (!hasIis && name.StartsWith("IIS"))
                {
                    hasIis = true;
                }

                if (counts.ProviderGuid == ApplicationServerTraceEventParser.ProviderGuid)
                {
                    hasWCFRequests = true;
                }

                if (name.StartsWith("JSDumpHeapEnvelope"))
                {
                    hasJSHeapDumps = true;
                }

                if (name.StartsWith("GC/Start"))
                {
                    hasGCEvents = true;
                }

                if (name.StartsWith("GC/BulkNode"))
                {
                    hasDotNetHeapDumps = true;
                }

                if (name.StartsWith("GC/PinObjectAtGCTime"))
                {
                    hasPinObjectAtGCTime = true;
                }

                if (name.StartsWith("GC/BulkSurvivingObjectRanges") || name.StartsWith("GC/BulkMovedObjectRanges"))
                {
                    hasObjectUpdate = true;
                }

                if (counts.ProviderGuid == TplEtwProviderTraceEventParser.ProviderGuid)
                {
                    hasTpl = true;
                }

                if (counts.ProviderGuid == MicrosoftAntimalwareEngineTraceEventParser.ProviderGuid)
                {
                    hasDefenderEvents = true;
                }

                if (name.StartsWith("Method/JittingStarted"))
                {
                    hasJIT = true;
                }
                if (name.StartsWith("TypeLoad/Start"))
                {
                    hasTypeLoad = true;
                }
                if (name.StartsWith("Loader/AssemblyLoad"))
                {
                    hasAssemblyLoad = true;
                }

                if (counts.StackCount > 0)
                {
                    hasAnyStacks = true;
                    if (counts.ProviderGuid == ETWClrProfilerTraceEventParser.ProviderGuid && name.StartsWith("ObjectAllocated"))
                    {
                        hasMemAllocStacks = true;
                    }

                    if (name.StartsWith("GC/SampledObjectAllocation"))
                    {
                        hasMemAllocStacks = true;
                    }

                    if (name.StartsWith("GC/CCWRefCountChange"))
                    {
                        hasCCWRefCountStacks = true;
                    }

                    if (name.StartsWith("TaskCCWRef"))
                    {
                        hasNetNativeCCWRefCountStacks = true;
                    }

                    if (name.StartsWith("Object/CreateHandle"))
                    {
                        hasWindowsRefCountStacks = true;
                    }

                    if (name.StartsWith("Image"))
                    {
                        hasDllStacks = true;
                    }

                    if (name.StartsWith("HeapTrace"))
                    {
                        hasHeapStacks = true;
                    }

                    if (name.StartsWith("Thread/CSwitch"))
                    {
                        hasCSwitchStacks = true;
                    }

                    if (name.StartsWith("GC/AllocationTick"))
                    {
                        hasGCAllocationTicks = true;
                    }

                    if (name.StartsWith("Exception") || name.StartsWith("PageFault/AccessViolation"))
                    {
                        hasExceptions = true;
                    }

                    if (name.StartsWith("GC/SetGCHandle"))
                    {
                        hasGCHandleStacks = true;
                    }

                    if (name.StartsWith("Loader/ModuleLoad"))
                    {
                        hasManagedLoads = true;
                    }

                    if (name.StartsWith("VirtualMem"))
                    {
                        hasVirtAllocStacks = true;
                    }

                    if (name.StartsWith("Dispatcher/ReadyThread"))
                    {
                        hasReadyThreadStacks = true;
                    }

                    if (counts.ProviderGuid == TplEtwProviderTraceEventParser.ProviderGuid)
                    {
                        hasTplStacks = true;
                    }

                    if (name.StartsWith("DiskIO"))
                    {
                        hasDiskStacks = true;
                    }

                    if (name.StartsWith("FileIO"))
                    {
                        hasFileStacks = true;
                    }

                    if (name.StartsWith("MethodEntry"))
                    {
                        hasProjectNExecutionTracingEvents = true;
                    }
                }
            }

            m_Children.Add(new PerfViewTraceInfo(this));
            m_Children.Add(new PerfViewProcesses(this));

            m_Children.Add(new PerfViewStackSource(this, "Processes / Files / Registry") { SkipSelectProcess = true });

            if (hasCPUStacks)
            {
                m_Children.Add(new PerfViewStackSource(this, "CPU"));
                experimental.Children.Add(new AutomatedAnalysisReport(this));
                if (!App.CommandLineArgs.ShowOptimizationTiers &&
                    tracelog.Events.Any(
                        e => e is MethodLoadUnloadTraceDataBase td && td.OptimizationTier != OptimizationTier.Unknown))
                {
                    advanced.Children.Add(new PerfViewStackSource(this, "CPU (with Optimization Tiers)"));
                }
                advanced.Children.Add(new PerfViewStackSource(this, "Processor"));
            }

            if (hasCSwitchStacks)
            {
                if (hasTplStacks)
                {
                    advanced.Children.Add(new PerfViewStackSource(this, "Thread Time"));
                    advanced.Children.Add(new PerfViewStackSource(this, "Thread Time (with Tasks)"));
                    m_Children.Add(new PerfViewStackSource(this, "Thread Time (with StartStop Activities)"));
                }
                else
                {
                    m_Children.Add(new PerfViewStackSource(this, "Thread Time"));
                }

                if (hasReadyThreadStacks)
                {
                    advanced.Children.Add(new PerfViewStackSource(this, "Thread Time (with ReadyThread)"));
                }
            }
            else if (hasCPUStacks && hasTplStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Thread Time (with StartStop Activities) (CPU ONLY)"));
            }

            if (hasDiskStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Disk I/O"));
            }

            if (hasFileStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "File I/O"));
            }

            if (hasHeapStacks)
            {
                memory.Children.Add(new PerfViewStackSource(this, "Net OS Heap Alloc"));
            }

            if (hasVirtAllocStacks)
            {
                memory.Children.Add(new PerfViewStackSource(this, "Net Virtual Alloc"));
                memory.Children.Add(new PerfViewStackSource(this, "Net Virtual Reserve"));
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

            if (hasDllStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Image Load"));
            }

            if (hasManagedLoads)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Managed Load"));
            }

            if (hasExceptions)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Exceptions"));
            }

            if (hasGCHandleStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Pinning"));
            }

            if (hasPinObjectAtGCTime)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Pinning At GC Time"));
            }

            if (hasGCEvents && hasCPUStacks && AppLog.InternalUser)
            {
                memory.Children.Add(new PerfViewStackSource(this, "Server GC"));
            }

            if (hasCCWRefCountStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "CCW Ref Count"));
            }

            if (hasNetNativeCCWRefCountStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, ".NET Native CCW Ref Count"));
            }

            if (hasWindowsRefCountStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Windows Handle Ref Count"));
            }

            if (hasGCHandleStacks && hasMemAllocStacks)
            {
                bool matchingHeapSnapshotExists = GCPinnedObjectAnalyzer.ExistsMatchingHeapSnapshot(FilePath);
                if (matchingHeapSnapshotExists)
                {
                    advanced.Children.Add(new PerfViewStackSource(this, "Heap Snapshot Pinning"));
                    advanced.Children.Add(new PerfViewStackSource(this, "Heap Snapshot Pinned Object Allocation"));
                }
            }

            if ((hasAspNet) || (hasWCFRequests))
            {
                if (hasCPUStacks)
                {
                    obsolete.Children.Add(new PerfViewStackSource(this, "Server Request CPU"));
                }
                if (hasCSwitchStacks)
                {
                    obsolete.Children.Add(new PerfViewStackSource(this, "Server Request Thread Time"));
                }
                if (hasGCAllocationTicks)
                {
                    obsolete.Children.Add(new PerfViewStackSource(this, "Server Request Managed Allocation"));
                }
            }

            if (hasAnyStacks)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Any"));
                if (hasTpl)
                {
                    if (hasCSwitchStacks)
                    {
                        advanced.Children.Add(new PerfViewStackSource(this, "Any Stacks (with Tasks)"));
                        advanced.Children.Add(new PerfViewStackSource(this, "Any Stacks (with StartStop Activities)"));
                        advanced.Children.Add(new PerfViewStackSource(this, "Any StartStopTree"));
                    }
                    advanced.Children.Add(new PerfViewStackSource(this, "Any TaskTree"));
                }
            }

            if (hasAspNet)
            {
                advanced.Children.Add(new PerfViewAspNetStats(this));
                if (hasCPUStacks)
                {
                    var name = "ASP.NET Thread Time";
                    if (hasCSwitchStacks && hasTplStacks)
                    {
                        obsolete.Children.Add(new PerfViewStackSource(this, "ASP.NET Thread Time (with Tasks)"));
                    }
                    else if (!hasCSwitchStacks)
                    {
                        name += " (CPU ONLY)";
                    }

                    obsolete.Children.Add(new PerfViewStackSource(this, name));
                }
            }

            if (hasIis)
            {
                advanced.Children.Add(new PerfViewIisStats(this));
            }

            if (hasProjectNExecutionTracingEvents && AppLog.InternalUser)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Execution Tracing"));
            }

            if (hasDefenderEvents)
            {
                advanced.Children.Add(new PerfViewStackSource(this, "Anti-Malware Real-Time Scan"));
            }

            memory.Children.Add(new PerfViewGCStats(this));

            // TODO currently this is experimental enough that we don't show it publicly.  
            if (AppLog.InternalUser)
            {
                memory.Children.Add(new MemoryAnalyzer(this));
            }

            if (hasJSHeapDumps || hasDotNetHeapDumps)
            {
                memory.Children.Add(new PerfViewHeapSnapshots(this));
            }

            advanced.Children.Add(new PerfViewJitStats(this));

            if (hasJIT || hasAssemblyLoad || hasTypeLoad)
            {
                advanced.Children.Add(new PerfViewRuntimeLoaderStats(this));
            }

            advanced.Children.Add(new PerfViewEventStats(this));

            m_Children.Add(new PerfViewEventSource(this));

            if (0 < memory.Children.Count)
            {
                m_Children.Add(memory);
            }

            if (0 < advanced.Children.Count)
            {
                m_Children.Add(advanced);
            }

            if (0 < obsolete.Children.Count)
            {
                m_Children.Add(obsolete);
            }

            if (AppLog.InternalUser && 0 < experimental.Children.Count)
            {
                m_Children.Add(experimental);
            }

            return null;
        }

    }
}
