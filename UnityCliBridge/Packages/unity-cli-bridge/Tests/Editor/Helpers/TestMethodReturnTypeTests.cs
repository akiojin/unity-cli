using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace UnityCliBridge.Tests.Helpers
{
    [TestFixture]
    public class TestMethodReturnTypeTests
    {
        // The Test Framework bundled with Unity 2022.3 (1.1.x) rejects test methods that
        // return Task ("Method has non-void return value"). Async bodies must run through
        // TaskTestUtility.Await from a [UnityTest] IEnumerator instead.
        [Test]
        public void NoTestMethodReturnsTask()
        {
            var offenders = typeof(TestMethodReturnTypeTests).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(m => m.IsDefined(typeof(TestAttribute), false)
                    || m.IsDefined(typeof(TestCaseAttribute), false)
                    || m.IsDefined(typeof(UnityTestAttribute), false))
                .Where(m => typeof(Task).IsAssignableFrom(m.ReturnType))
                .Select(m => m.DeclaringType.FullName + "." + m.Name)
                .ToArray();

            CollectionAssert.IsEmpty(offenders, "Task-returning tests fail on Unity 2022.3: " + string.Join(", ", offenders));
        }
    }
}
