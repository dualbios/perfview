using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.ClrPrivate;
using Utilities;

namespace PerfView.PerfViewData
{
    public class PerfViewJitStats : PerfViewHtmlReport
    {
        public PerfViewJitStats(PerfViewFile dataFile) : base(dataFile, "JITStats") { }
        protected override string DoCommand(string command, StatusBar worker)
        {
            if (command.StartsWith("excel/"))
            {
                var rest = command.Substring(6);
                var processId = int.Parse(rest);
                if (m_jitStats.ContainsKey(processId))
                {
                    var jitProc = m_jitStats[processId];
                    var mang = Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(jitProc);
                    var csvFile = CacheFiles.FindFile(FilePath, ".jitStats." + processId.ToString() + ".csv");
                    if (!File.Exists(csvFile) || File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(FilePath) ||
                        File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                    {
                        Stats.JitStats.ToCsv(csvFile, mang);
                    }

                    Command.Run(Command.Quote(csvFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                    System.Threading.Thread.Sleep(500);     // Give it time to start a bit.  
                    return "Opening CSV " + csvFile;
                }
            }
            else if (command.StartsWith("excelInlining/"))
            {
                var rest = command.Substring(14);
                var processId = int.Parse(rest);
                if (m_jitStats.ContainsKey(processId))
                {
                    var jitProc = m_jitStats[processId];
                    var mang = Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(jitProc);
                    var csvFile = CacheFiles.FindFile(FilePath, ".jitInliningStats." + processId.ToString() + ".csv");
                    if (!File.Exists(csvFile) || File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(FilePath) ||
                        File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                    {
                        Stats.JitStats.ToInliningCsv(csvFile, mang);
                    }

                    Command.Run(Command.Quote(csvFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                    System.Threading.Thread.Sleep(500);     // Give it time to start a bit.  
                    return "Opening CSV " + csvFile;
                }
            }
            else if (command.StartsWith("excelBackgroundDiag/"))
            {
                var rest = command.Substring(20);
                var processId = int.Parse(rest);
                if (m_jitStats.ContainsKey(processId))
                {
                    var jitProc = m_jitStats[processId];
                    var mang = Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(jitProc);
                    List<object> events = m_bgJitEvents[processId];
                    var csvFile = CacheFiles.FindFile(FilePath, ".BGjitStats." + processId.ToString() + ".csv");
                    if (!File.Exists(csvFile) || File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(FilePath) ||
                        File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                    {
                        Stats.JitStats.BackgroundDiagCsv(csvFile, mang, events);
                    }

                    Command.Run(Command.Quote(csvFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                    System.Threading.Thread.Sleep(500);     // Give it time to start a bit.  
                    return "Opening CSV " + csvFile;
                }
            }

            return "Unknown command " + command;
        }

        protected override void WriteHtmlBody(TraceLog dataFile, TextWriter output, string fileName, TextWriter log)
        {
            var source = dataFile.Events.GetSource();

            m_jitStats = new Dictionary<int, Microsoft.Diagnostics.Tracing.Analysis.TraceProcess>();
            m_bgJitEvents = new Dictionary<int, List<object>>();

            // attach callbacks to grab background JIT events
            var clrPrivate = new ClrPrivateTraceEventParser(source);
            clrPrivate.ClrMulticoreJitCommon += delegate (MulticoreJitPrivateTraceData data)
            {
                if (!m_bgJitEvents.ContainsKey(data.ProcessID))
                {
                    m_bgJitEvents.Add(data.ProcessID, new List<object>());
                }

                m_bgJitEvents[data.ProcessID].Add(data.Clone());
            };
            source.Clr.LoaderModuleLoad += delegate (ModuleLoadUnloadTraceData data)
            {
                if (!m_bgJitEvents.ContainsKey(data.ProcessID))
                {
                    m_bgJitEvents.Add(data.ProcessID, new List<object>());
                }

                m_bgJitEvents[data.ProcessID].Add(data.Clone());
            };

            // process the model
            Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.NeedLoadedDotNetRuntimes(source);
            source.Process();
            foreach (var proc in Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.Processes(source))
            {
                if (Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.LoadedDotNetRuntime(proc) != null && !m_jitStats.ContainsKey(proc.ProcessID))
                {
                    m_jitStats.Add(proc.ProcessID, proc);
                }
            }

            Stats.ClrStats.ToHtml(output, m_jitStats.Values.ToList(), fileName, "JITStats", Stats.ClrStats.ReportType.JIT, true);
        }

        private Dictionary<int /*pid*/, Microsoft.Diagnostics.Tracing.Analysis.TraceProcess> m_jitStats;
        private Dictionary<int /*pid*/, List<object>> m_bgJitEvents;
    }
}