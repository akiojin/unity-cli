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
        public void GetCompilationState_ShouldIncludeConsoleCountFields()
        {
            var result = CompilationHandler.GetCompilationState(new Newtonsoft.Json.Linq.JObject());
            var obj = Newtonsoft.Json.Linq.JObject.FromObject(result);

            Assert.IsTrue(obj.ContainsKey("consoleErrorCount"), "consoleErrorCount field missing");
            Assert.IsTrue(obj.ContainsKey("consoleWarningCount"), "consoleWarningCount field missing");
            Assert.GreaterOrEqual(obj["consoleErrorCount"].ToObject<int>(), 0);
            Assert.GreaterOrEqual(obj["consoleWarningCount"].ToObject<int>(), 0);
        }

        [Test]
        public void GetCompilationState_ErrorCount_ShouldNotExceedConsoleErrorCount()
        {
            var result = CompilationHandler.GetCompilationState(new Newtonsoft.Json.Linq.JObject());
            var obj = Newtonsoft.Json.Linq.JObject.FromObject(result);

            int compErrors = obj["errorCount"].ToObject<int>();
            int consErrors = obj["consoleErrorCount"].ToObject<int>();

            // Compilation errors are a subset of console errors
            Assert.LessOrEqual(compErrors, consErrors,
                "Compilation errorCount should not exceed consoleErrorCount");
        }

        [Test]
        public void GetCompilationState_ShouldIncludeLastAssemblyWriteTimeOrNull()
        {
            var result = CompilationHandler.GetCompilationState(new Newtonsoft.Json.Linq.JObject());
            var obj = Newtonsoft.Json.Linq.JObject.FromObject(result);

            // allow null when assemblies not present, but key should exist
            Assert.IsTrue(obj.ContainsKey("lastCompilationTime"));
        }

        [Test]
        public void GetCompilationState_WithIncludeMessages_ShouldContainConsoleCountFields()
        {
            var param = new Newtonsoft.Json.Linq.JObject { ["includeMessages"] = true };
            var result = CompilationHandler.GetCompilationState(param);
            var obj = Newtonsoft.Json.Linq.JObject.FromObject(result);

            Assert.IsTrue(obj.ContainsKey("consoleErrorCount"), "consoleErrorCount missing in includeMessages response");
            Assert.IsTrue(obj.ContainsKey("consoleWarningCount"), "consoleWarningCount missing in includeMessages response");
        }
    }
}
