using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Diagnostics.Tracing.StackSources;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Stacks;
using Microsoft.Diagnostics.Tracing.StackSources;
using Microsoft.Diagnostics.Utilities;
using Utilities;
using EventSource = EventSources.EventSource;

namespace PerfView.PerfViewData
{
    public partial class LinuxPerfViewData : PerfViewFile
    {
        private string[] PerfScriptStreams = new string[]
        {
            "CPU",
            "CPU (with Optimization Tiers)",
            "Thread Time"
        };

        public override string FormatName { get { return "Perf"; } }

        public override string[] FileExtensions { get { return new string[] { ".trace.zip", "perf.data.txt" }; } }

        public override bool SupportsProcesses => true;

        protected internal override EventSource OpenEventSourceImpl(TextWriter log)
        {
            var traceLog = GetTraceLog(log);
            return new ETWEventSource(traceLog);
        }
        protected internal override StackSource OpenStackSourceImpl(string streamName, TextWriter log, double startRelativeMSec = 0, double endRelativeMSec = double.PositiveInfinity, Predicate<TraceEvent> predicate = null)
        {
            if (PerfScriptStreams.Contains(streamName))
            {
                string xmlPath;
                bool doThreadTime = false;

                if (streamName == "Thread Time")
                {
                    xmlPath = CacheFiles.FindFile(FilePath, ".perfscript.threadtime.xml.zip");
                    doThreadTime = true;
                }
                else
                {
                    xmlPath = CacheFiles.FindFile(FilePath, ".perfscript.cpu.xml.zip");
                }

#if !DEBUG
                if (!CacheFiles.UpToDate(xmlPath, FilePath))
#endif
                {
                    XmlStackSourceWriter.WriteStackViewAsZippedXml(
                        new LinuxPerfScriptStackSource(FilePath, doThreadTime), xmlPath);
                }

                bool showOptimizationTiers =
                    App.CommandLineArgs.ShowOptimizationTiers || streamName.Contains("(with Optimization Tiers)");
                return new XmlStackSource(xmlPath, null, showOptimizationTiers);
            }

            return null;
        }

        public override Action<Action> OpenImpl(Window parentWindow, StatusBar worker)
        {
            // Open the file.
            m_traceLog = GetTraceLog(worker.LogWriter);

            bool hasGC = false;
            bool hasJIT = false;
            bool hasTypeLoad = false;
            bool hasAssemblyLoad = false;
            if (m_traceLog != null)
            {
                foreach (TraceEventCounts eventStats in m_traceLog.Stats)
                {
                    if (eventStats.EventName.StartsWith("GC/Start"))
                    {
                        hasGC = true;
                    }
                    else if (eventStats.EventName.StartsWith("Method/JittingStarted"))
                    {
                        hasJIT = true;
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
            var experimental = new PerfViewTreeGroup("Experimental Group");

            m_Children.Add(new PerfViewStackSource(this, "CPU"));

            if (!App.CommandLineArgs.ShowOptimizationTiers &&
                m_traceLog != null &&
                m_traceLog.Events.Any(
                    e => e is MethodLoadUnloadTraceDataBase td && td.OptimizationTier != OptimizationTier.Unknown))
            {
                advanced.AddChild(new PerfViewStackSource(this, "CPU (with Optimization Tiers)"));
            }

            experimental.AddChild(new PerfViewStackSource(this, "Thread Time"));

            if (m_traceLog != null)
            {
                m_Children.Add(new PerfViewEventSource(this));
                m_Children.Add(new PerfViewEventStats(this));

                if (hasGC)
                {
                    memory.AddChild(new PerfViewGCStats(this));
                }

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

            if(AppLog.InternalUser && experimental.Children.Count > 0)
            {
                m_Children.Add(experimental);
            }

            return null;
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

        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["FileBitmapImage"] as ImageSource; } }

        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            stackWindow.ScalingPolicy = ScalingPolicyKind.TimeMetric;
            stackWindow.GroupRegExTextBox.Text = stackWindow.GetDefaultGroupPat();

            ConfigureGroupRegExTextBox(stackWindow, windows: false);
        }

        public TraceLog GetTraceLog(TextWriter log)
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
            var options = new TraceLogOptions();
            options.ConversionLog = log;
            if (App.CommandLineArgs.KeepAllEvents)
            {
                options.KeepAllEvents = true;
            }

            options.MaxEventCount = App.CommandLineArgs.MaxEventCount;
            options.ContinueOnError = App.CommandLineArgs.ContinueOnError;
            options.SkipMSec = App.CommandLineArgs.SkipMSec;
            //options.OnLostEvents = onLostEvents;
            options.LocalSymbolsOnly = false;
            options.ShouldResolveSymbols = delegate (string moduleFilePath) { return false; };       // Don't resolve any symbols

            // Generate the etlx file path / name.
            string etlxFile = CacheFiles.FindFile(dataFileName, ".etlx");
            if (!File.Exists(etlxFile) || File.GetLastWriteTimeUtc(etlxFile) < File.GetLastWriteTimeUtc(dataFileName))
            {
                FileUtilities.ForceDelete(etlxFile);
                log.WriteLine("Creating ETLX file {0} from {1}", etlxFile, dataFileName);
                try
                {
                    TraceLog.CreateFromLttngTextDataFile(dataFileName, etlxFile, options);
                }
                catch (Exception e)        // Throws this if there is no CTF Information
                {
                    if (e is EndOfStreamException)
                    {
                        log.WriteLine("Warning: Trying to open CTF stream failed, no CTF (lttng) information");
                    }
                    else
                    {
                        log.WriteLine("Error: Exception CTF conversion: {0}", e.ToString());
                        log.WriteLine("[Error: exception while opening CTF (lttng) data.]");
                    }

                    Debug.Assert(m_traceLog == null);
                    m_noTraceLogInfo = true;
                    return m_traceLog;
                }
            }

            var dataFileSize = "Unknown";
            if (File.Exists(dataFileName))
            {
                dataFileSize = ((new System.IO.FileInfo(dataFileName)).Length / 1000000.0).ToString("n3") + " MB";
            }

            log.WriteLine("ETL Size {0} ETLX Size {1:n3} MB", dataFileSize, (new System.IO.FileInfo(etlxFile)).Length / 1000000.0);

            // Open the ETLX file.  
            m_traceLog = new TraceLog(etlxFile);
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

        public TraceLog TryGetTraceLog() { return m_traceLog; }

        #region Private
        private TraceLog m_traceLog;
        private bool m_noTraceLogInfo;
        #endregion
    }
}