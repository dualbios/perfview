using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Stacks;
using EventSource = EventSources.EventSource;

namespace PerfView.PerfViewData
{
    /// <summary>
    /// PerfViewData is an abstraction of something that PerfViewGui knows how to display.   It is 
    /// </summary>
    public abstract class PerfViewFile : PerfViewTreeItem
    {
        public bool IsOpened { get { return m_opened; } }
        public bool IsUpToDate { get { return m_utcLastWriteAtOpen == File.GetLastWriteTimeUtc(FilePath); } }

        /// <summary>
        /// Get does not actually open the file (which might be expensive).   It also does not
        /// populate the children for the node (which again might be expensive).  Instead it 
        /// just looks at the file name).   It DOES however determine how this data will be
        /// treated from here on (based on file extension or an explicitly passed template parameter.
        /// 
        /// Get implements interning, so if you Get the same full file path, then you will get the
        /// same PerfViewDataFile structure. 
        /// 
        /// After you have gotten a PerfViewData, you use instance methods to manipulate it
        /// 
        /// This routine throws if the path name does not have a suffix we understand.  
        /// </summary>
        public static PerfViewFile Get(string filePath, PerfViewFile format = null)
        {
            var ret = TryGet(filePath, format);
            if (ret == null)
            {
                throw new ApplicationException("Could not determine data Template from the file extension for " + filePath + ".");
            }

            return ret;
        }
        /// <summary>
        /// Tries to to a 'Get' operation on filePath.   If format == null (indicating
        /// that we should try to determine the type of the file from the file suffix) and 
        /// it does not have a suffix we understand, then we return null.   
        /// </summary>
        public static PerfViewFile TryGet(string filePath, PerfViewFile format = null)
        {
            if (format == null)
            {
                // See if it is any format we recognize.  
                foreach (PerfViewFile potentalFormat in Formats)
                {
                    if (potentalFormat.IsMyFormat(filePath))
                    {
                        format = potentalFormat;
                        break;
                    };
                }
                if (format == null)
                {
                    return null;
                }
            }

            string fullPath = Path.GetFullPath(filePath);
            PerfViewFile ret;
            if (!s_internTable.TryGetValue(fullPath, out ret))
            {
                ret = (PerfViewFile)format.MemberwiseClone();
                ret.m_filePath = filePath;
                s_internTable[fullPath] = ret;
            }
            ret.Name = Path.GetFileName(ret.FilePath);
            if (ret.Name.EndsWith(".etl", StringComparison.OrdinalIgnoreCase))
            {
                var dir = Path.GetDirectoryName(ret.FilePath);
                if (dir.Length == 0)
                {
                    dir = ".";
                }

                var wildCard = ret.Name.Insert(ret.Name.Length - 4, ".*");
                if (Directory.GetFiles(dir, wildCard).Length > 0)
                {
                    ret.Name += " (unmerged)";
                }
            }
            return ret;
        }

        /// <summary>
        /// Logs the fact that the GUI should call a user defined method when a file is opened.  
        /// </summary>
        public static PerfViewFile GetTemplateForExtension(string extension)
        {
            foreach (PerfViewFile potentalFormat in Formats)
            {
                if (potentalFormat.IsMyFormat(extension))
                {
                    return potentalFormat;
                }
            }
            var ret = new PerfViewUserFile(extension + " file", new string[] { extension });
            Formats.Add(ret);
            return ret;
        }

        /// <summary>
        /// Declares that the user command 'userCommand' (that takes one string argument) 
        /// should be called when the file is opened.  
        /// </summary>
        public void OnOpenFile(string userCommand)
        {
            if (userCommands == null)
            {
                userCommands = new List<string>();
            }

            userCommands.Add(userCommand);
        }

        /// <summary>
        /// Declares that the file should have a view called 'viewName' and the user command
        /// 'userCommand' (that takes two string arguments (file, viewName)) should be called 
        /// when that view is opened 
        /// </summary>
        public void DeclareFileView(string viewName, string userCommand)
        {
            if (m_UserDeclaredChildren == null)
            {
                m_UserDeclaredChildren = new List<PerfViewReport>();
            }

            m_UserDeclaredChildren.Add(new PerfViewReport(viewName, delegate (string reportFileName, string reportViewName)
            {
                PerfViewExtensibility.Extensions.ExecuteUserCommand(userCommand, new string[] { reportFileName, reportViewName });
            }));
        }

