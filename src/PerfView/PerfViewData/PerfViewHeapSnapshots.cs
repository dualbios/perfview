using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using Microsoft.Diagnostics.Tracing.Parsers.JSDumpHeap;

namespace PerfView.PerfViewData
{
    /// <summary>
    /// Represents all the heap snapshots in the trace
    /// </summary>
    public class PerfViewHeapSnapshots : PerfViewTreeItem
    {
        public PerfViewHeapSnapshots(PerfViewFile file)
        {
            Name = "GC Heap Snapshots";
            DataFile = file;
        }

        public virtual string Title { get { return Name + " for " + DataFile.Title; } }
        public PerfViewFile DataFile { get; private set; }
        public override string FilePath { get { return DataFile.FilePath; } }

        /// <summary>
        /// Open the file (This might be expensive (but maybe not).  This should populate the Children property 
        /// too.  
        /// </summary>
        public override void Open(Window parentWindow, StatusBar worker, Action doAfter)
        {
            if (m_Children == null)
            {
                var newChildren = new List<PerfViewTreeItem>();
                worker.StartWork("Searching for heap dumps in " + Name, delegate ()
                {
                    TraceLog traceLog = null;
                    if (DataFile is ETLPerfViewData)
                    {
                        traceLog = ((ETLPerfViewData)DataFile).GetTraceLog(worker.LogWriter);
                    }
                    else if (DataFile is EventPipePerfViewData)
                    {
                        traceLog = ((EventPipePerfViewData)DataFile).GetTraceLog(worker.LogWriter);
                    }
                    var source = traceLog.Events.GetSource();
                    var jsHeapParser = new JSDumpHeapTraceEventParser(source);


                    // For .NET, we are looking for a Gen 2 GC Start that is induced that has GCBulkNodes after it.   
                    var lastGCStartsRelMSec = new Dictionary<int, double>();

                    source.Clr.GCStart += delegate (Microsoft.Diagnostics.Tracing.Parsers.Clr.GCStartTraceData data)
                    {
                        // Look for induced GCs.  and remember their when it happened.    
                        if (data.Depth == 2 && data.Reason == GCReason.Induced)
                        {
                            lastGCStartsRelMSec[data.ProcessID] = data.TimeStampRelativeMSec;
                        }
                    };
                    source.Clr.GCBulkNode += delegate (GCBulkNodeTraceData data)
                    {
                        double lastGCStartRelMSec;
                        if (lastGCStartsRelMSec.TryGetValue(data.ProcessID, out lastGCStartRelMSec))
                        {
                            var processName = "";
                            var process = data.Process();
                            if (process != null)
                            {
                                processName = process.Name;
                            }

                            newChildren.Add(new PerfViewHeapSnapshot(DataFile, data.ProcessID, processName, lastGCStartRelMSec, ".NET"));

                            lastGCStartsRelMSec.Remove(data.ProcessID);     // Remove it since so we ignore the rest of the node events.  
                        }
                    };

                    jsHeapParser.JSDumpHeapEnvelopeStart += delegate (SettingsTraceData data)
                    {
                        var processName = "";
                        var process = data.Process();
                        if (process != null)
                        {
                            processName = process.Name;
                        }

                        newChildren.Add(new PerfViewHeapSnapshot(DataFile, data.ProcessID, processName, data.TimeStampRelativeMSec, "JS"));
                    };
                    source.Process();

                    worker.EndWork(delegate ()
                    {
                        m_Children = newChildren;
                        FirePropertyChanged("Children");
                        doAfter?.Invoke();
                    });
                });
            }

            doAfter?.Invoke();
        }
        /// <summary>
        /// Close the file
        /// </summary>
        public override void Close() { }

        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["FolderOpenBitmapImage"] as ImageSource; } }
    }
}