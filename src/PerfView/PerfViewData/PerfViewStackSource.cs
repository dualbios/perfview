using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace PerfView.PerfViewData
{
    public class PerfViewStackSource : PerfViewTreeItem
    {
        public PerfViewStackSource(PerfViewFile dataFile, string sourceName)
        {
            DataFile = dataFile;
            SourceName = sourceName;
            if (sourceName.EndsWith(" TaskTree"))   // Special case, call it 'TaskTree' to make it clearer that it is not a call stack
            {
                Name = SourceName;
            }
            else
            {
                Name = SourceName + " Stacks";
            }
        }
        public PerfViewFile DataFile { get; private set; }
        public string SourceName { get; private set; }
        public StackWindow Viewer { get; internal set; }
        public override string HelpAnchor { get { return SourceName.Replace(" ", "") + "Stacks"; } }
        public virtual string Title { get { return SourceName + " Stacks " + DataFile.Title; } }
        public virtual StackSource GetStackSource(TextWriter log, double startRelativeMSec = 0, double endRelativeMSec = double.PositiveInfinity)
        {
            if (m_StackSource != null && DataFile.IsUpToDate && startRelativeMSec == 0 && endRelativeMSec == double.PositiveInfinity)
            {
                return m_StackSource;
            }

            StackSource ret = DataFile.OpenStackSourceImpl(SourceName, log, startRelativeMSec, endRelativeMSec);
            if (ret == null)
            {
                ret = DataFile.OpenStackSourceImpl(log);
            }

            if (ret == null)
            {
                throw new ApplicationException("Not a file type that supports the StackView.");
            }

            if (startRelativeMSec == 0 && endRelativeMSec == double.PositiveInfinity)
            {
                m_StackSource = ret;
            }

            return ret;
        }

        // TODO not clear I want this method 
        protected virtual StackSource OpenStackSource(
            string streamName, TextWriter log, double startRelativeMSec = 0, double endRelativeMSec = double.PositiveInfinity, Predicate<TraceEvent> predicate = null)
        {
            return DataFile.OpenStackSourceImpl(streamName, log, startRelativeMSec, endRelativeMSec, predicate);
        }
        // TODO not clear I want this method (client could do it).  
        protected virtual void SetProcessFilter(string incPat)
        {
            Viewer.IncludeRegExTextBox.Text = incPat;
        }
        protected internal virtual StackSource OpenStackSourceImpl(TextWriter log) { return DataFile.OpenStackSourceImpl(log); }
        public override string FilePath { get { return DataFile.FilePath; } }
        public override void Open(Window parentWindow, StatusBar worker, Action doAfter = null)
        {
            // The OS Heap Alloc stack source has logic to look up type names from PDBs, we only do this
            // lookup when we initially create the stack source.  To allow the user to fetch more PDBs and 
            // try again, we remove the caching of the StackSource so re-opening recomputes the stack source.   
            if (Name.StartsWith("Net OS Heap Alloc"))
                m_StackSource = null;

            if (Viewer == null || !DataFile.IsUpToDate)
            {
                worker.StartWork("Opening " + Name, delegate ()
                {
                    if (m_StackSource == null || !DataFile.IsUpToDate)
                    {
                        // Compute the stack events
                        m_StackSource = OpenStackSource(SourceName, worker.LogWriter);
                        if (m_StackSource == null)
                        {
                            m_StackSource = OpenStackSourceImpl(worker.LogWriter);
                        }

                        if (m_StackSource == null)
                        {
                            throw new ApplicationException("Not a file type that supports the StackView.");
                        }
                    }

                    // Get the process summary if needed. 
                    List<IProcess> processes = null;
                    // TODO Using the source name here is a bit of hack.  Heap Allocations, however are already filtered to a process. 
                    if (DataFile.SupportsProcesses && SourceName != "Net OS Heap Alloc")
                    {
                        worker.Log("[Computing the processes involved in the trace.]");
                        processes = DataFile.GetProcesses(worker.LogWriter);
                    }

                    worker.EndWork(delegate ()
                    {
                        // This is the action that happens either after select process or after the stacks are computed.  
                        Action<List<IProcess>> launchViewer = delegate (List<IProcess> selectedProcesses)
                        {
                            Viewer = new StackWindow(parentWindow, this);
                            ConfigureStackWindow(Viewer);
                            Viewer.Show();

                            List<int> processIDs = null;
                            if (selectedProcesses != null && selectedProcesses.Count != 0)
                            {
                                processIDs = new List<int>();
                                string incPat = "";
                                foreach (var process in selectedProcesses)
                                {
                                    if (incPat.Length != 0)
                                    {
                                        incPat += "|";
                                    }

                                    incPat += DataFile.GetProcessIncPat(process);

                                    if (process.ProcessID != default) // process ID is not always available
                                    {
                                        processIDs.Add(process.ProcessID);
                                    }
                                }
                                SetProcessFilter(incPat);
                            }

                            Viewer.StatusBar.StartWork("Looking up high importance PDBs that are locally cached", delegate
                            {
                                // TODO This is probably a hack that it is here.  
                                var etlDataFile = DataFile as ETLPerfViewData;
                                TraceLog traceLog = null;
                                if (etlDataFile != null)
                                {
                                    var moduleFiles = ETLPerfViewData.GetInterestingModuleFiles(etlDataFile, 5.0, Viewer.StatusBar.LogWriter, processIDs);
                                    traceLog = etlDataFile.GetTraceLog(Viewer.StatusBar.LogWriter);
                                    using (var reader = etlDataFile.GetSymbolReader(Viewer.StatusBar.LogWriter,
                                        SymbolReaderOptions.CacheOnly | SymbolReaderOptions.NoNGenSymbolCreation))
                                    {
                                        foreach (var moduleFile in moduleFiles)
                                        {
                                            // TODO FIX NOW don't throw exceptions, 
                                            Viewer.StatusBar.Log("[Quick lookup of " + moduleFile.Name + "]");
                                            traceLog.CodeAddresses.LookupSymbolsForModule(reader, moduleFile);
                                        }
                                    }
                                }
                                Viewer.StatusBar.EndWork(delegate
                                {
                                    // Catch the error if you don't merge and move to a new machine.  
                                    if (traceLog != null && !traceLog.CurrentMachineIsCollectionMachine() && !traceLog.HasPdbInfo)
                                    {
                                        MessageBox.Show(parentWindow,
                                            "Warning!   This file was not merged and was moved from the collection\r\n" +
                                            "machine.  This means the data is incomplete and symbolic name resolution\r\n" +
                                            "will NOT work.  The recommended fix is use the perfview (not windows OS)\r\n" +
                                            "zip command.  Right click on the file in the main view and select ZIP.\r\n" +
                                            "\r\n" +
                                            "See merging and zipping in the users guide for more information.",
                                            "Data not merged before leaving the machine!");
                                    }

                                    Viewer.SetStackSource(m_StackSource, delegate ()
                                    {
                                        worker.Log("Opening Viewer.");
                                        if (WarnAboutBrokenStacks(Viewer, Viewer.StatusBar.LogWriter))
                                        {
                                            // TODO, WPF leaves blank regions after the dialog box is dismissed.  
                                            // Force a redraw by changing the size.  This should not be needed.   
                                            var width = Viewer.Width;
                                            Viewer.Width = width - 1;
                                            Viewer.Width = width;
                                        }
                                        FirstAction(Viewer);
                                        doAfter?.Invoke();
                                    });
                                });
                            });
                        };

                        if (processes != null && !SkipSelectProcess)
                        {
                            if (DataFile.InitiallyIncludedProcesses == null)
                            {
                                m_SelectProcess = new SelectProcess(parentWindow, processes, new TimeSpan(1, 0, 0), delegate (List<IProcess> selectedProcesses)
                                {
                                    launchViewer(selectedProcesses);
                                }, hasAllProc: true);
                                m_SelectProcess.Show();
                            }
                            else
                            {
                                launchViewer(processes.Where(p => DataFile.InitiallyIncludedProcesses
                                        .Any(iip => string.Equals(p.Name, iip, StringComparison.OrdinalIgnoreCase)))
                                    .ToList());
                            }
                        }
                        else
                        {
                            launchViewer(null);
                        }
                    });
                });
            }
            else
            {
                Viewer.Focus();
                doAfter?.Invoke();
            }
        }

        public override void Close() { }
        protected internal virtual void ConfigureStackWindow(StackWindow stackWindow)
        {
            DataFile.ConfigureStackWindow(SourceName, stackWindow);
        }
        protected internal virtual void FirstAction(StackWindow stackWindow)
        {
            DataFile.FirstAction(stackWindow);
        }
        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["StackSourceBitmapImage"] as ImageSource; } }

        // If set, we don't show the process selection dialog.  
        public bool SkipSelectProcess;
        #region private
        internal void ViewClosing(StackWindow viewer)
        {
            Viewer = null;
            DataFile.StackSourceClosing(this);
        }

        private bool WarnAboutBrokenStacks(Window parentWindow, TextWriter log)
        {
            if (!m_WarnedAboutBrokenStacks)
            {
                m_WarnedAboutBrokenStacks = true;
                float brokenPercent = Viewer.CallTree.Root.GetBrokenStackCount() * 100 / Viewer.CallTree.Root.InclusiveCount;
                if (brokenPercent > 0)
                {
                    bool is64bit = false;
                    foreach (var child in Viewer.CallTree.Root.Callees)
                    {
                        // if there is any process we can't determine is 64 bit, then we assume it might be.  
                        if (!child.Name.StartsWith("Process32 "))
                        {
                            is64bit = true;
                        }
                    }
                    return WarnAboutBrokenStacks(parentWindow, brokenPercent, is64bit, log);
                }
            }
            return false;
        }
        private static bool WarnAboutBrokenStacks(Window parentWindow, float brokenPercent, bool is64Bit, TextWriter log)
        {
            if (brokenPercent > 1)
            {
                log.WriteLine("Finished aggregating stacks.  (" + brokenPercent.ToString("f1") + "% Broken Stacks)");
            }

            if (brokenPercent > 10)
            {
                if (is64Bit)
                {
                    MessageBox.Show(parentWindow, "Warning: There are " + brokenPercent.ToString("f1") +
                                                  "% stacks that are broken, analysis is suspect." + "\r\n" +
                                                  "This is likely due the current inability of the OS stackwalker to walk 64 bit\r\n" +
                                                  "code that is dynamically (JIT) generated.\r\n\r\n" +
                                                  "This can be worked around by either by NGENing the EXE,\r\n" +
                                                  "forcing the EXE to run as a 32 bit app, profiling on Windows 8\r\n" +
                                                  "or avoiding any top-down analysis.\r\n\r\n" +
                                                  "Use the troubleshooting link at the top of the view for more information.\r\n",
                        "Broken Stacks");
                }
                else
                {
                    MessageBox.Show(parentWindow, "Warning: There are " + brokenPercent.ToString("f1") + "% stacks that are broken\r\n" +
                                                  "Top down analysis is suspect, however bottom up approaches are still valid.\r\n\r\n" +
                                                  "Use the troubleshooting link at the top of the view for more information.\r\n",
                        "Broken Stacks");
                }

                return true;
            }
            return false;
        }

        internal StackSource m_StackSource;
        internal SelectProcess m_SelectProcess;
        private bool m_WarnedAboutBrokenStacks;
        #endregion
    }
}