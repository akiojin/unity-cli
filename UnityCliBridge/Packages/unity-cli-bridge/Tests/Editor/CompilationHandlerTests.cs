using NUnit.Framework;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests.Editor
{
    public class CompilationHandlerTests
    {
        [Test]
        public void GetCompilationState_ShouldReturnCountsWithoutException()
        {
            var result = CompilationHandler.GetCompilationState(new Newtonsoft.Json.Linq.JObject());
            var obj = Newtonsoft.Json.Linq.JObject.FromObject(result);

            Assert.IsTrue(obj.ContainsKey("errorCount"));
            Assert.IsTrue(obj.ContainsKey("warningCount"));
            Assert.GreaterOrEqual(obj["errorCount"].ToObject<int>(), 0);
            Assert.GreaterOrEqual(obj["warningCount"].ToObject<int>(), 0);
        }

        [Test]
        public void GetCompilationState_ShouldIncludeLastAssemblyWriteTimeOrNull()
        {
            var result = CompilationHandler.GetCompilationState(new Newtonsoft.Json.Linq.JObject());
            var obj = Newtonsoft.Json.Linq.JObject.FromObject(result);

            // allow null when assemblies not present, but key should exist
            Assert.IsTrue(obj.ContainsKey("lastCompilationTime"));
        }
    }
}
