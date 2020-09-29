using System;

namespace EventView.Dialogs
{
    public interface IDialogPlaceHolder
    {
        IDialog DialogContainer { get; set; }
        void Show(IDialog dialog, Action<IDialog> okAction);
    }
}