        internal void ExecuteOnOpenCommand(StatusBar worker)
        {
            if (m_UserDeclaredChildren != null)
            {
                // The m_UserDeclaredChildren are templates.  We need to instantiate them to this file before adding them as children. 
                foreach (var userDeclaredChild in m_UserDeclaredChildren)
                {
                    m_Children.Add(new PerfViewReport(userDeclaredChild, this));
                }

                m_UserDeclaredChildren = null;
            }
            // Add the command to the list 
            if (userCommands == null)
            {
                return;
            }

            var args = new string[] { FilePath };
            foreach (string command in userCommands)
            {
                worker.LogWriter.WriteLine("Running User defined OnFileOpen command " + command);
                try
                {
                    PerfViewExtensibility.Extensions.ExecuteUserCommand(command, args);
                }
                catch (Exception e)
                {
                    worker.LogError(@"Error executing OnOpenFile command " + command + "\r\n" + e.ToString());
                }
            }
            return;
        }

        // A list of user commands to be executed when a file is opened 
        private List<string> userCommands;

        /// <summary>
        /// Retrieves the base file name from a PerfView data source's name.
        /// </summary>
        /// <example>
        /// GetBaseName(@"C:\data\foo.bar.perfView.xml.zip") == "foo.bar"
        /// </example>
        /// <param name="filePath">The path to the data source.</param>
        /// <returns>The base name, without extensions or path, of <paramref name="filePath"/>.</returns>
        public static string GetFileNameWithoutExtension(string filePath)
        {
            string fileName = Path.GetFileName(filePath);

            foreach (var fmt in Formats)
            {
                foreach (string ext in fmt.FileExtensions)
                {
                    if (fileName.EndsWith(ext))
                    {
                        return fileName.Substring(0, fileName.Length - ext.Length);
                    }
                }
            }

            return fileName;
        }
        /// <summary>
        /// Change the extension of a PerfView data source path.
        /// </summary>
        /// <param name="filePath">The path to change.</param>
        /// <param name="newExtension">The new extension to add.</param>
        /// <returns>The path to a file with the same directory and base name of <paramref name="filePath"/>, 
        /// but with extension <paramref name="newExtension"/>.</returns>
        public static string ChangeExtension(string filePath, string newExtension)
        {
            string dirName = Path.GetDirectoryName(filePath);
            string fileName = GetFileNameWithoutExtension(filePath) + newExtension;
            return Path.Combine(dirName, fileName);
        }

        public override void Open(Window parentWindow, StatusBar worker, Action doAfter = null)
        {
            if (!m_opened)
            {
                worker.StartWork("Opening " + Name, delegate ()
                {
                    Action<Action> continuation = OpenImpl(parentWindow, worker);
                    ExecuteOnOpenCommand(worker);

                    worker.EndWork(delegate ()
                    {
                        m_opened = true;
                        FirePropertyChanged("Children");

                        IsExpanded = true;
                        var defaultSource = GetStackSource();
                        if (defaultSource != null)
                        {
                            defaultSource.IsSelected = true;
                        }

                        if (continuation != null)
                        {
                            continuation(doAfter);
                        }
                        else
                        {
                            doAfter?.Invoke();
                        }
                    });
                });
            }
            else
            {
                if (m_singletonStackSource != null && m_singletonStackSource.Viewer != null)
                {
                    m_singletonStackSource.Viewer.Focus();
                }
            }
        }
        public override void Close()
        {
            if (m_opened)
            {
                m_opened = false;
                s_internTable.Remove(FilePath);
            }

            if (m_Children != null)
            {
                m_Children.Clear();
                FirePropertyChanged("Children");
            }
        }

