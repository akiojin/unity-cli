using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityCliBridge.Evaluation;
using UnityCliBridge.Helpers;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityCliBridge.Tests.Editor
{
    public class ObjectIdentityTests
    {
        private GameObject first;
        private GameObject second;

        [SetUp]
        public void SetUp()
        {
            first = new GameObject("IdentityFirst");
            second = new GameObject("IdentitySecond");
        }

        [TearDown]
        public void TearDown()
        {
            RuntimeChangeJournal.RestoreAll();
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
        }

        [Test]
        public void IntegerIdentityMatchesLegacyApiAndDistinguishesObjects()
        {
            // Reflection is intentional: the installed legacy implementation is
            // our compatibility oracle even when direct calls are compile errors.
            var legacy = typeof(Object).GetMethod("GetInstanceID", BindingFlags.Public | BindingFlags.Instance);
            Assert.That(legacy, Is.Not.Null);
            var id = ObjectIdentity.GetInstanceId(first);
            Assert.That(id, Is.EqualTo((int)legacy.Invoke(first, null)));
            Assert.That(id, Is.EqualTo(ObjectIdentity.GetInstanceId(first)));
            Assert.That(id, Is.Not.EqualTo(ObjectIdentity.GetInstanceId(second)));
        }

        [Test]
        public void EvalObjectIdentityRemainsAJsonInteger()
        {
            var json = EvalResultSerializer.Serialize(first);
            Assert.That(json["instanceId"].Type, Is.EqualTo(JTokenType.Integer));
            Assert.That(json["instanceId"].Value<int>(), Is.EqualTo(ObjectIdentity.GetInstanceId(first)));
        }

        [Test]
        public void JournalRestoresDistinctObjectsAndKeepsTheFirstSnapshot()
        {
            first.transform.position = Vector3.one;
            second.transform.position = Vector3.right;
            RuntimeChangeJournal.Record(first);
            RuntimeChangeJournal.Record(second);
            first.name = "changed";
            first.transform.position = Vector3.zero;
            RuntimeChangeJournal.Record(first);
            second.SetActive(false);
            second.transform.position = Vector3.zero;

            RuntimeChangeJournal.RestoreAll();

            Assert.That(first.name, Is.EqualTo("IdentityFirst"));
            Assert.That(first.transform.position, Is.EqualTo(Vector3.one));
            Assert.That(second.activeSelf, Is.True);
            Assert.That(second.transform.position, Is.EqualTo(Vector3.right));
        }
    }
}
