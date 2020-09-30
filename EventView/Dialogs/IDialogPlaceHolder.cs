using System;
using System.Threading.Tasks;

namespace EventView.Dialogs
{
    public interface IDialogPlaceHolder
    {
        IDialog DialogContainer { get; set; }
        Task Show(IDialog dialog, Action<IDialog> okAction);
    }
}