        public virtual PerfViewStackSource GetStackSource(string sourceName = null)
        {
            if (sourceName == null)
            {
                sourceName = DefaultStackSourceName;
                if (sourceName == null)
                {
                    return null;
                }
            }

            Debug.Assert(m_opened);
            if (m_Children != null)
            {
                foreach (var child in m_Children)
                {
                    var asStackSource = child as PerfViewStackSource;
                    if (asStackSource != null && asStackSource.SourceName == sourceName)
                    {
                        return asStackSource;
                    }

                    var asGroup = child as PerfViewTreeGroup;
                    if (asGroup != null && asGroup.Children != null)
                    {
                        foreach (var groupChild in asGroup.Children)
                        {
                            asStackSource = groupChild as PerfViewStackSource;
                            if (asStackSource != null && asStackSource.SourceName == sourceName)
                            {
                                return asStackSource;
                            }
                        }
                    }
                }
            }
            else if (m_singletonStackSource != null)
            {
                var asStackSource = m_singletonStackSource as PerfViewStackSource;
                if (asStackSource != null)
                {
                    return asStackSource;
                }
            }
            return null;
        }
        public virtual string DefaultStackSourceName { get { return "CPU"; } }

        /// <summary>
        /// Gets or sets the processes to be initially included in stack views. The default value is null. 
        /// The names are not case sensitive.
        /// </summary>
        /// <remarks>
        /// If this is null, a dialog will prompt the user to choose the initially included processes
        /// from a list that contains all processes from the trace.
        /// If this is NOT null, the 'Choose Process' dialog will never show and the initially included
        /// processes will be all processes with a name in InitiallyIncludedProcesses.
        /// </remarks>
        /// <example>
        /// Let's say these are the processes in the trace: devenv (104), PerfWatson2, (67) devenv (56)
        /// and InitiallyIncludedProcesses = ["devenv", "vswinexpress"].
        /// When the user opens a stack window, the included filter will be set to "^Process32% devenv (104)|^Process32% devenv (56)"
        /// </example>
        public string[] InitiallyIncludedProcesses { get; set; }
        /// <summary>
        /// If the stack sources have their first tier being the Process, then SupportsProcesses should be true.  
        /// </summary>
        public virtual bool SupportsProcesses { get { return false; } }
        /// <summary>
        /// If the source logs data from multiple processes, this gives a list
        /// of those processes.  Returning null means you don't support this.  
        /// 
        /// This can take a while.  Don't call on the GUI thread.  
        /// </summary>
        public virtual List<IProcess> GetProcesses(TextWriter log)
        {
            // This can take a while, should not be on GUI thread.  
            Debug.Assert(GuiApp.MainWindow.Dispatcher.Thread != System.Threading.Thread.CurrentThread);

            var dataSource = GetStackSource(DefaultStackSourceName);
            if (dataSource == null)
            {
                return null;
            }

            StackSource stackSource = dataSource.GetStackSource(log);

            // maps call stack indexes to callStack closest to the root.
            var rootMostFrameCache = new Dictionary<StackSourceCallStackIndex, IProcessForStackSource>();
            var processes = new List<IProcess>();

            DateTime start = DateTime.Now;
            stackSource.ForEach(delegate (StackSourceSample sample)
            {
                if (sample.StackIndex != StackSourceCallStackIndex.Invalid)
                {
                    var process = GetProcessFromStack(sample.StackIndex, stackSource, rootMostFrameCache, processes);
                    if (process != null)
                    {
                        process.CPUTimeMSec += sample.Metric;
                        long sampleTicks = start.Ticks + (long)(sample.TimeRelativeMSec * 10000);
                        if (sampleTicks < process.StartTime.Ticks)
                        {
                            process.StartTime = new DateTime(sampleTicks);
                        }

                        if (sampleTicks > process.EndTime.Ticks)
                        {
                            process.EndTime = new DateTime(sampleTicks);
                        }

                        Debug.Assert(process.EndTime >= process.StartTime);
                    }
                }
            });
            processes.Sort();
            if (processes.Count == 0)
            {
                processes = null;
            }

            return processes;
        }

