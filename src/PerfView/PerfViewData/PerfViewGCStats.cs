using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Tracing.Etlx;
using Utilities;

namespace PerfView.PerfViewData
{
    public class PerfViewGCStats : PerfViewHtmlReport
    {
        public PerfViewGCStats(PerfViewFile dataFile) : base(dataFile, "GCStats") { }
        protected override string DoCommand(string command, StatusBar worker)
        {
            if (command.StartsWith("excel/"))
            {
                string raw = "";
                var rest = command.Substring(6);
                if (rest.StartsWith("perGeneration/"))
                {
                    raw = ".perGen";
                    rest = rest.Substring(14);
                }
                var processId = int.Parse(rest);
                if (m_gcStats.ContainsKey(processId))
                {
                    var gcProc = m_gcStats[processId];
                    var mang = Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(gcProc);
                    var csvFile = CacheFiles.FindFile(FilePath, ".gcStats." + processId.ToString() + raw + ".csv");
                    if (!File.Exists(csvFile) || File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(FilePath) ||
                        File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                    {
                        if (raw.Length != 0)
                        {
                            Stats.GcStats.PerGenerationCsv(csvFile, mang);
                        }
                        else
                        {
                            Stats.GcStats.ToCsv(csvFile, mang);
                        }
                    }
                    Command.Run(Command.Quote(csvFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                    System.Threading.Thread.Sleep(500);     // Give it time to start a bit.  
                    return "Opening CSV " + csvFile;
                }
            }
            else if (command.StartsWith("excelFinalization/"))
            {
                var processId = int.Parse(command.Substring(18));
                if (m_gcStats.ContainsKey(processId))
                {
                    var gcProc = m_gcStats[processId];
                    var mang = Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(gcProc);
                    var csvFile = CacheFiles.FindFile(FilePath, ".gcStats.Finalization." + processId.ToString() + ".csv");
                    if (!File.Exists(csvFile) || File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(FilePath) ||
                        File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                    {
                        Stats.GcStats.ToCsvFinalization(csvFile, mang);
                    }
                    Command.Run(Command.Quote(csvFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                    System.Threading.Thread.Sleep(500);     // Give it time to start a bit.  
                    return "Opening CSV " + csvFile;
                }
            }
            else if (command.StartsWith("xml/"))
            {
                var processId = int.Parse(command.Substring(4));
                if (m_gcStats.ContainsKey(processId) && Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(m_gcStats[processId]).GC.Stats().HasDetailedGCInfo)
                {
                    var gcProc = m_gcStats[processId];
                    var mang = Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(gcProc);
                    var xmlOutputName = CacheFiles.FindFile(FilePath, ".gcStats." + processId.ToString() + ".xml");
                    var csvFile = CacheFiles.FindFile(FilePath, ".gcStats." + processId.ToString() + ".csv");
                    if (!File.Exists(xmlOutputName) || File.GetLastWriteTimeUtc(xmlOutputName) < File.GetLastWriteTimeUtc(FilePath) ||
                        File.GetLastWriteTimeUtc(xmlOutputName) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                    {
                        using (var writer = File.CreateText(xmlOutputName))
                        {
                            Stats.GcStats.ToXml(writer, gcProc, mang, "");
                        }
                    }

                    // TODO FIX NOW Need a way of viewing it.  
                    var viewer = Command.FindOnPath("xmlView");
                    if (viewer == null)
                    {
                        viewer = "notepad";
                    }

                    Command.Run(viewer + " " + Command.Quote(xmlOutputName),
                        new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite).AddNoThrow());
                    return viewer + " launched on " + xmlOutputName;
                }
            }
            return "Unknown command " + command;
        }

        protected override void WriteHtmlBody(TraceLog dataFile, TextWriter writer, string fileName, TextWriter log)
        {
            using (var source = dataFile.Events.GetSource())
            {
                m_gcStats = new Dictionary<int, Microsoft.Diagnostics.Tracing.Analysis.TraceProcess>();
                Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.NeedLoadedDotNetRuntimes(source);
                Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.AddCallbackOnProcessStart(source, proc =>
                {
                    Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.SetSampleIntervalMSec(proc, (float)dataFile.SampleProfileInterval.TotalMilliseconds);
                    proc.Log = dataFile;
                });
                source.Process();
                foreach (var proc in Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.Processes(source))
                {
                    if (!m_gcStats.ContainsKey(proc.ProcessID) && Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(proc) != null)
                    {
                        m_gcStats.Add(proc.ProcessID, proc);
                    }
                }

                Stats.ClrStats.ToHtml(writer, m_gcStats.Values.ToList(), fileName, "GCStats", Stats.ClrStats.ReportType.GC, true);
            }
        }

        private Dictionary<int/*pid*/, Microsoft.Diagnostics.Tracing.Analysis.TraceProcess> m_gcStats;
    }
}