using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityCliBridge.Tests.PlayMode
{
    // Keep exactly two tests: scripts/e2e-test-results.sh checks leaf-only totals.
    public class TestResultCountingPlayModeTests
    {
        [UnityTest]
        public IEnumerator FrameAdvances()
        {
            var frame = Time.frameCount;
            yield return null;
            Assert.Greater(Time.frameCount, frame);
        }

        [UnityTest]
        public IEnumerator RigidbodyFalls()
        {
            var bodyObject = new GameObject("TestResultCountingBody");
            try
            {
                bodyObject.transform.position = Vector3.up * 5;
                var body = bodyObject.AddComponent<Rigidbody>();
                for (var i = 0; i < 5; i++)
                    yield return new WaitForFixedUpdate();
                Assert.Less(body.position.y, 5);
            }
            finally
            {
                Object.Destroy(bodyObject);
            }
        }
    }
}