        public virtual string Title
        {
            get
            {
                // Arrange the title putting most descriptive inTemplateion first.  
                var fullName = m_filePath;
                Match m = Regex.Match(fullName, @"(([^\\]+)\\)?([^\\]+)$");
                return m.Groups[3].Value + " in " + m.Groups[2].Value + " (" + fullName + ")";
            }
        }

        public SymbolReader GetSymbolReader(TextWriter log, SymbolReaderOptions symbolFlags = SymbolReaderOptions.None)
        {
            return App.GetSymbolReader(FilePath, symbolFlags);
        }
        public virtual void LookupSymbolsForModule(string simpleModuleName, TextWriter log, int processId = 0)
        {
            throw new ApplicationException("This file type does not support lazy symbol resolution.");
        }

        // Things a subclass should be overriding 
        /// <summary>
        /// The name of the file format.
        /// </summary>
        public abstract string FormatName { get; }
        /// <summary>
        /// The file extensions that this format knows how to read.  
        /// </summary>
        public abstract string[] FileExtensions { get; }
        /// <summary>
        /// Implements the open operation.   Executed NOT on the GUI thread.   Typically returns null
        /// which means the open is complete.  If some operation has to be done on the GUI thread afterward
        /// then  action(doAfter) continuation is returned.  This function is given an addition action 
        /// that must be done at the every end.   
        /// </summary>
        public virtual Action<Action> OpenImpl(Window parentWindow, StatusBar worker)
        {
            return delegate (Action doAfter)
            {
                // By default we have a singleton source (which we don't show on the GUI) and we immediately open it
                m_singletonStackSource = new PerfViewStackSource(this, "");
                m_singletonStackSource.Open(parentWindow, worker);
                doAfter?.Invoke();
            };
        }

        protected internal virtual void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow) { }
        /// <summary>
        /// Allows you to do a first action after everything is done.  
        /// </summary>
        protected internal virtual void FirstAction(StackWindow stackWindow) { }
        protected internal virtual StackSource OpenStackSourceImpl(
            string streamName, TextWriter log, double startRelativeMSec = 0, double endRelativeMSec = double.PositiveInfinity, Predicate<TraceEvent> predicate = null)
        {
            return null;
        }
        /// <summary>
        /// Simplified form, you should implement one overload or the other.  
        /// </summary>
        protected internal virtual StackSource OpenStackSourceImpl(TextWriter log) { return null; }
        protected internal virtual EventSource OpenEventSourceImpl(TextWriter log) { return null; }

