using System;
using System.Collections;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;

namespace UnityCliBridge.Tests.Helpers
{
    internal static class TaskTestUtility
    {
        // The Test Framework bundled with Unity 2022.3 (1.1.x) cannot run Task-returning tests.
        // Drive the async body from a [UnityTest] instead, yielding editor frames so main-thread
        // continuations and EditorApplication.update keep running, then rethrow the original
        // exception so assertion failures and Assert.Ignore keep their NUnit result.
        public static IEnumerator Await(Func<Task> body, int timeoutMs = 30000)
        {
            var task = body();
            var elapsed = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                if (elapsed.ElapsedMilliseconds > timeoutMs)
                {
                    Assert.Fail($"Async test body did not complete within {timeoutMs} ms");
                }
                yield return null;
            }
            task.GetAwaiter().GetResult();
        }
    }
}
