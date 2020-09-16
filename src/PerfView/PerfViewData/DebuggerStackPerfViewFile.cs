using System.IO;
using Diagnostics.Tracing.StackSources;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace PerfView.PerfViewData
{
    internal class DebuggerStackPerfViewFile : PerfViewFile
    {
        public override string FormatName
        {
            get { return "Windbg kc Call stack"; }
        }

        public override string[] FileExtensions
        {
            get { return new string[] { ".cdbStack", ".windbgStack" }; }
        }

        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            return new DebuggerStackSource(FilePath);
        }

        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            stackWindow.RemoveColumn("IncCountColumn");
            stackWindow.RemoveColumn("ExcCountColumn");
            stackWindow.RemoveColumn("FoldCountColumn");
        }
    }
}