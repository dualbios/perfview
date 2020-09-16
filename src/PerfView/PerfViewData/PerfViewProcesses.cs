using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Diagnostics.Tracing.Etlx;
using Utilities;

namespace PerfView.PerfViewData
{
    /// <summary>
    /// Used to Display Processes Summary 
    /// </summary>
    public class PerfViewProcesses : PerfViewHtmlReport
    {

        public PerfViewProcesses(PerfViewFile dataFile) : base(dataFile, "Processes") { }
        protected override void WriteHtmlBody(TraceLog dataFile, TextWriter writer, string fileName, TextWriter log)
        {
            m_processes = new List<TraceProcess>(dataFile.Processes);
            // Sort by CPU time (largest first), then by start time (latest first)
            m_processes.Sort(delegate (TraceProcess x, TraceProcess y)
            {
                var ret = y.CPUMSec.CompareTo(x.CPUMSec);
                if (ret != 0)
                {
                    return ret;
                }

                return y.StartTimeRelativeMsec.CompareTo(x.StartTimeRelativeMsec);
            });

            var shortProcs = new List<TraceProcess>();
            var longProcs = new List<TraceProcess>();
            foreach (var process in m_processes)
            {
                if (process.ProcessID < 0)
                {
                    continue;
                }

                if (process.StartTimeRelativeMsec == 0 &&
                    process.EndTimeRelativeMsec == dataFile.SessionEndTimeRelativeMSec)
                {
                    longProcs.Add(process);
                }
                else
                {
                    shortProcs.Add(process);
                }
            }

            writer.WriteLine("<H2>Process Summary</H2>");

            writer.WriteLine("<UL>");
            writer.WriteLine("<LI> <A HREF=\"command:processes\">View Process Data in Excel</A></LI>");
            writer.WriteLine("<LI> <A HREF=\"command:module\">View Process Modules in Excel</A></LI>");
            writer.WriteLine("</UL>");

            if (shortProcs.Count > 0)
            {
                writer.WriteLine("<H3>Processes that did <strong>not</strong> live for the entire trace.</H3>");
                WriteProcTable(writer, shortProcs, true);
            }
            if (longProcs.Count > 0)
            {
                writer.WriteLine("<H3>Processes that <strong>did</strong> live for the entire trace.</H3>");
                WriteProcTable(writer, longProcs, false);
            }
        }
        /// <summary>
        /// Takes in either "processes" or "module" which will make a csv of their respective format
        /// </summary>
        protected override string DoCommand(string command, StatusBar worker)
        {
            if (command == "processes")
            {
                var csvFile = CacheFiles.FindFile(FilePath, ".processesSummary.csv");
                if (!File.Exists(csvFile) || File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(FilePath) ||
                    File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                {
                    MakeProcessesCsv(m_processes, csvFile);
                }
                Command.Run(Command.Quote(csvFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                System.Threading.Thread.Sleep(500);     // Give it time to start a bit.  
                return "Opening CSV " + csvFile;
            }
            else if (command == "module")
            {
                var csvFile = CacheFiles.FindFile(FilePath, ".processesModule.csv");
                if (!File.Exists(csvFile) || File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(FilePath) ||
                    File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                {
                    MakeModuleCsv(m_processes, csvFile);
                }
                Command.Run(Command.Quote(csvFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                System.Threading.Thread.Sleep(500);     // Give it time to start a bit.  
                return "Opening CSV " + csvFile;
            }
            return null;
        }
        #region private
        private void WriteProcTable(TextWriter writer, List<TraceProcess> processes, bool showExit)
        {
            bool showBitness = false;
            if (processes.Count > 0)
            {
                showBitness = (processes[0].Log.PointerSize == 8);
            }

            writer.WriteLine("<Table Border=\"1\">");
            writer.Write("<TR>");
            writer.Write("<TH Align=\"Center\">Name</TH>");
            writer.Write("<TH Align=\"Center\">ID</TH>");
            writer.Write("<TH Align=\"Center\">Parent<BR/>ID</TH>");
            if (showBitness)
            {
                writer.Write("<TH Align=\"Center\">Bitness</TH>");
            }

            writer.Write("<TH Align=\"Center\" Title=\"The amount of CPU time used (on any processor).\" >CPU<BR/>MSec</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The CPU used divided by the duration.\">Ave Procs<BR/>Used</TH>");
            if (showExit)
            {
                writer.Write("<TH Align=\"Center\">Duration<BR/>MSec</TH>");
                writer.Write("<TH Align=\"Center\" Title=\"The start time in milliseconds from the time the trace started.\">Start<BR/>MSec</TH>");
                writer.Write("<TH Align=\"Center\" Title=\"The integer that the process returned when it exited.\">Exit<BR/>Code</TH>");
            }
            writer.Write("<TH Align=\"Center\">Command Line</TH>");
            writer.WriteLine("</TR>");
            foreach (TraceProcess process in processes)
            {
                writer.Write("<TR>");
                writer.Write("<TD Align=\"Left\">{0}</TD>", process.Name);
                writer.Write("<TD Align=\"Right\">{0}</TD>", process.ProcessID);
                writer.Write("<TD Align=\"Right\">{0}</TD>", process.ParentID);
                if (showBitness)
                {
                    writer.Write("<TD Align=\"Center\">{0}</TD>", process.Is64Bit ? 64 : 32);
                }

                writer.Write("<TD Align=\"Right\">{0:n0}</TD>", process.CPUMSec);
                writer.Write("<TD Align=\"Right\">{0:n3}</TD>", process.CPUMSec / (process.EndTimeRelativeMsec - process.StartTimeRelativeMsec));
                if (showExit)
                {
                    writer.Write("<TD Align=\"Right\">{0:n3}</TD>", process.EndTimeRelativeMsec - process.StartTimeRelativeMsec);
                    writer.Write("<TD Align=\"Right\">{0:n3}</TD>", process.StartTimeRelativeMsec);
                    writer.Write("<TD Align=\"Right\">{0}</TD>", process.ExitStatus.HasValue ? "0x" + process.ExitStatus.Value.ToString("x") : "?");
                }
                writer.Write("<TD Align=\"Left\">{0}</TD>", process.CommandLine);
                writer.WriteLine("</TR>");
            }
            writer.WriteLine("</Table>");
        }

        /// <summary>
        /// Makes a csv file of the contents or processes at the filepath. 
        /// Headers to csv are  Name,ID,Parent_ID,Bitness,CPUMsec,AveProcsUsed,DurationMSec,StartMSec,ExitCode,CommandLine
        /// </summary>
        private void MakeProcessesCsv(List<TraceProcess> processes, string filepath)
        {
            using (var writer = File.CreateText(filepath))
            {
                //add headers 
                string listSeparator = Thread.CurrentThread.CurrentCulture.TextInfo.ListSeparator;
                writer.WriteLine("Name{0}ID{0}Parent_ID{0}Bitness{0}CPUMsec{0}AveProcsUsed{0}DurationMSec{0}StartMSec{0}ExitCode{0}CommandLine", listSeparator);
                foreach (TraceProcess process in processes)
                {
                    writer.Write("{0}{1}", process.Name, listSeparator);
                    writer.Write("{0}{1}", process.ProcessID, listSeparator);
                    writer.Write("{0}{1}", process.ParentID, listSeparator);
                    writer.Write("{0}{1}", process.Is64Bit ? 64 : 32, listSeparator);
                    writer.Write("{0:f0}{1}", process.CPUMSec, listSeparator);
                    writer.Write("{0:f3}{1}", process.CPUMSec / (process.EndTimeRelativeMsec - process.StartTimeRelativeMsec), listSeparator);
                    writer.Write("{0:f3}{1}", process.EndTimeRelativeMsec - process.StartTimeRelativeMsec, listSeparator);
                    writer.Write("{0:f3}{1}", process.StartTimeRelativeMsec, listSeparator);
                    writer.Write("{0}{1}", process.ExitStatus.HasValue ? "0x" + process.ExitStatus.Value.ToString("x") : "?", listSeparator);
                    writer.Write("{0}", PerfViewExtensibility.Events.EscapeForCsv(process.CommandLine, listSeparator));
                    writer.WriteLine("");
                }
            }
        }
        /// <summary>
        /// Makes a Csv at filepath
        /// </summary>
        private void MakeModuleCsv(List<TraceProcess> processes, string filepath)
        {
            using (var writer = File.CreateText(filepath))
            {
                //add headers 
                string listSeparator = Thread.CurrentThread.CurrentCulture.TextInfo.ListSeparator;
                writer.WriteLine("ProcessName{0}ProcessID{0}Name{0}FileVersion{0}Commit{0}BuildTime{0}FilePath{0}", listSeparator);
                foreach (TraceProcess process in processes)  //turn into private function 
                {
                    foreach (TraceLoadedModule module in process.LoadedModules)
                    {
                        writer.Write("{0}{1}", process.Name, listSeparator);
                        writer.Write("{0}{1}", process.ProcessID, listSeparator);
                        writer.Write("{0}{1}", module.ModuleFile.Name, listSeparator);
                        writer.Write("{0}{1}", module.ModuleFile.FileVersion, listSeparator);
                        writer.Write("{0}{1}", module.ModuleFile.GitCommitHash, listSeparator);
                        writer.Write("{0}{1}", PerfViewExtensibility.Events.EscapeForCsv(module.ModuleFile.BuildTime.ToString(), listSeparator), listSeparator);
                        writer.Write("{0}", module.ModuleFile.FilePath);
                        writer.WriteLine();
                    }
                }
            }
        }

        /// <summary>
        /// All the processes in this view.  
        /// </summary>
        private List<TraceProcess> m_processes;
        #endregion
    }
}