using System;
using System.Windows;
using System.Windows.Media;

namespace PerfView.PerfViewData
{
    public class PerfViewReport : PerfViewTreeItem
    {
        // Used to create a template for all PerfViewFiles
        public PerfViewReport(string name, Action<string, string> onOpen)
        {
            Name = name;
            m_onOpen = onOpen;
        }

        // Used to clone a PerfViewReport and specialize it to a particular data file.  
        internal PerfViewReport(PerfViewReport template, PerfViewFile dataFile)
        {
            Name = template.Name;
            m_onOpen = template.m_onOpen;
            DataFile = dataFile;
        }

        #region overrides
        public virtual string Title { get { return Name + " for " + DataFile.Title; } }
        public PerfViewFile DataFile { get; private set; }
        public override string FilePath { get { return DataFile.FilePath; } }
        public override void Open(Window parentWindow, StatusBar worker, Action doAfter)
        {
            m_onOpen(DataFile.FilePath, Name);
        }
        public override void Close() { }
        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["HtmlReportBitmapImage"] as ImageSource; } }
        #endregion
        #region private
        private Action<string, string> m_onOpen;
        #endregion
    }
}