        // Helper functions for ConfigStackWindowImpl (we often configure windows the same way)
        internal static void ConfigureAsMemoryWindow(string stackSourceName, StackWindow stackWindow)
        {
            bool walkableObjectView = HeapDumpPerfViewFile.Gen0WalkableObjectsViewName.Equals(stackSourceName) || HeapDumpPerfViewFile.Gen1WalkableObjectsViewName.Equals(stackSourceName);

            // stackWindow.ScalingPolicy = ScalingPolicyKind.TimeMetric;
            stackWindow.IsMemoryWindow = true;
            stackWindow.FoldPercentTextBox.Text = stackWindow.GetDefaultFoldPercentage();
            var defaultFold = "[];mscorlib!String";
            if (!walkableObjectView)
            {
                stackWindow.FoldRegExTextBox.Text = defaultFold;
            }
            stackWindow.FoldRegExTextBox.Items.Insert(0, defaultFold);

            var defaultExclusions = "[not reachable from roots]";
            stackWindow.ExcludeRegExTextBox.Text = defaultExclusions;
            stackWindow.ExcludeRegExTextBox.Items.Insert(0, defaultExclusions);

            stackWindow.GroupRegExTextBox.Items.Insert(0, "[group modules]           {%}!->module $1");
            stackWindow.GroupRegExTextBox.Items.Insert(0, "[group full path module entries]  {*}!=>module $1");
            stackWindow.GroupRegExTextBox.Items.Insert(0, "[group module entries]  {%}!=>module $1");

            var defaultGroup = @"[group Framework] mscorlib!=>LIB;System%!=>LIB;";
            if (!walkableObjectView)
            {
                stackWindow.GroupRegExTextBox.Text = defaultGroup;
            }
            stackWindow.GroupRegExTextBox.Items.Insert(0, defaultGroup);

            stackWindow.PriorityTextBox.Text = Graphs.MemoryGraphStackSource.DefaultPriorities;

            stackWindow.RemoveColumn("WhenColumn");
            stackWindow.RemoveColumn("WhichColumn");
            stackWindow.RemoveColumn("FirstColumn");
            stackWindow.RemoveColumn("LastColumn");
            stackWindow.RemoveColumn("IncAvgColumn");
        }
        internal static void ConfigureAsEtwStackWindow(StackWindow stackWindow, bool removeCounts = true, bool removeScenarios = true, bool removeIncAvg = true, bool windows = true)
        {
            if (removeCounts)
            {
                stackWindow.RemoveColumn("IncCountColumn");
                stackWindow.RemoveColumn("ExcCountColumn");
                stackWindow.RemoveColumn("FoldCountColumn");
            }

            if (removeScenarios)
            {
                stackWindow.RemoveColumn("WhichColumn");
            }

            if (removeIncAvg)
            {
                stackWindow.RemoveColumn("IncAvgColumn");
            }

            var defaultEntry = stackWindow.GetDefaultFoldPat();
            stackWindow.FoldRegExTextBox.Text = defaultEntry;
            stackWindow.FoldRegExTextBox.Items.Clear();
            if (!string.IsNullOrWhiteSpace(defaultEntry))
            {
                stackWindow.FoldRegExTextBox.Items.Add(defaultEntry);
            }

            if (windows && defaultEntry != "ntoskrnl!%ServiceCopyEnd")
            {
                stackWindow.FoldRegExTextBox.Items.Add("ntoskrnl!%ServiceCopyEnd");
            }

            ConfigureGroupRegExTextBox(stackWindow, windows);
        }

        internal static void ConfigureGroupRegExTextBox(StackWindow stackWindow, bool windows)
        {
            stackWindow.GroupRegExTextBox.Text = stackWindow.GetDefaultGroupPat();
            stackWindow.GroupRegExTextBox.Items.Clear();
            stackWindow.GroupRegExTextBox.Items.Add(@"[no grouping]");
            if (windows)
            {
                stackWindow.GroupRegExTextBox.Items.Add(@"[group CLR/OS entries] \Temporary ASP.NET Files\->;v4.0.30319\%!=>CLR;v2.0.50727\%!=>CLR;mscoree=>CLR;\mscorlib.*!=>LIB;\System.Xaml.*!=>WPF;\System.*!=>LIB;Presentation%=>WPF;WindowsBase%=>WPF;system32\*!=>OS;syswow64\*!=>OS;{%}!=> module $1");
            }

            stackWindow.GroupRegExTextBox.Items.Add(@"[group modules]           {%}!->module $1");
            stackWindow.GroupRegExTextBox.Items.Add(@"[group module entries]  {%}!=>module $1");
            stackWindow.GroupRegExTextBox.Items.Add(@"[group full path module entries]  {*}!=>module $1");
            stackWindow.GroupRegExTextBox.Items.Add(@"[group class entries]     {%!*}.%(=>class $1;{%!*}::=>class $1");
            stackWindow.GroupRegExTextBox.Items.Add(@"[group classes]            {%!*}.%(->class $1;{%!*}::->class $1");
            stackWindow.GroupRegExTextBox.Items.Add(@"[fold threads]            Thread -> AllThreads");
        }

        // ideally this function would not exist.  Does the open logic on the current thread (likely GUI thread)
        // public is consumed by external extensions
        public void OpenWithoutWorker()
        {
            OpenWithoutWorker(GuiApp.MainWindow, GuiApp.MainWindow.StatusBar);
        }

        internal void OpenWithoutWorker(Window parentWindow, StatusBar worker)
        {
            OpenImpl(parentWindow, worker);
        }

