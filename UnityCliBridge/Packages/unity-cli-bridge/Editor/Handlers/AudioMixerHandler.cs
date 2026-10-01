using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace UnityCliBridge.Handlers
{
    /// <summary>Audio authoring adapter; Unity's internal editor types never cross the wire.</summary>
    public static class AudioMixerHandler
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static object HandleCommand(JObject parameters)
        {
            try
            {
                var action = Required(parameters, "action");
                var path = Required(parameters, "assetPath");
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".mixer", StringComparison.OrdinalIgnoreCase)
                    || path.Contains('\\') || path.Split('/').Any(p => p == ".." || p == "." || p.Length == 0))
                    throw new ArgumentException("assetPath must be an Assets/ path ending in .mixer without traversal");
                if (EditorApplication.isPlayingOrWillChangePlaymode && action != "get")
                    return new { success = false, error = "AudioMixer authoring requires Edit Mode", code = "EDIT_MODE_REQUIRED" };

                AudioMixer mixer;
                if (action == "create")
                {
                    if (!AssetDatabase.IsValidFolder(Path.GetDirectoryName(path)))
                        throw new ArgumentException("The parent asset folder must exist");
                    if (File.Exists(path) || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets)))
                        throw new ArgumentException("An asset already exists at assetPath");
                    var controller = FindType("UnityEditor.Audio.AudioMixerController");
                    mixer = (AudioMixer)Method(controller, "CreateMixerControllerAtPath").Invoke(null, new object[] { path });
                    EditorUtility.SetDirty(mixer);
                    AssetDatabase.SaveAssets();
                }
                else
                {
                    mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(path);
                    if (mixer == null) throw new ArgumentException("AudioMixer not found: " + path);
                    var groups = Groups(mixer);
                    switch (action)
                    {
                        case "get": break;
                        case "add_group":
                            var parentPath = Required(parameters, "parentGroup");
                            var parent = FindGroup(groups, parentPath);
                            var name = Required(parameters, "name");
                            if (name.Contains('/') || name.Contains('\\') || name == "." || name == "..")
                                throw new ArgumentException("Group name must be a single path segment");
                            if (groups.Any(g => g.path == parentPath + "/" + name))
                                throw new ArgumentException("A group already exists at that path");
                            var create = Method(mixer.GetType(), "CreateNewGroup");
                            var attach = Method(mixer.GetType(), "AddChildToParent");
                            Undo.RecordObject(parent, "Add AudioMixer group");
                            var group = (Object)create.Invoke(mixer, new object[] { name, true });
                            attach.Invoke(mixer, new object[] { group, parent });
                            EditorUtility.SetDirty(parent);
                            EditorUtility.SetDirty(group);
                            break;
                        case "expose_parameter":
                            var target = FindGroup(groups, Required(parameters, "groupPath"));
                            if (Required(parameters, "parameter") != "Volume")
                                throw new ArgumentException("Only the Volume group parameter is supported");
                            var exposedName = Required(parameters, "parameterName");
                            var guid = Method(target.GetType(), "GetGUIDForVolume").Invoke(target, null);
                            var property = Property(mixer.GetType(), "exposedParameters");
                            var exposed = (Array)property.GetValue(mixer);
                            foreach (var entry in exposed)
                            {
                                if ((string)Field(entry, "name") == exposedName || Field(entry, "guid").Equals(guid))
                                    throw new ArgumentException("The parameter or exposed name already exists");
                            }
                            var parameterPath = Activator.CreateInstance(FindType("UnityEditor.Audio.AudioGroupParameterPath"), new[] { target, guid });
                            var expose = Method(mixer.GetType(), "AddExposedParameter");
                            Undo.RecordObject(mixer, "Expose AudioMixer parameter");
                            expose.Invoke(mixer, new[] { parameterPath });
                            exposed = (Array)property.GetValue(mixer);
                            for (var i = 0; i < exposed.Length; i++)
                            {
                                var entry = exposed.GetValue(i);
                                if (!Field(entry, "guid").Equals(guid)) continue;
                                entry.GetType().GetField("name", Flags).SetValue(entry, exposedName);
                                exposed.SetValue(entry, i);
                            }
                            property.SetValue(mixer, exposed);
                            break;
                        default: throw new ArgumentException("Unknown audio mixer action: " + action);
                    }
                    if (action != "get")
                    {
                        EditorUtility.SetDirty(mixer);
                        AssetDatabase.SaveAssets();
                    }
                }
                return Describe(mixer, path, action);
            }
            catch (Exception exception)
            {
                var error = exception is TargetInvocationException && exception.InnerException != null ? exception.InnerException : exception;
                return new { success = false, error = error.Message, code = error is ArgumentException ? "INVALID_ARGUMENT" : "AUDIO_MIXER_API_ERROR" };
            }
        }

        private static string Required(JObject parameters, string key)
        {
            if (parameters?[key]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)parameters[key]))
                throw new ArgumentException(key + " must be a non-empty string");
            return (string)parameters[key];
        }

        private static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name)).FirstOrDefault(t => t != null) ?? throw new NotSupportedException("Unity audio editor API unavailable: " + name);
        private static MethodInfo Method(Type type, string name) => type.GetMethod(name, Flags)
            ?? throw new NotSupportedException("Unity audio editor method unavailable: " + name);
        private static PropertyInfo Property(Type type, string name) => type.GetProperty(name, Flags)
            ?? throw new NotSupportedException("Unity audio editor property unavailable: " + name);
        private static object Field(object instance, string name) => instance.GetType().GetField(name, Flags).GetValue(instance);

        private sealed class GroupInfo
        {
            public Object group;
            public string path;
            public string parentPath;
        }

        private static List<GroupInfo> Groups(AudioMixer mixer)
        {
            var result = new List<GroupInfo>();
            Visit((Object)Property(mixer.GetType(), "masterGroup").GetValue(mixer), null, result);
            return result;
        }

        private static void Visit(Object group, string parent, List<GroupInfo> result)
        {
            var path = parent == null ? group.name : parent + "/" + group.name;
            result.Add(new GroupInfo { group = group, path = path, parentPath = parent });
            foreach (Object child in (IEnumerable)Property(group.GetType(), "children").GetValue(group)) Visit(child, path, result);
        }

        private static Object FindGroup(List<GroupInfo> groups, string path)
        {
            var matches = groups.Where(g => g.path == path).ToArray();
            if (matches.Length != 1) throw new ArgumentException("Group path must identify exactly one group: " + path);
            return matches[0].group;
        }

        private static object Describe(AudioMixer mixer, string path, string action)
        {
            var groups = Groups(mixer);
            var exposed = new List<object>();
            foreach (var entry in (IEnumerable)Property(mixer.GetType(), "exposedParameters").GetValue(mixer))
            {
                var guid = Field(entry, "guid");
                var group = groups.FirstOrDefault(g => Method(g.group.GetType(), "GetGUIDForVolume").Invoke(g.group, null).Equals(guid));
                exposed.Add(new { name = (string)Field(entry, "name"), guid = guid.ToString(), groupPath = group?.path, parameter = group == null ? null : "Volume" });
            }
            return new { success = true, action, assetPath = path,
                groups = groups.Select(g => new { name = g.group.name, g.path, g.parentPath }).ToArray(), exposedParameters = exposed };
        }
    }
}
