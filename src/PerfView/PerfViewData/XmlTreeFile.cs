using System.IO;
using Diagnostics.Tracing.StackSources;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace PerfView.PerfViewData
{
    internal class XmlTreeFile : PerfViewFile
    {
        public override string FormatName { get { return "Tree XML FILE"; } }
        public override string[] FileExtensions { get { return new string[] { ".tree.xml" }; } }

        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            return new XmlTreeStackSource(FilePath);
        }
    }
}