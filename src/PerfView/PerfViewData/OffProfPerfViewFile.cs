using System.IO;
using Diagnostics.Tracing.StackSources;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace PerfView.PerfViewData
{
    internal class OffProfPerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return "Office Profiler"; } }
        public override string[] FileExtensions { get { return new string[] { ".offtree" }; } }

        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            return new OffProfStackSource(FilePath);
        }
        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            stackWindow.FoldPercentTextBox.Text = stackWindow.GetDefaultFoldPercentage();
            stackWindow.RemoveColumn("WhenColumn");
            stackWindow.RemoveColumn("FirstColumn");
            stackWindow.RemoveColumn("LastColumn");
        }
    }
}