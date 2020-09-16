using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Diagnostics.Tracing.Etlx;
using Utilities;

namespace PerfView.PerfViewData
{
    public class PerfViewEventStats : PerfViewHtmlReport
    {
        public PerfViewEventStats(PerfViewFile dataFile) : base(dataFile, "EventStats") { }
        protected override void WriteHtmlBody(TraceLog dataFile, TextWriter writer, string fileName, TextWriter log)
        {
            m_counts = new List<TraceEventCounts>(dataFile.Stats);
            // Sort by count
            m_counts.Sort((x, y) => y.Count - x.Count);
            writer.WriteLine("<H2>Event Statistics</H2>");
            writer.WriteLine("<UL>");
            writer.WriteLine("<LI> <A HREF=\"command:excel\">View Event Statistics in Excel</A></LI>");
            writer.WriteLine("<LI>Total Event Count = {0:n0}</LI>", dataFile.EventCount);
            writer.WriteLine("<LI>Total Lost Events = {0:n0}</LI>", dataFile.EventsLost);
            writer.WriteLine("</UL>");

            writer.WriteLine("<Table Border=\"1\">");
            writer.Write("<TR>");
            writer.Write("<TH Align=\"Center\">Name</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The number of times this event occurs in the log.\">Count</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The average size of just the payload of this event.\">Average<BR/>Data Size</TH>");
            writer.Write("<TH Align=\"Center\" Title=\"The number of times this event has a stack trace associated with it.\">Stack<BR/>Count</TH>");
            writer.WriteLine("</TR>");
            foreach (TraceEventCounts count in m_counts)
            {
                writer.Write("<TR>");
                writer.Write("<TD Align=\"Left\">{0}/{1}</TD>", count.ProviderName, count.EventName);
                writer.Write("<TD Align=\"Right\">{0:n0}</TD>", count.Count);
                writer.Write("<TD Align=\"Right\">{0:n0}</TD>", count.AveragePayloadSize);
                writer.Write("<TD Align=\"Right\">{0:n0}</TD>", count.StackCount);
                writer.WriteLine("</TR>");
            }
            writer.WriteLine("</Table>");
        }
        protected override string DoCommand(string command, StatusBar worker)
        {
            if (command == "excel")
            {
                var csvFile = CacheFiles.FindFile(FilePath, ".eventstats.csv");
                if (!File.Exists(csvFile) || File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(FilePath) ||
                    File.GetLastWriteTimeUtc(csvFile) < File.GetLastWriteTimeUtc(SupportFiles.MainAssemblyPath))
                {
                    //make the csv
                    MakeEventStatCsv(m_counts, csvFile);
                }
                Command.Run(Command.Quote(csvFile), new CommandOptions().AddStart().AddTimeout(CommandOptions.Infinite));
                System.Threading.Thread.Sleep(500);     // Give it time to start a bit.  
                return "Opening CSV " + csvFile;
            }
            return null;
        }
        #region private
        private List<TraceEventCounts> m_counts;
        private void MakeEventStatCsv(List<TraceEventCounts> trace, string filepath)
        {
            string listSeparator = Thread.CurrentThread.CurrentCulture.TextInfo.ListSeparator;
            using (var writer = File.CreateText(filepath))
            {
                writer.WriteLine("Name{0}Count{0}AverageSize{0}StackCount", listSeparator);
                foreach (TraceEventCounts count in trace)
                {
                    writer.Write("{0}/{1}{2}", count.ProviderName, count.EventName, listSeparator);
                    writer.Write("{0}{1}", count.Count, listSeparator);
                    writer.Write("{0:f0}{1}", count.AveragePayloadSize, listSeparator);
                    writer.WriteLine("{0}", count.StackCount);
                }
            }
        }
        #endregion
    }
}