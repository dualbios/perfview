using System.Collections.Generic;

namespace EventView.Dialogs
{
    public interface ITabHolder
    {
        IEnumerable<IDataViewer> TabItems { get; }

        void Add(IDataViewer dataViewer);
    }
}