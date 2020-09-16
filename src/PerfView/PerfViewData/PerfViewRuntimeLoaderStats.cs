using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Utilities;

namespace PerfView.PerfViewData
{
    public class PerfViewRuntimeLoaderStats : PerfViewHtmlReport
    {
        public PerfViewRuntimeLoaderStats(PerfViewFile dataFile) : base(dataFile, "Runtime Loader") { }
        protected override string DoCommand(Uri commandUri, StatusBar worker, out Action continuation)
        {
            continuation = null;

            string command = commandUri.LocalPath;
            string textStr = "txt/";
            string csvStr = "csv/";
            bool text = command.StartsWith(textStr);
            bool csv = command.StartsWith(csvStr);

            if (text || csv)
            {
                var rest = command.Substring(textStr.Length);


                bool tree = true;
                List<string> filters = null;
                if (!String.IsNullOrEmpty(commandUri.Query))
                {
                    filters = new List<string>();
                    tree = commandUri.Query.Contains("TreeView");
                    if (commandUri.Query.Contains("JIT"))
                        filters.Add("JIT");
                    if (commandUri.Query.Contains("R2R_Found"))
                        filters.Add("R2R_Found");
                    if (commandUri.Query.Contains("R2R_Failed"))
                        filters.Add("R2R_Failed");
                    if (commandUri.Query.Contains("TypeLoad"))
                        filters.Add("TypeLoad");
                    if (commandUri.Query.Contains("AssemblyLoad"))
                        filters.Add("AssemblyLoad");
                }
                string identifier = $"{(tree?"Tree":"Flat")}_";
                if (filters != null)
                {
                    foreach (var filter in filters)
                    {
                        identifier = identifier + "_" + filter;
                    }
                }

                var startMSec = double.Parse(rest.Substring(rest.IndexOf(',') + 1));
                var processId = int.Parse(rest.Substring(0, rest.IndexOf(',')));
                var processData = m_runtimeData.GetProcessDataFromProcessIDAndTimestamp(processId, startMSec);

                var txtFile = CacheFiles.FindFile(FilePath, ".runtimeLoaderstats." + processId.ToString() + "_" + ((long)startMSec).ToString() + "_" + identifier + (csv ? ".csv" : ".txt"));
                if (!File.Exists(txtFile) || File.GetLastWriteTimeUtc(txtFile) < File.GetLastWriteTimeUtc(FilePath) ||
                    File.GetLastWriteTimeUtc(txtFile) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                {
                    Stats.RuntimeLoaderStats.ToTxt(txtFile, processData, filters.ToArray(), tree);
                }
                Command.Run(Command.Quote(txtFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                System.Threading.Thread.Sleep(500);     // Give it time to start a bit.  
                return "Opening Txt " + txtFile;
            }
            return "Unknown command " + command;
        }

        protected override void WriteHtmlBody(TraceLog dataFile, TextWriter writer, string fileName, TextWriter log)
        {
            using (var source = dataFile.Events.GetSource())
            {
                Microsoft.Diagnostics.Tracing.Analysis.TraceLoadedDotNetRuntimeExtensions.NeedLoadedDotNetRuntimes(source);
                CLRRuntimeActivityComputer runtimeLoaderComputer = new CLRRuntimeActivityComputer(source);
                source.Process();
                m_runtimeData = runtimeLoaderComputer.RuntimeLoaderData;
                Stats.ClrStats.ToHtml(writer, Microsoft.Diagnostics.Tracing.Analysis.TraceProcessesExtensions.Processes(source).ToList(), fileName, "Runtime Loader", Stats.ClrStats.ReportType.RuntimeLoader, true, runtimeOpsStats : m_runtimeData);
            }
        }

        private RuntimeLoaderStatsData m_runtimeData;
    }
}