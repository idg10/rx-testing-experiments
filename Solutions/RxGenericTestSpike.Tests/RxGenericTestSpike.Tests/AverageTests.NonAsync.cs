using RxGenericTestSpike.SharedTests;

namespace RxGenericTestSpike.Tests
{
    [TestClass]
    public class AverageNonAsync
    {
        [TestMethod]
        public void Average_Int32_Some()
        {
            AverageTests.Average_Int32_Some(ObservableQueryToSync.Rewrite);
        }
    }
}