using System;
using System.Windows;
using System.Windows.Media;

namespace PerfView.PerfViewData
{
    public class ProcessDumpPerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return "Process Dump"; } }
        public override string[] FileExtensions { get { return new string[] { ".dmp" }; } }

        public override void Open(Window parentWindow, StatusBar worker, Action doAfter = null)
        {
            App.CommandLineArgs.ProcessDumpFile = FilePath;
            GuiApp.MainWindow.TakeHeapShapshot(null);
        }
        public override void Close() { }
        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["FileBitmapImage"] as ImageSource; } }

        public const string DiagSessionIdentity = "Microsoft.Diagnostics.Minidump";
    }
}