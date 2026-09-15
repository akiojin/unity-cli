using NUnit.Framework;
using UnityCliBridge.Helpers;
using UnityEngine;

namespace UnityCliBridge.Tests.Editor.Helpers
{
    public class RuntimeChangeJournalTests
    {
        private GameObject _first;
        private GameObject _second;

        [SetUp]
        public void SetUp()
        {
            RuntimeChangeJournal.RestoreAll();
            _first = new GameObject("RuntimeJournalFirst");
            _second = new GameObject("RuntimeJournalSecond");
        }

        [TearDown]
        public void TearDown()
        {
            RuntimeChangeJournal.RestoreAll();
            if (_first != null) Object.DestroyImmediate(_first);
            if (_second != null) Object.DestroyImmediate(_second);
        }

        [Test]
        public void Record_RepeatedRecordingRestoresTheFirstSnapshot()
        {
            var position = new Vector3(1f, 2f, 3f);
            var rotation = Quaternion.Euler(10f, 20f, 30f);
            var scale = new Vector3(2f, 3f, 4f);
            _first.transform.position = position;
            _first.transform.rotation = rotation;
            _first.transform.localScale = scale;

            RuntimeChangeJournal.Record(_first);
            _first.name = "ChangedOnce";
            _first.SetActive(false);
            _first.transform.position = Vector3.zero;
            _first.transform.rotation = Quaternion.identity;
            _first.transform.localScale = Vector3.one;
            RuntimeChangeJournal.Record(_first);
            _first.name = "ChangedTwice";

            RuntimeChangeJournal.RestoreAll();

            Assert.AreEqual("RuntimeJournalFirst", _first.name);
            Assert.IsTrue(_first.activeSelf);
            Assert.AreEqual(position, _first.transform.position);
            Assert.Less(Quaternion.Angle(rotation, _first.transform.rotation), 0.01f);
            Assert.AreEqual(scale, _first.transform.localScale);
        }

        [Test]
        public void RestoreAll_RestoresDistinctObjectsIndependently()
        {
            _first.name = "SameName";
            _second.name = "SameName";
            _first.transform.position = Vector3.left;
            _second.transform.position = Vector3.right;
            RuntimeChangeJournal.Record(_first);
            RuntimeChangeJournal.Record(_second);
            _first.transform.position = Vector3.up;
            _second.transform.position = Vector3.down;

            RuntimeChangeJournal.RestoreAll();

            Assert.AreEqual(Vector3.left, _first.transform.position);
            Assert.AreEqual(Vector3.right, _second.transform.position);
        }

        [Test]
        public void RestoreAll_SkipsDestroyedObjectsAndRestoresSurvivors()
        {
            RuntimeChangeJournal.Record(_first);
            RuntimeChangeJournal.Record(_second);
            Object.DestroyImmediate(_first);
            _second.name = "Changed";

            Assert.DoesNotThrow(() => RuntimeChangeJournal.RestoreAll());
            Assert.AreEqual("RuntimeJournalSecond", _second.name);
        }

        [Test]
        public void RestoreAll_ClearsSnapshotsBeforeTheNextRecording()
        {
            RuntimeChangeJournal.Record(_first);
            _first.name = "Changed";
            RuntimeChangeJournal.RestoreAll();
            _first.name = "NextBaseline";

            RuntimeChangeJournal.RestoreAll();
            Assert.AreEqual("NextBaseline", _first.name);

            RuntimeChangeJournal.Record(_first);
            _first.name = "ChangedAgain";
            RuntimeChangeJournal.RestoreAll();
            Assert.AreEqual("NextBaseline", _first.name);
        }
    }
}
