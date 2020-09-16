using System.IO;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace PerfView.PerfViewData
{
    internal class ClrProfilerCodeSizePerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return "Clr Profiler Code Size"; } }
        public override string[] FileExtensions { get { return new string[] { ".codesize" }; } }

        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            var codeSizeSource = new ClrProfiler.ClrProfilerMethodSizeStackSource(FilePath);
            log.WriteLine("Info Read:  method called:{0}  totalILSize called:{1}  totalCalls:{2}",
                codeSizeSource.TotalMethodCount, codeSizeSource.TotalMethodSize, codeSizeSource.TotalCalls);
            return codeSizeSource;
        }
        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            var defaultGroup = "[group framework] !System.=> CLR;!Microsoft.=>CLR";
            stackWindow.GroupRegExTextBox.Text = defaultGroup;
            stackWindow.GroupRegExTextBox.Items.Insert(0, defaultGroup);
        }
    }
}