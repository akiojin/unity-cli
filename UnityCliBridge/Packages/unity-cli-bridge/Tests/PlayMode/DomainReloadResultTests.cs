using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityCliBridge.Tests.PlayMode
{
    // Kept deliberately short so the external E2E can exercise the full runner lifecycle.
    public class DomainReloadResultTests
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
            var gameObject = new GameObject("DomainReloadResultTestBody");
            try
            {
                gameObject.transform.position = Vector3.up * 5;
                var body = gameObject.AddComponent<Rigidbody>();
                for (var i = 0; i < 5; i++)
                    yield return new WaitForFixedUpdate();
                Assert.Less(body.position.y, 5);
            }
            finally
            {
                Object.Destroy(gameObject);
            }
        }
    }
}
