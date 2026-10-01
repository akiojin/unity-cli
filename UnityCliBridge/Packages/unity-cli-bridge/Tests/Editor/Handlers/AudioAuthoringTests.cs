using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests
{
    public class AudioAuthoringTests
    {
        private const string Folder = "Assets/AudioAuthoringTests";
        private const string Mixer = Folder + "/Test.mixer";
        private const string Clip = Folder + "/Test.wav";

        [SetUp]
        public void Setup()
        {
            AssetDatabase.CreateFolder("Assets", "AudioAuthoringTests");
            using (var writer = new BinaryWriter(File.Create(Clip)))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + 8820);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(44100);
                writer.Write(88200); writer.Write((short)2); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(8820);
                writer.Write(new byte[8820]);
            }
            AssetDatabase.ImportAsset(Clip, ImportAssetOptions.ForceSynchronousImport);
        }

        [TearDown]
        public void Cleanup() => AssetDatabase.DeleteAsset(Folder);

        private static JObject Call(string action, JObject args = null)
        {
            args = args ?? new JObject();
            args["action"] = action;
            if (args["assetPath"] == null) args["assetPath"] = Mixer;
            return JObject.FromObject(AudioMixerHandler.HandleCommand(args));
        }

        [Test]
        public void MixerHierarchyAndExposedVolumePersist()
        {
            Assert.IsTrue(Call("create").Value<bool>("success"));
            Assert.IsTrue(Call("add_group", new JObject { ["parentGroup"] = "Master", ["name"] = "Music" }).Value<bool>("success"));
            Assert.IsTrue(Call("add_group", new JObject { ["parentGroup"] = "Master/Music", ["name"] = "Ambient" }).Value<bool>("success"));
            Assert.IsTrue(Call("expose_parameter", new JObject { ["groupPath"] = "Master/Music/Ambient", ["parameter"] = "Volume", ["parameterName"] = "AmbientVolume" }).Value<bool>("success"));
            AssetDatabase.ImportAsset(Mixer, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var result = Call("get");
            CollectionAssert.AreEquivalent(new[] { "Master", "Master/Music", "Master/Music/Ambient" }, result["groups"].Select(g => g.Value<string>("path")));
            Assert.AreEqual("Master/Music", result["groups"].Last.Value<string>("parentPath"));
            Assert.AreEqual("AmbientVolume", result["exposedParameters"][0].Value<string>("name"));
            Assert.AreEqual("Master/Music/Ambient", result["exposedParameters"][0].Value<string>("groupPath"));
            Assert.AreEqual("Volume", result["exposedParameters"][0].Value<string>("parameter"));
            Assert.IsTrue(AssetDatabase.LoadAssetAtPath<AudioMixer>(Mixer).GetFloat("AmbientVolume", out _));
        }

        [Test]
        public void MixerRejectsDuplicatesAndMissingParentWithoutMutation()
        {
            var created = Call("create");
            Assert.IsTrue(created.Value<bool>("success"), created.ToString());
            Assert.IsNotNull(Call("create")["error"]);
            Assert.IsNotNull(Call("add_group", new JObject { ["parentGroup"] = "Master/Missing", ["name"] = "Child" })["error"]);
            Call("add_group", new JObject { ["parentGroup"] = "Master", ["name"] = "Music" });
            Assert.IsNotNull(Call("add_group", new JObject { ["parentGroup"] = "Master", ["name"] = "Music" })["error"]);
            Call("expose_parameter", new JObject { ["groupPath"] = "Master", ["parameter"] = "Volume", ["parameterName"] = "Volume" });
            Assert.IsNotNull(Call("expose_parameter", new JObject { ["groupPath"] = "Master/Music", ["parameter"] = "Volume", ["parameterName"] = "Volume" })["error"]);
            Assert.IsNotNull(Call("expose_parameter", new JObject { ["groupPath"] = "Master", ["parameter"] = "Volume", ["parameterName"] = "Other" })["error"]);
            Assert.AreEqual(2, Call("get")["groups"].Count());
            Assert.AreEqual(1, Call("get")["exposedParameters"].Count());
        }

        [TestCase("../Test.mixer")]
        [TestCase("Assets/../Test.mixer")]
        [TestCase("Packages/Test.mixer")]
        [TestCase("Assets/Test.asset")]
        public void MixerRejectsUnsafeAssetPaths(string path) => Assert.IsNotNull(Call("create", new JObject { ["assetPath"] = path })["error"]);

        [Test]
        public void MixerCanBeRecreatedAfterAssetDeletion()
        {
            var first = Call("create");
            Assert.IsTrue(first.Value<bool>("success"), first.ToString());
            Assert.IsTrue(AssetDatabase.DeleteAsset(Mixer));
            var second = Call("create");
            Assert.IsTrue(second.Value<bool>("success"), second.ToString());
        }

        [Test]
        public void AudioImporterChangesPersist()
        {
            var result = JObject.FromObject(AssetImportSettingsHandler.HandleCommand("modify", new JObject
            {
                ["assetPath"] = Clip,
                ["settings"] = new JObject { ["loadType"] = "Streaming", ["compressionFormat"] = "Vorbis", ["quality"] = 0.42f, ["forceToMono"] = true, ["loadInBackground"] = true }
            }));
            Assert.IsNull(result["error"], result.ToString());
            var importer = (AudioImporter)AssetImporter.GetAtPath(Clip);
            Assert.AreEqual(AudioClipLoadType.Streaming, importer.defaultSampleSettings.loadType);
            Assert.AreEqual(AudioCompressionFormat.Vorbis, importer.defaultSampleSettings.compressionFormat);
            Assert.AreEqual(0.42f, importer.defaultSampleSettings.quality, 0.001);
            Assert.IsTrue(importer.forceToMono);
            Assert.IsTrue(importer.loadInBackground);
            Assert.AreEqual(5, result["newSettings"].Count());
        }

        [TestCase("quality", "1.5")]
        [TestCase("loadType", "\"Wrong\"")]
        [TestCase("compressionFormat", "999")]
        [TestCase("forceToMono", "\"true\"")]
        [TestCase("sampleRateSetting", "\"Wrong\"")]
        [TestCase("sampleRateOverride", "0")]
        [TestCase("typo", "true")]
        public void AudioImporterRejectsInvalidBatchBeforeChangingFlags(string key, string value)
        {
            var importer = (AudioImporter)AssetImporter.GetAtPath(Clip);
            var before = importer.loadInBackground;
            var result = JObject.FromObject(AssetImportSettingsHandler.HandleCommand("modify", new JObject
            {
                ["assetPath"] = Clip,
                ["settings"] = new JObject { ["loadInBackground"] = !before, [key] = JToken.Parse(value) }
            }));
            Assert.IsNotNull(result["error"]);
            Assert.AreEqual(before, importer.loadInBackground);
        }

        [Test]
        public void AudioImporterSampleRateSettingsRoundTrip()
        {
            var result = JObject.FromObject(AssetImportSettingsHandler.HandleCommand("modify", new JObject
            {
                ["assetPath"] = Clip,
                ["settings"] = new JObject { ["sampleRateSetting"] = "OverrideSampleRate", ["sampleRateOverride"] = 22050, ["ambisonic"] = false }
            }));
            Assert.IsNull(result["error"], result.ToString());
            var read = JObject.FromObject(AssetImportSettingsHandler.HandleCommand("get", new JObject { ["assetPath"] = Clip }));
            Assert.AreEqual("OverrideSampleRate", read["settings"].Value<string>("sampleRateSetting"));
            Assert.AreEqual(22050, read["settings"].Value<int>("sampleRateOverride"));
            Assert.IsFalse(read["settings"].Value<bool>("ambisonic"));
        }
    }
}
