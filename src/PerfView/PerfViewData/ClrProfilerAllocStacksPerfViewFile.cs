using System.IO;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace PerfView.PerfViewData
{
    internal class ClrProfilerAllocStacksPerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return "Clr Profiler Alloc"; } }
        public override string[] FileExtensions { get { return new string[] { ".allocStacks" }; } }

        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            return new ClrProfiler.ClrProfilerAllocStackSource(FilePath);
        }
        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            var defaultGroup = "[group framework] !System.=> CLR;!Microsoft.=>CLR";
            stackWindow.GroupRegExTextBox.Text = defaultGroup;
            stackWindow.GroupRegExTextBox.Items.Insert(0, defaultGroup);
        }
    }
}