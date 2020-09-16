using System.IO;
using Diagnostics.Tracing.StackSources;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace PerfView.PerfViewData
{
    internal class WTPerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return "CDB WT calls"; } }
        public override string[] FileExtensions { get { return new string[] { ".wt" }; } }

        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            return new WTStackSource(FilePath);
        }
        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            stackWindow.FoldPercentTextBox.Text = stackWindow.GetDefaultFoldPercentage();
            stackWindow.ScalingPolicy = ScalingPolicyKind.TimeMetric;
        }
    }
}