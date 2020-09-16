using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using EventSources;

namespace PerfView.PerfViewData
{
    public class PerfViewEventSource : PerfViewTreeItem
    {
        public PerfViewEventSource(PerfViewFile dataFile)
        {
            DataFile = dataFile;
            Name = "Events";
        }

        public PerfViewEventSource(ETWEventSource source)
        {
        }
        public virtual string Title { get { return "Events " + DataFile.Title; } }
        public PerfViewFile DataFile { get; private set; }
        public EventWindow Viewer { get; internal set; }
        public virtual EventSource GetEventSource()
        {
            Debug.Assert(m_eventSource != null, "Open must be called first");
            if (m_needClone)
            {
                return m_eventSource.Clone();
            }

            m_needClone = true;
            return m_eventSource;
        }
        public override string FilePath { get { return DataFile.FilePath; } }
        public override void Open(Window parentWindow, StatusBar worker, Action doAfter)
        {
            if (Viewer == null || !DataFile.IsUpToDate)
            {
                worker.StartWork("Opening " + Name, delegate ()
                {
                    if (m_eventSource == null || !DataFile.IsUpToDate)
                    {
                        m_eventSource = DataFile.OpenEventSourceImpl(worker.LogWriter);
                    }

                    worker.EndWork(delegate ()
                    {
                        if (m_eventSource == null)
                        {
                            throw new ApplicationException("Not a file type that supports the EventView.");
                        }

                        Viewer = new EventWindow(parentWindow, this);
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
        public override void Close() { }
        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["EventSourceBitmapImage"] as ImageSource; } }

        #region private
        internal EventSource m_eventSource;     // TODO internal is a hack
        private bool m_needClone;           // After giving this out the first time, we need to clone it 
        #endregion
    }
}