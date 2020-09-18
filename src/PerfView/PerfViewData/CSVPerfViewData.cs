using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Stacks;
using EventSource = EventSources.EventSource;

namespace PerfView.PerfViewData
{
    /// <summary>
    /// These are the data Templates that PerfView understands.  
    /// </summary>
    internal class CSVPerfViewData : PerfViewFile
    {
        public override string FormatName { get { return "XPERF CSV"; } }
        public override string[] FileExtensions { get { return new string[] { ".csvz", ".etl.csv" }; } }

        public override Action<Action> OpenImpl(Window parentWindow, StatusBar worker)
        {
            m_csvReader = new CSVReader.CSVReader(FilePath);
            m_Children = new List<PerfViewTreeItem>();

            m_Children.Add(new PerfViewEventSource(this));
            foreach (var stackEventName in m_csvReader.StackEventNames)
            {
                m_Children.Add(new PerfViewStackSource(this, stackEventName));
            }

            return null;
        }
        public override void Close()
        {
            if (m_csvReader != null)
            {
                m_csvReader.Dispose();
                m_csvReader = null;
            }
            base.Close();
        }
        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            stackWindow.FoldPercentTextBox.Text = stackWindow.GetDefaultFoldPercentage();
            ConfigureAsEtwStackWindow(stackWindow, stackSourceName == "SampledProfile");
        }
        public override bool SupportsProcesses { get { return true; } }
        protected internal override StackSource OpenStackSourceImpl(
            string streamName, TextWriter log, double startRelativeMSec = 0, double endRelativeMSec = double.PositiveInfinity, Predicate<TraceEvent> predicate = null)
        {
            // TODO: predicate not used
            return m_csvReader.StackSamples(streamName, startRelativeMSec, endRelativeMSec);
        }
        protected internal override EventSource OpenEventSourceImpl(TextWriter log)
        {
            return m_csvReader.GetEventSource();
        }
        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["FileBitmapImage"] as ImageSource; } }

        #region private
        private CSVReader.CSVReader m_csvReader;
        #endregion
    }
}