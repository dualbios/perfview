using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Stacks;
using PerfEventView.Utils;
using PerfEventView.Utils.Process;

namespace EventView.FileFormats.EtlPerf
{
    public class ETLPerfFileFormat : IFileFormat
    {
        private readonly IEtlPerfPartFactory _etlPerfPartFactory;

        public string FormatName { get { return "ETW"; } }
        public string[] FileExtensions { get { return new string[] { ".btl", ".etl", ".etlx", ".etl.zip", ".vspx" }; } }

        public IList<IFilePart> FileParts { get; private set; }

        private DateTime UtcLastWriteAtOpen
        {
            get;
            set;
        }

        private TraceLog m_traceLog;

        public ETLPerfFileFormat()
        {
        }

        public async Task ParseAsync(string fileName)
        {
            StringBuilder stringBuilder = new StringBuilder(0);
            TextWriter logWriter = new StringWriter(stringBuilder);
            CommandLineArgs args = new CommandLineArgs();

            

            var tracelog = await Task.Run(()=>GetTraceLog(fileName, args, logWriter, delegate (bool truncated, int numberOfLostEvents, int eventCountAtTrucation)
            {
                //if (!m_notifiedAboutLostEvents)
                //{
                //    HandleLostEvents(parentWindow, truncated, numberOfLostEvents, eventCountAtTrucation, worker);
                //    m_notifiedAboutLostEvents = true;
                //}
            }));

            // Warn about possible Win8 incompatibility.
            //var logVer = tracelog.OSVersion.Major * 10 + tracelog.OSVersion.Minor;
            //if (62 <= logVer)
            //{
            //    var ver = Environment.OSVersion.Version.Major * 10 + Environment.OSVersion.Version.Minor;
            //    if (ver < 62)       // We are decoding on less than windows 8
            //    {
            //        //if (!m_notifiedAboutWin8)
            //        //{
            //        //    m_notifiedAboutWin8 = true;
            //        var versionMismatchWarning = "This trace was captured on Window 8 and is being read\r\n" +
            //                                     "on and earlier OS.  If you experience any problems please\r\n" +
            //                                     "read the trace on an Windows 8 OS.";
            //        logWriter.WriteLine(versionMismatchWarning);
            //        throw new Exception(versionMismatchWarning);
            //        //parentWindow.Dispatcher.BeginInvoke((Action)delegate ()
            //        //{
            //        //    MessageBox.Show(parentWindow, versionMismatchWarning, "Log File Version Mismatch", MessageBoxButton.OK);
            //        //});
            //        //}
            //    }
            //}

            //var advanced = new PerfViewTreeGroup("Advanced Group");
            //var memory = new PerfViewTreeGroup("Memory Group");
            //var obsolete = new PerfViewTreeGroup("Old Group");
            //var experimental = new PerfViewTreeGroup("Experimental Group");
            FileParts = new List<IFilePart>();

            EtlPerfFileStats fileStats = _etlPerfPartFactory.CreateStats(tracelog.Stats);

            //m_Children.Add(new PerfViewTraceInfo(this));
            //m_Children.Add(new PerfViewProcesses(this));

            FileParts.Clear();
            foreach (IEtlFilePart etlFilePart in _etlPerfPartFactory.GetParts(fileStats))
            {
                await etlFilePart.Init(this, tracelog);
                FileParts.Add(etlFilePart);
            }

            //m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Processes / Files / Registry") { SkipSelectProcess = true });

            ////if (hasCPUStacks)
            ////{
            ////    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "CPU"));
            ////    experimental.Children.Add(new AutomatedAnalysisReport(this));
            ////    if (!commandLineArgs.ShowOptimizationTiers &&
            ////        tracelog.Events.Any(
            ////            e => e is MethodLoadUnloadTraceDataBase td && td.OptimizationTier != OptimizationTier.Unknown))
            ////    {
            ////        m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "CPU (with Optimization Tiers)"));
            ////    }
            ////    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Processor"));
            ////}

            ////if (hasCSwitchStacks)
            ////{
            ////    if (hasTplStacks)
            ////    {
            ////        m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Thread Time"));
            ////        m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Thread Time (with Tasks)"));
            ////        m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Thread Time (with StartStop Activities)"));
            ////    }
            ////    else
            ////    {
            ////        m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Thread Time"));
            ////    }

            ////    if (hasReadyThreadStacks)
            ////    {
            ////        m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Thread Time (with ReadyThread)"));
            ////    }
            ////}
            ////else if (hasCPUStacks && hasTplStacks)
            ////{
            ////    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Thread Time (with StartStop Activities) (CPU ONLY)"));
            ////}

            ////if (hasDiskStacks)
            ////{
            ////    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Disk I/O"));
            ////}

            ////if (hasFileStacks)
            ////{
            ////    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "File I/O"));
            ////}

            ////if (hasHeapStacks)
            ////{
            ////    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Memory Group", "Net OS Heap Alloc"));
            ////}

            ////if (hasVirtAllocStacks)
            ////{
            ////    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Memory Group", "Net Virtual Alloc"));
            ////    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Memory Group", "Net Virtual Reserve"));
            ////}

            ////if (hasGCAllocationTicks)
            ////{
            ////    if (hasObjectUpdate)
            ////    {
            ////        m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Memory Group", "GC Heap Net Mem (Coarse Sampling)"));
            ////        m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Memory Group", "Gen 2 Object Deaths (Coarse Sampling)"));
            ////    }
            ////    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Memory Group", "GC Heap Alloc Ignore Free (Coarse Sampling)"));
            ////}

            ////if (hasMemAllocStacks)
            ////{
            ////    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Memory Group", "GC Heap Net Mem"));
            ////    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Memory Group", "GC Heap Alloc Ignore Free"));
            ////    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Memory Group", "Gen 2 Object Deaths"));
            ////}

            ////if (hasDllStacks)
            ////{
            ////    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Image Load"));
            ////}

            ////if (hasManagedLoads)
            ////{
            ////    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Managed Load"));
            ////}

            ////if (hasExceptions)
            ////{
            ////    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Exceptions"));
            ////}

            ////if (hasGCHandleStacks)
            ////{
            ////    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Pinning"));
            ////}

            //if (hasPinObjectAtGCTime)
            //{
            //    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Pinning At GC Time"));
            //}

            //if (hasGCEvents && hasCPUStacks && AppLog.InternalUser)
            //{
            //    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Server GC"));
            //}

            //if (hasCCWRefCountStacks)
            //{
            //    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "CCW Ref Count"));
            //}

            //if (hasNetNativeCCWRefCountStacks)
            //{
            //    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, ".NET Native CCW Ref Count"));
            //}

            //if (hasWindowsRefCountStacks)
            //{
            //    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Windows Handle Ref Count"));
            //}

            ////if (hasGCHandleStacks && hasMemAllocStacks)
            ////{
            ////    bool matchingHeapSnapshotExists = GCPinnedObjectAnalyzer.ExistsMatchingHeapSnapshot(FilePath);
            ////    if (matchingHeapSnapshotExists)
            ////    {
            ////        m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Heap Snapshot Pinning"));
            ////        m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Heap Snapshot Pinned Object Allocation"));
            ////    }
            ////}

            //if ((hasAspNet) || (hasWCFRequests))
            //{
            //    if (hasCPUStacks)
            //    {
            //        m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Old Group", "Server Request CPU"));
            //    }
            //    if (hasCSwitchStacks)
            //    {
            //        m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Old Group", "Server Request Thread Time"));
            //    }
            //    if (hasGCAllocationTicks)
            //    {
            //        m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Old Group", "Server Request Managed Allocation"));
            //    }
            //}

            //if (hasAnyStacks)
            //{
            //    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Any"));
            //    if (hasTpl)
            //    {
            //        if (hasCSwitchStacks)
            //        {
            //            m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Any Stacks (with Tasks)"));
            //            m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Any Stacks (with StartStop Activities)"));
            //            m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Any StartStopTree"));
            //        }
            //        m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Any TaskTree"));
            //    }
            //}

            //if (hasAspNet)
            //{
            //    m_Children.Add(new PerfViewAspNetStats(this));
            //    if (hasCPUStacks)
            //    {
            //        var name = "ASP.NET Thread Time";
            //        if (hasCSwitchStacks && hasTplStacks)
            //        {
            //            m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Old Group", "ASP.NET Thread Time (with Tasks)"));
            //        }
            //        else if (!hasCSwitchStacks)
            //        {
            //            name += " (CPU ONLY)";
            //        }

            //        m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Old Group", name));
            //    }
            //}

            //if (hasIis)
            //{
            //    m_Children.Add(new PerfViewIisStats(this));
            //}

            //if (hasProjectNExecutionTracingEvents && AppLog.InternalUser)
            //{
            //    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Execution Tracing"));
            //}

            //if (hasDefenderEvents)
            //{
            //    m_Children.Add(new PerfViewStackSourceFilePart_Temp(this, "Anti-Malware Real-Time Scan"));
            //}

            //m_Children.Add(new PerfViewGCStats(this));

            //// TODO currently this is experimental enough that we don't show it publicly.
            //if (AppLog.InternalUser)
            //{
            //    m_Children.Add(new MemoryAnalyzer(this));
            //}

            //if (hasJSHeapDumps || hasDotNetHeapDumps)
            //{
            //    m_Children.Add(new PerfViewHeapSnapshots(this));
            //}

            //m_Children.Add(new PerfViewJitStats(this));

            //if (hasJIT || hasAssemblyLoad || hasTypeLoad)
            //{
            //    m_Children.Add(new PerfViewRuntimeLoaderStats(this));
            //}

            ////m_Children.Add(new PerfViewEventStats(this));

            ////m_Children.Add(new PerfViewEventSource(this));

            ////if (0 < m_Children.Count)
            ////{
            ////    m_Children.Add(memory);
            ////}

            ////if (0 < m_Children.Count)
            ////{
            ////    m_Children.Add(advanced);
            ////}

            ////if (0 < m_Children.Count)
            ////{
            ////    m_Children.Add(obsolete);
            ////}

            ////if (AppLog.InternalUser && 0 < experimental.Children.Count)
            ////{
            ////    m_Children.Add(experimental);
            ////}

            ////return Task.CompletedTask;
        }

