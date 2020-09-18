using PerfView.PerfViewData;
using Xunit;

namespace PerfView.TestUtilities
{
    public class ETLPerfViewDataTests
    {
        [Fact]
        public void test1()
        {
            ETLPerfViewDataForTest etlPerfViewData = new ETLPerfViewDataForTest();

            etlPerfViewData.OpenImpl(null);
        }


    }
}