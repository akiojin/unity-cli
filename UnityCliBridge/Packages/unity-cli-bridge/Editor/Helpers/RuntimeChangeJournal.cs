using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityCliBridge.Helpers
{
    [InitializeOnLoad]
    public static class RuntimeChangeJournal
    {
        private class Snapshot
        {
            public string name;
            public bool activeSelf;
            public Vector3 position;
            public Vector3 euler;
            public Vector3 scale;
            public WeakReference<GameObject> weak;
        }

        private static readonly Dictionary<int, Snapshot> _snapshots = new Dictionary<int, Snapshot>();

        static RuntimeChangeJournal()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode)
            {
                RestoreAll();
            }
        }

        public static void Record(GameObject go)
        {
            if (go == null) return;
            var id = go.GetInstanceID();
            if (_snapshots.ContainsKey(id)) return;
            var t = go.transform;
            _snapshots[id] = new Snapshot
            {
                name = go.name,
                activeSelf = go.activeSelf,
                position = t.position,
                euler = t.rotation.eulerAngles,
                scale = t.localScale,
                weak = new WeakReference<GameObject>(go)
            };
        }

        public static void RestoreAll()
        {
            foreach (var kv in _snapshots)
            {
                var snap = kv.Value;
                if (snap.weak != null && snap.weak.TryGetTarget(out var go) && go != null)
                {
                    try
                    {
                        go.name = snap.name;
                        if (go.activeSelf != snap.activeSelf) go.SetActive(snap.activeSelf);
                        var t = go.transform;
                        t.position = snap.position;
                        t.rotation = Quaternion.Euler(snap.euler);
                        t.localScale = snap.scale;
                    }
                    catch { }
                }
            }
            _snapshots.Clear();
        }
    }
}

