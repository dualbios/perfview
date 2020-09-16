using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Diagnostics.Tracing.Etlx;
using PerfView.GuiUtilities;
using Utilities;

namespace PerfView.PerfViewData
{
    /// <summary>
    /// Represents a report from an ETL file that can be viewed in a web browsers.  Subclasses need 
    /// to override OpenImpl().  
    /// </summary>
    public abstract class PerfViewHtmlReport : PerfViewTreeItem
    {
        public PerfViewHtmlReport(PerfViewFile dataFile, string name)
        {
            DataFile = dataFile;
            Name = name;
        }
        public virtual string Title { get { return Name + " for " + DataFile.Title; } }
        public PerfViewFile DataFile { get; private set; }
        public WebBrowserWindow Viewer { get; internal set; }
        public override string FilePath { get { return DataFile.FilePath; } }
        protected abstract void WriteHtmlBody(TraceLog dataFile, TextWriter writer, string fileName, TextWriter log);
        /// <summary>
        /// You can make Command:XXXX urls which come here when users click on them.   
        /// Returns an  error message (or null if it succeeds).  
        /// </summary>
        protected virtual string DoCommand(string command, StatusBar worker)
        {
            return "Unimplemented command: " + command;
        }

        protected virtual string DoCommand(string command, StatusBar worker, out Action continuation)
        {
            continuation = null;
            return DoCommand(command, worker);
        }

        protected virtual string DoCommand(Uri commandUri, StatusBar worker, out Action continuation)
        {
            return DoCommand(commandUri.LocalPath, worker, out continuation);
        }

        public override void Open(Window parentWindow, StatusBar worker, Action doAfter)
        {
            if (Viewer == null)
            {
                TraceLog trace = GetTrace(worker);

                worker.StartWork("Opening " + Name, delegate ()
                {
                    string reportFileName = GenerateReportFile(worker, trace);

                    worker.EndWork(delegate ()
                    {
                        Viewer = new WebBrowserWindow(parentWindow);
                        Viewer.WindowState = System.Windows.WindowState.Maximized;
                        Viewer.Closing += delegate (object sender, CancelEventArgs e)
                        {
                            Viewer = null;
                        };
                        Viewer.Browser.Navigating += delegate (object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
                        {
                            if (e.Uri.Scheme == "command")
                            {
                                e.Cancel = true;
                                Viewer.StatusBar.StartWork("Following Hyperlink", delegate ()
                                {
                                    Action continuation;
                                    var message = DoCommand(e.Uri, Viewer.StatusBar, out continuation);
                                    Viewer.StatusBar.EndWork(delegate ()
                                    {
                                        if (message != null)
                                        {
                                            Viewer.StatusBar.Log(message);
                                        }

                                        continuation?.Invoke();
                                    });
                                });
                            }
                        };

                        Viewer.Width = 1000;
                        Viewer.Height = 600;
                        Viewer.Title = Title;
                        WebBrowserWindow.Navigate(Viewer.Browser, reportFileName);
                        Viewer.Show();

                        doAfter?.Invoke();
                    });
                });
            }
            else
            {
                Viewer.Focus();
                doAfter?.Invoke();
            }
        }

        /// <summary>
        /// Generates an HTML report and opens it using the machine's default handler .html file paths.
        /// </summary>
        /// <param name="worker">The StatusBar that should be updated with progress.</param>
        public void OpenInExternalBrowser(StatusBar worker)
        {
            TraceLog trace = GetTrace(worker);

            worker.StartWork("Opening in external browser " + Name, delegate ()
            {
                string reportFileName = GenerateReportFile(worker, trace);

                worker.EndWork(delegate ()
                {
                    Process.Start(reportFileName);
                });
            });
        }

        public override void Close() { }
        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["HtmlReportBitmapImage"] as ImageSource; } }

        private TraceLog GetTrace(StatusBar worker)
        {
            var etlDataFile = DataFile as ETLPerfViewData;
            TraceLog trace = null;
            if (etlDataFile != null)
            {
                trace = etlDataFile.GetTraceLog(worker.LogWriter);
            }
            else
            {
                var linuxDataFile = DataFile as LinuxPerfViewData;
                if (linuxDataFile != null)
                {
                    trace = linuxDataFile.GetTraceLog(worker.LogWriter);
                }
                else
                {
                    var eventPipeDataFile = DataFile as EventPipePerfViewData;
                    if (eventPipeDataFile != null)
                    {
                        trace = eventPipeDataFile.GetTraceLog(worker.LogWriter);
                    }
                }
            }

            return trace;
        }

        private string GenerateReportFile(StatusBar worker, TraceLog trace)
        {
            var reportFileName = CacheFiles.FindFile(FilePath, "." + Name + ".html");
            using (var writer = File.CreateText(reportFileName))
            {
                writer.WriteLine("<html>");
                writer.WriteLine("<head>");
                writer.WriteLine("<title>{0}</title>", Title);
                writer.WriteLine("<meta charset=\"UTF-8\"/>");
                writer.WriteLine("<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"/>");

                // Add basic styling to the generated HTML
                writer.WriteLine(@"
<style>
body {
    font-family: Segoe UI Light, Helvetica, sans-serif;
}

tr:hover {
    background-color: #eeeeee;
}

th {
    background-color: #eeeeee;
    font-family: Helvetica;
    padding: 4px;
    font-size: small;
    font-weight: normal;
}

td {
    font-family: Consolas, monospace;
    font-size: small;
    padding: 3px;
    padding-bottom: 5px;
}

table {
    border-collapse: collapse;
}
</style>
");

                writer.WriteLine("</head>");
                writer.WriteLine("<body>");
                WriteHtmlBody(trace, writer, reportFileName, worker.LogWriter);
                writer.WriteLine("</body>");
                writer.WriteLine("</html>");


            }

            return reportFileName;
        }
    }
}