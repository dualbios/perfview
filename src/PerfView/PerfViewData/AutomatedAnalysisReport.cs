using System.IO;
using Microsoft.Diagnostics.Tracing.AutomatedAnalysis;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace PerfView.PerfViewData
{
    public class AutomatedAnalysisReport : PerfViewHtmlReport
    {
        public AutomatedAnalysisReport(PerfViewFile dataFile) : base(dataFile, "Automatic CPU Analysis") { }

        protected override void WriteHtmlBody(TraceLog dataFile, TextWriter writer, string fileName, TextWriter log)
        {
            AutomatedAnalysisManager manager = new AutomatedAnalysisManager(dataFile, log, App.GetSymbolReader(dataFile.FilePath));
            manager.GenerateReport(writer);
        }
    }
}