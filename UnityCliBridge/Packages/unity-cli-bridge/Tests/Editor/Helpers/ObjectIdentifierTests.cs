using System.Globalization;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityCliBridge.Helpers;
using UnityEngine;

namespace UnityCliBridge.Tests.Editor.Helpers
{
    public class ObjectIdentifierTests
    {
        private GameObject _first;
        private GameObject _second;

        [SetUp]
        public void SetUp()
        {
            _first = new GameObject("ObjectIdentifierFirst");
            _second = new GameObject("ObjectIdentifierSecond");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_first);
            Object.DestroyImmediate(_second);
        }

        [Test]
        public void GetId_PreservesIdentityAcrossChangesAndDistinguishesObjects()
        {
            var original = ObjectIdentifier.GetId(_first);
            _first.name = _second.name;
            _first.SetActive(false);

            Assert.AreEqual(original, ObjectIdentifier.GetId(_first));
            Assert.AreNotEqual(original, ObjectIdentifier.GetId(_second));
#if UNITY_6000_4_OR_NEWER
            Assert.AreEqual(_first.GetEntityId(), original);
#else
            Assert.AreEqual(_first.GetInstanceID(), original);
#endif
        }

        [Test]
        public void ToResponseValue_SerializesTheCompleteNativeIdentifier()
        {
            var response = JObject.FromObject(new { id = ObjectIdentifier.ToResponseValue(_first) });
#if UNITY_6000_4_OR_NEWER
            Assert.AreEqual(JTokenType.String, response["id"].Type);
            var rawId = ulong.Parse(response.Value<string>("id"), CultureInfo.InvariantCulture);
            Assert.AreEqual(_first.GetEntityId(), EntityId.FromULong(rawId));
#else
            Assert.AreEqual(JTokenType.Integer, response["id"].Type);
            Assert.AreEqual(_first.GetInstanceID(), response.Value<int>("id"));
#endif
        }

        [Test]
        public void ToResponseValue_IsIndependentOfCurrentCulture()
        {
            var previousCulture = CultureInfo.CurrentCulture;
            var expected = ObjectIdentifier.ToResponseValue(_first);
            try
            {
                var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                culture.NumberFormat.NegativeSign = "negative";
                CultureInfo.CurrentCulture = culture;

                Assert.AreEqual(expected, ObjectIdentifier.ToResponseValue(_first));
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }
    }
}
