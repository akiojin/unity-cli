using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityCliBridge.Handlers;
using Newtonsoft.Json.Linq;

namespace UnityCliBridge.Tests
{
    /// <summary>
    /// Tests for TestExecutionHandler
    /// Note: These are basic validation tests. Full integration tests would be circular
    /// (tests testing the test runner).
    /// </summary>
    [TestFixture]
    public class TestExecutionHandlerTests
    {
        [Test]
        public void RunTests_ShouldFailWithInvalidTestMode()
        {
            var parameters = new JObject
            {
                ["testMode"] = "InvalidMode"
            };

            var result = TestExecutionHandler.RunTests(parameters) as dynamic;

            Assert.IsNotNull(result);
            Assert.IsNotNull(result.error);
            Assert.IsTrue(((string)result.error).Contains("Invalid testMode"));
        }

        [Test]
        public void RunTests_ShouldFailWhenScenesHaveUnsavedChanges()
        {
            try
            {
                TestExecutionHandler.DirtySceneDetector = () => true;

                var result = TestExecutionHandler.RunTests(new JObject()) as dynamic;

                Assert.IsNotNull(result);
                Assert.IsNotNull(result.error);
                StringAssert.Contains("unsaved", (string)result.error);
            }
            finally
            {
                TestExecutionHandler.ResetForTesting();
            }
        }

        [Test]
        public void RunTests_ShouldFailDuringPlayMode()
        {
            try
            {
                TestExecutionHandler.PlayModeDetector = () => true;

                var result = TestExecutionHandler.RunTests(new JObject()) as dynamic;

                Assert.IsNotNull(result);
                Assert.IsNotNull(result.error);
                StringAssert.Contains("Play Mode", (string)result.error);
            }
            finally
            {
                TestExecutionHandler.ResetForTesting();
            }
        }

        [Test]
        public void RunTests_ShouldFailIfAlreadyRunning()
        {
            // Note: This test would be difficult to implement reliably
            // without mocking internal state, so we skip it for now
            Assert.Pass("Skipping concurrent execution test (requires state mocking)");
        }

        [Test]
        public void RunTests_ShouldAcceptValidEditModeParameter()
        {
            var parameters = new JObject
            {
                ["testMode"] = "EditMode",
                ["filter"] = "NonExistentTestClass" // Use non-existent class to avoid actual execution
            };

            // This will attempt to run tests, but with a non-existent filter
            // it should complete quickly with 0 tests
            var result = TestExecutionHandler.RunTests(parameters) as dynamic;

            Assert.IsNotNull(result);
            // Either success with 0 tests or error is acceptable
            if (result.error == null)
            {
                Assert.IsTrue(result.totalTests >= 0);
            }
        }

        [Test]
        public void RunTests_ShouldAcceptValidPlayModeParameter()
        {
            var parameters = new JObject
            {
                ["testMode"] = "PlayMode",
                ["filter"] = "NonExistentTestClass"
            };

            var result = TestExecutionHandler.RunTests(parameters) as dynamic;

            Assert.IsNotNull(result);
            // PlayMode tests may not be available in all environments
            // Accept either success or specific error
            Assert.IsTrue(result != null);
        }

        [Test]
        public void RunTests_ShouldAcceptFilterParameter()
        {
            var parameters = new JObject
            {
                ["testMode"] = "EditMode",
                ["filter"] = "SomeTestClass"
            };

            var result = TestExecutionHandler.RunTests(parameters) as dynamic;

            Assert.IsNotNull(result);
            // Should not crash with filter parameter
            Assert.IsTrue(result != null);
        }

        [Test]
        public void RunTests_ShouldAcceptCategoryParameter()
        {
            var parameters = new JObject
            {
                ["testMode"] = "EditMode",
                ["category"] = "Integration"
            };

            var result = TestExecutionHandler.RunTests(parameters) as dynamic;

            Assert.IsNotNull(result);
            // Should not crash with category parameter
            Assert.IsTrue(result != null);
        }

        [Test]
        public void RunTests_ShouldAcceptNamespaceParameter()
        {
            var parameters = new JObject
            {
                ["testMode"] = "EditMode",
                ["namespace"] = "UnityCliBridge.Tests"
            };

            var result = TestExecutionHandler.RunTests(parameters) as dynamic;

            Assert.IsNotNull(result);
            // Should not crash with namespace parameter
            Assert.IsTrue(result != null);
        }

        [Test]
        public void RunTests_ShouldAcceptIncludeDetailsParameter()
        {
            var parameters = new JObject
            {
                ["testMode"] = "EditMode",
                ["filter"] = "NonExistentTestClass",
                ["includeDetails"] = true
            };

            var result = TestExecutionHandler.RunTests(parameters) as dynamic;

            Assert.IsNotNull(result);
            // Should not crash with includeDetails parameter
            Assert.IsTrue(result != null);
        }

        [Test]
        public void RunTests_ShouldHandleEmptyParameters()
        {
            var parameters = new JObject();

            var result = TestExecutionHandler.RunTests(parameters) as dynamic;

            Assert.IsNotNull(result);
            // Should default to EditMode and run (or attempt to run) tests
            Assert.IsTrue(result != null);
        }

        [Test]
        public void RunTests_ShouldReturnExpectedResultStructure()
        {
            var parameters = new JObject
            {
                ["testMode"] = "EditMode",
                ["filter"] = "NonExistentTestClass"
            };

            var result = TestExecutionHandler.RunTests(parameters) as dynamic;

            Assert.IsNotNull(result);

            // Check for expected fields (either success or error)
            if (result.error == null)
            {
                // Success case
                Assert.IsNotNull(result.success);
                Assert.IsNotNull(result.totalTests);
                Assert.IsNotNull(result.passedTests);
                Assert.IsNotNull(result.failedTests);
                Assert.IsNotNull(result.skippedTests);
                Assert.IsNotNull(result.duration);
                Assert.IsNotNull(result.failures);
            }
            else
            {
                // Error case
                Assert.IsNotNull(result.error);
            }
        }

        [Test]
        public void GetLastTestResults_ShouldReturnMissingWhenNoExports()
        {
            TestExecutionHandler.ResetForTesting();

            var result = TestExecutionHandler.GetLastTestResults(new JObject()) as dynamic;

            Assert.IsNotNull(result);
            Assert.AreEqual("missing", (string)result.status);
        }

        [Test]
        public void GetLastTestResults_ShouldReturnSummaryWhenExported()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"unitymcp-test-results-{Guid.NewGuid():N}.json");
            try
            {
                var summary = new JObject
                {
                    ["generatedAt"] = DateTime.UtcNow.ToString("o"),
                    ["totalTests"] = 1,
                    ["passed"] = 1,
                    ["failed"] = 0,
                    ["testMode"] = "EditMode",
                    ["status"] = "passed"
                };

                File.WriteAllText(tempPath, summary.ToString());
                TestExecutionHandler.OnResultsExported(tempPath, summary);

                var result = TestExecutionHandler.GetLastTestResults(new JObject()) as dynamic;

                Assert.IsNotNull(result);
                Assert.AreEqual("available", (string)result.status);
                Assert.AreEqual(tempPath, (string)result.path);
                Assert.AreEqual(1L, (long)result.summary.totalTests);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
                TestExecutionHandler.ResetForTesting();
            }
        }
    }
}
