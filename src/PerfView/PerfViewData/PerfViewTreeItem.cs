using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace PerfView.PerfViewData
{
    /// <summary>
    /// PerfViewTreeItem is a common base class for something that can be represented in the 
    /// TreeView GUI.  in particular it has a name, and children.   This includes both
    /// file directories as well as data file (that might have multiple sources inside them)
    /// </summary>
    public abstract class PerfViewTreeItem : INotifyPropertyChanged
    {
        /// <summary>
        /// The name to place in the treeview (should be short).  
        /// </summary>
        public string Name { get; protected internal set; }
        /// <summary>
        /// If the entry should have children in the TreeView, this is them.
        /// </summary>
        public virtual IList<PerfViewTreeItem> Children { get { return m_Children; } }
        /// <summary>
        /// All items have some sort of file path that is associated with them.  
        /// </summary>
        public virtual string FilePath { get { return m_filePath; } }
        public virtual string HelpAnchor { get { return GetType().Name; } }

        public bool IsExpanded { get { return m_isExpanded; } set { m_isExpanded = value; FirePropertyChanged("IsExpanded"); } }
        public bool IsSelected { get { return m_isSelected; } set { m_isSelected = value; FirePropertyChanged("IsSelected"); } }

        /// <summary>
        /// Open the file (This might be expensive (but maybe not).  It should not be run on
        /// the GUI thread.  This should populate the Children property if that is appropriate.  
        /// 
        /// if 'doAfter' is present, it will be run after the window has been opened.   It is always
        /// executed on the GUI thread.  
        /// </summary>
        public abstract void Open(Window parentWindow, StatusBar worker, Action doAfter = null);
        /// <summary>
        /// Once something is opened, it should be closed.  
        /// </summary>
        public abstract void Close();

        // Support so that we can update children and the view will update
        public event PropertyChangedEventHandler PropertyChanged;
        protected void FirePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// The Icon to show next to the entry.  
        /// </summary>
        public virtual ImageSource Icon { get { return GuiApp.MainWindow.Resources["StackSourceBitmapImage"] as ImageSource; } }
        #region private
        public override string ToString() { if (FilePath != null) { return FilePath; } return Name; }

        protected List<PerfViewTreeItem> m_Children;
        protected List<PerfViewReport> m_UserDeclaredChildren;
        protected string m_filePath;
        private bool m_isExpanded;
        private bool m_isSelected;
        #endregion
    }
}