using System;
using System.Windows;

namespace PerfView.PerfViewData
{
    internal class PerfViewUserFile : PerfViewFile
    {
        public PerfViewUserFile(string formatName, string[] fileExtensions)
        {
            m_formatName = formatName;
            m_fileExtensions = fileExtensions;
        }

        public override string FormatName { get { return m_formatName; } }
        public override string[] FileExtensions { get { return m_fileExtensions; } }
        public override Action<Action> OpenImpl(Window parentWindow, StatusBar worker) { return null; }

        #region private
        private string m_formatName;
        private string[] m_fileExtensions;
        #endregion
    }
}