        // This is the global list of all known file types.  
        private static List<PerfViewFile> Formats = new List<PerfViewFile>()
        {
            new CSVPerfViewData(),
            new ETLPerfViewData(),
            new WTPerfViewFile(),
            new ClrProfilerCodeSizePerfViewFile(),
            new ClrProfilerAllocStacksPerfViewFile(),
            new XmlPerfViewFile(),
            new ClrProfilerHeapPerfViewFile(),
            new PdbScopePerfViewFile(),
            new VmmapPerfViewFile(),
            new DebuggerStackPerfViewFile(),
            new HeapDumpPerfViewFile(),
            new ProcessDumpPerfViewFile(),
            new ScenarioSetPerfViewFile(),
            new OffProfPerfViewFile(),
            new DiagSessionPerfViewFile(),
            new LinuxPerfViewData(),
            new XmlTreeFile(),
            new EventPipePerfViewData()
        };

        #region private
        internal void StackSourceClosing(PerfViewStackSource dataSource)
        {
            // TODO FIX NOW.   WE need reference counting 
            if (m_singletonStackSource != null)
            {
                m_opened = false;
            }
        }

        protected PerfViewFile() { }        // Don't allow public default constructor
        /// <summary>
        /// Gets the process from the stack.  It assumes that the stack frame closest to the root is the process
        /// name and returns an IProcess representing it.  
        /// </summary>
        private IProcessForStackSource GetProcessFromStack(StackSourceCallStackIndex callStack, StackSource stackSource,
            Dictionary<StackSourceCallStackIndex, IProcessForStackSource> rootMostFrameCache, List<IProcess> processes)
        {
            Debug.Assert(callStack != StackSourceCallStackIndex.Invalid);

            IProcessForStackSource ret;
            if (rootMostFrameCache.TryGetValue(callStack, out ret))
            {
                return ret;
            }

            var caller = stackSource.GetCallerIndex(callStack);
            if (caller == StackSourceCallStackIndex.Invalid)
            {
                string topCallStackStr = stackSource.GetFrameName(stackSource.GetFrameIndex(callStack), true);

                if (GetProcessForStackSourceFromTopCallStackFrame(topCallStackStr, out ret))
                {
                    processes.Add(ret);
                }
            }
            else
            {
                ret = GetProcessFromStack(caller, stackSource, rootMostFrameCache, processes);
            }

            rootMostFrameCache.Add(callStack, ret);
            return ret;
        }

        internal virtual string GetProcessIncPat(IProcess process) => $"Process% {process.Name} ({process.ProcessID})";

        /// <summary>
        /// This is and ugly routine that scrapes the data to find the full path (without the .exe extension) of the
        /// exe in the program.   It may fail (return nulls).   
        /// </summary>
        internal virtual string FindExeName(string incPat)
        {
            string procName = null;
            if (!string.IsNullOrWhiteSpace(incPat))
            {
                Match m = Regex.Match(incPat, @"^Process%\s+([^();]+[^(); ])", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    procName = m.Groups[1].Value;
                }
            }
            return procName;
        }

        internal virtual bool GetProcessForStackSourceFromTopCallStackFrame(string topCallStackStr, out IProcessForStackSource result)
        {
            Match m = Regex.Match(topCallStackStr, @"^Process\d*\s+([^()]*?)\s*(\(\s*(\d+)\s*\))?\s*$");
            if (m.Success)
            {
                var processIDStr = m.Groups[3].Value;
                var processName = m.Groups[1].Value;
                if (processName.Length == 0)
                {
                    processName = "(" + processIDStr + ")";
                }

                result = new IProcessForStackSource(processName);
                if (int.TryParse(processIDStr, out int processID))
                {
                    result.ProcessID = processID;
                }

                return true;
            }

            result = null;
            return false;
        }

        protected bool IsMyFormat(string fileName)
        {
            foreach (var extension in FileExtensions)
            {
                if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        protected bool m_opened;
        protected internal DateTime m_utcLastWriteAtOpen;

        // If we have only one stack source put it here
        protected PerfViewStackSource m_singletonStackSource;

        private static Dictionary<string, PerfViewFile> s_internTable = new Dictionary<string, PerfViewFile>();
        #endregion
    }
}