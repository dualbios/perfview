using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace PerfView.PerfViewData
{
    public class PerfViewTraceInfo : PerfViewHtmlReport
    {
        public PerfViewTraceInfo(PerfViewFile dataFile) : base(dataFile, "TraceInfo") { }
        protected override void WriteHtmlBody(TraceLog dataFile, TextWriter writer, string fileName, TextWriter log)
        {
            writer.WriteLine("<H2>Information on the Trace and Machine</H2>");
            writer.WriteLine("<Table Border=\"1\">");
            writer.WriteLine("<TR><TD>Machine Name</TD><TD Align=\"Center\">{0}</TD></TR>",
                string.IsNullOrEmpty(dataFile.MachineName) ? "&nbsp;" : dataFile.MachineName);
            writer.WriteLine("<TR><TD>Operating System</TD><TD Align=\"Center\">{0}</TD></TR>",
                string.IsNullOrEmpty(dataFile.OSName) ? "&nbsp;" : dataFile.OSName);
            writer.WriteLine("<TR><TD>OS Build Number</TD><TD Align=\"Center\">{0}</TD></TR>",
                string.IsNullOrEmpty(dataFile.OSBuild) ? "&nbsp;" : dataFile.OSBuild);
            writer.WriteLine("<TR><TD Title=\"This is negative if the data was collected in a time zone west of UTC\">UTC offset where data was collected</TD><TD Align=\"Center\">{0}</TD></TR>",
                dataFile.UTCOffsetMinutes.HasValue ? (dataFile.UTCOffsetMinutes.Value / 60.0).ToString("f2") : "Unknown");
            writer.WriteLine("<TR><TD Title=\"This is negative if PerfView is running in a time zone west of UTC\">UTC offset where PerfView is running</TD><TD Align=\"Center\">{0:f2}</TD></TR>",
                TimeZoneInfo.Local.GetUtcOffset(dataFile.SessionStartTime).TotalHours);

            if (dataFile.UTCOffsetMinutes.HasValue)
            {
                writer.WriteLine("<TR><TD Title=\"This is negative if analysis is happening west of collection\">Delta of Local and Collection Time</TD><TD Align=\"Center\">{0:f2}</TD></TR>",
                    TimeZoneInfo.Local.GetUtcOffset(dataFile.SessionStartTime).TotalHours - (dataFile.UTCOffsetMinutes.Value / 60.0));
            }
            writer.WriteLine("<TR><TD>OS Boot Time</TD><TD Align=\"Center\">{0:MM/dd/yyyy HH:mm:ss.fff}</TD></TR>", dataFile.BootTime);
            writer.WriteLine("<TR><TD>Trace Start Time</TD><TD Align=\"Center\">{0:MM/dd/yyyy HH:mm:ss.fff}</TD></TR>", dataFile.SessionStartTime);
            writer.WriteLine("<TR><TD>Trace End Time</TD><TD Align=\"Center\">{0:MM/dd/yyyy HH:mm:ss.fff}</TD></TR>", dataFile.SessionEndTime);
            writer.WriteLine("<TR><TD>Trace Duration (Sec)</TD><TD Align=\"Center\">{0:n1}</TD></TR>", dataFile.SessionDuration.TotalSeconds);
            writer.WriteLine("<TR><TD>CPU Frequency (MHz)</TD><TD Align=\"Center\">{0:n0}</TD></TR>", dataFile.CpuSpeedMHz);
            writer.WriteLine("<TR><TD>Number Of Processors</TD><TD Align=\"Center\">{0}</TD></TR>", dataFile.NumberOfProcessors);
            writer.WriteLine("<TR><TD>Memory Size (MB)</TD><TD Align=\"Center\">{0:n0}</TD></TR>", dataFile.MemorySizeMeg);
            writer.WriteLine("<TR><TD>Pointer Size</TD><TD Align=\"Center\">{0}</TD></TR>", dataFile.PointerSize);
            writer.WriteLine("<TR><TD>Sample Profile Interval (MSec) </TD><TD Align=\"Center\">{0:n2}</TD></TR>", dataFile.SampleProfileInterval.TotalMilliseconds);
            writer.WriteLine("<TR><TD>Total Events</TD><TD Align=\"Center\">{0:n0}</TD></TR>", dataFile.EventCount);
            writer.WriteLine("<TR><TD>Lost Events</TD><TD Align=\"Center\">{0:n0}</TD></TR>", dataFile.EventsLost);

            double len = 0;
            try
            {
                len = new System.IO.FileInfo(DataFile.FilePath).Length / 1000000.0;
            }
            catch (Exception) { }
            if (len > 0)
            {
                writer.WriteLine("<TR><TD>ETL File Size (MB)</TD><TD Align=\"Center\">{0:n1}</TD></TR>", len);
            }

            string logPath = null;

            Debug.Assert(fileName.EndsWith("TraceInfo.html"));
            if (fileName.EndsWith(".TraceInfo.html"))
            {
                logPath = fileName.Substring(0, fileName.Length - 15) + ".LogFile.txt";
            }

            if (logPath != null && File.Exists(logPath))
            {
                writer.WriteLine("<TR><TD colspan=\"2\" Align=\"Center\"> <A HREF=\"command:displayLog:{0}\">View data collection log file</A></TD></TR>", logPath);
            }
            else
            {
                writer.WriteLine("<TR><TD colspan=\"2\" Align=\"Center\"> No data collection log file found</A></TD></TR>");
            }

            writer.WriteLine("</Table>");
        }

        protected override string DoCommand(string command, StatusBar worker)
        {

            if (command.StartsWith("displayLog:"))
            {
                string logFile = command.Substring(command.IndexOf(':') + 1);
                worker.Parent.Dispatcher.BeginInvoke((Action)delegate ()
                {
                    var logTextWindow = new Controls.TextEditorWindow(GuiApp.MainWindow);
                    logTextWindow.TextEditor.OpenText(logFile);
                    logTextWindow.TextEditor.IsReadOnly = true;
                    logTextWindow.Title = "Collection time log";
                    logTextWindow.HideOnClose = true;
                    logTextWindow.Show();
                    logTextWindow.TextEditor.Body.ScrollToEnd();
                });

                return "Displaying Log";
            }
            return "Unknown command " + command;
        }
    }
}