        public TraceLog GetTraceLog(string filePath, CommandLineArgs commandLineArgs, TextWriter log, Action<bool, int, int> onLostEvents = null)
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
            var dataFileName = filePath;
            var options = new TraceLogOptions();
            options.ConversionLog = log;
            if (commandLineArgs.KeepAllEvents)
            {
                options.KeepAllEvents = true;
            }

            var traceEventDispatcherOptions = new TraceEventDispatcherOptions();
            traceEventDispatcherOptions.StartTime = commandLineArgs.StartTime;
            traceEventDispatcherOptions.EndTime = commandLineArgs.EndTime;

            options.MaxEventCount = commandLineArgs.MaxEventCount;
            options.ContinueOnError = commandLineArgs.ContinueOnError;
            options.SkipMSec = commandLineArgs.SkipMSec;
            options.OnLostEvents = onLostEvents;
            options.LocalSymbolsOnly = false;
            options.ShouldResolveSymbols = delegate (string moduleFilePath)
            { return false; };       // Don't resolve any symbols

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

            try
            {
                m_traceLog = new TraceLog(etlxFile);

                // Add some more parser that we would like.
                new Microsoft.Diagnostics.Tracing.Parsers.ETWClrProfilerTraceEventParser(m_traceLog);
                new MicrosoftWindowsNDISPacketCaptureTraceEventParser(m_traceLog);
            }
            catch (Exception)
            {
                if (cachedEtlxFile)
                {
                    // Delete the file and try again.
                    object p = EventView.Utils.FileUtilities.ForceDelete(etlxFile);
                    if (!File.Exists(etlxFile))
                    {
                        return GetTraceLog(filePath, commandLineArgs, log, onLostEvents);
                    }
                }
                throw;
            }

            UtcLastWriteAtOpen = File.GetLastWriteTimeUtc(filePath);
            if (commandLineArgs.UnsafePDBMatch)
            {
                m_traceLog.CodeAddresses.UnsafePDBMatching = true;
            }

            if (m_traceLog.Truncated)   // Warn about truncation.
            {
                throw new Exception("The ETL file was too big to convert and was truncated.\r\nSee log for details");
                //GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                //{
                //    MessageBox.Show("The ETL file was too big to convert and was truncated.\r\nSee log for details", "Log File Truncated", MessageBoxButton.OK);
                //});
            }
            return m_traceLog;
        }

        public List<IProcess> GetProcesses(string filePath, CommandLineArgs args, TextWriter log)
        {
            TraceLog eventLog = GetTraceLog(filePath, args, log);
            return eventLog.Processes.Select(process => new ProcessForStackSource(process.Name)
                {
                    StartTime = process.StartTime,
                    EndTime = process.EndTime,
                    CPUTimeMSec = process.CPUMSec,
                    ParentID = process.ParentID,
                    CommandLine = process.CommandLine,
                    ProcessID = process.ProcessID
                })
                .Cast<IProcess>()
                .ToList();
        }
    }
}