using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace PerfView.PerfViewData
{
    /// <summary>
    /// A PerfViewTreeGroup simply groups other Items.  Thus it has a name, and you use the Children
    /// to add Child nodes to the group.  
    /// </summary>
    public class PerfViewTreeGroup : PerfViewTreeItem
    {
        public PerfViewTreeGroup(string name)
        {
            Name = name;
            m_Children = new List<PerfViewTreeItem>();
        }

        public PerfViewTreeGroup AddChild(PerfViewTreeItem child)
        {
            m_Children.Add(child);
            return this;
        }

        // Groups do no semantic action.   All the work is in the visual GUI part.  
        public override void Open(Window parentWindow, StatusBar worker, Action doAfter = null)
        {
            doAfter?.Invoke();
        }
        public override void Close() { }

        public override IList<PerfViewTreeItem> Children { get { return m_Children; } }

        public override string HelpAnchor { get { return Name.Replace(" ", ""); } }

        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["FolderOpenBitmapImage"] as ImageSource; } }
    }
}