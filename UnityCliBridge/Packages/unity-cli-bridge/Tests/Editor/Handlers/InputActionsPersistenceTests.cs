#if ENABLE_INPUT_SYSTEM
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests
{
    public class InputActionsPersistenceTests
    {
        private string assetPath;

        [SetUp]
        public void SetUp()
        {
            assetPath = "Assets/InputActionsPersistence-" + Guid.NewGuid().ToString("N") + ".inputactions";
            File.WriteAllText(assetPath, @"{
                ""name"": ""Persistence"",
                ""maps"": [{""name"":""Audit"", ""id"":""77923821-9389-4f88-b8a5-e87b47951798"",
                    ""actions"":[{""name"":""Jump"",""type"":""Button"",""id"":""64f314c2-782b-4835-8932-5be253084a83""}],
                    ""bindings"":[{""id"":""d87a4ec9-f0d1-4645-b799-cd4e4c72b14d"",""path"":""<Keyboard>/space"",""action"":""Jump""}]}],
                ""controlSchemes"":[{""name"":""Keyboard"",""bindingGroup"":""Keyboard"",""devices"":[{""devicePath"":""<Keyboard>"",""isOptional"":true,""isOR"":true}]}]
            }");
            Reimport();
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(assetPath);
        }

        [TestCase("create_map")]
        [TestCase("remove_map")]
        [TestCase("add_action")]
        [TestCase("remove_action")]
        [TestCase("add_binding")]
        [TestCase("remove_binding")]
        [TestCase("remove_all_bindings")]
        [TestCase("composite")]
        [TestCase("add_scheme")]
        [TestCase("remove_scheme")]
        public void SuccessfulEdit_ChangesSourceAndSurvivesReimport(string operation)
        {
            var before = JObject.Parse(File.ReadAllText(assetPath));
            var parameters = new JObject
            {
                ["assetPath"] = assetPath,
                ["mapName"] = "Audit",
                ["actionName"] = "Jump"
            };
            object response;
            switch (operation)
            {
                case "create_map":
                    parameters["mapName"] = "NewMap";
                    response = InputActionsHandler.CreateActionMap(parameters);
                    break;
                case "remove_map":
                    response = InputActionsHandler.RemoveActionMap(parameters);
                    break;
                case "add_action":
                    parameters["actionName"] = "NewAction";
                    response = InputActionsHandler.AddInputAction(parameters);
                    break;
                case "remove_action":
                    response = InputActionsHandler.RemoveInputAction(parameters);
                    break;
                case "add_binding":
                    parameters["path"] = "<Keyboard>/enter";
                    response = InputActionsHandler.AddInputBinding(parameters);
                    break;
                case "remove_binding":
                    parameters["bindingIndex"] = 0;
                    response = InputActionsHandler.RemoveInputBinding(parameters);
                    break;
                case "remove_all_bindings":
                    response = InputActionsHandler.RemoveAllBindings(parameters);
                    break;
                case "composite":
                    parameters["compositeType"] = "1DAxis";
                    parameters["bindings"] = new JObject { ["negative"] = "<Keyboard>/a", ["positive"] = "<Keyboard>/d" };
                    response = InputActionsHandler.CreateCompositeBinding(parameters);
                    break;
                case "add_scheme":
                    parameters["operation"] = "add";
                    parameters["schemeName"] = "Gamepad";
                    parameters["devices"] = new JArray("Gamepad");
                    response = InputActionsHandler.ManageControlSchemes(parameters);
                    break;
                default:
                    parameters["operation"] = "remove";
                    parameters["schemeName"] = "Keyboard";
                    response = InputActionsHandler.ManageControlSchemes(parameters);
                    break;
            }

            var result = JObject.FromObject(response);
            Assert.IsNull(result["error"], result.ToString());
            Assert.IsTrue(result.Value<bool>("success"));
            var saved = JObject.Parse(File.ReadAllText(assetPath));
            Assert.IsFalse(JToken.DeepEquals(before, saved), "Successful edit must update source JSON immediately");
            if (operation == "remove_map")
                Assert.IsTrue(JToken.DeepEquals(before["controlSchemes"], saved["controlSchemes"]),
                    "Removing the last map must preserve control scheme device flags");
            var editedState = ReadState();
            Reimport();
            Assert.IsTrue(JToken.DeepEquals(editedState, ReadState()), "Reimport must retain edited state and IDs");
            Assert.IsTrue(JToken.DeepEquals(saved, JObject.Parse(File.ReadAllText(assetPath))));
        }

        [Test]
        public void NativeAsset_EditStillUsesUnitySerialization()
        {
            var copy = UnityEngine.Object.Instantiate(AssetDatabase.LoadMainAssetAtPath(assetPath));
            AssetDatabase.DeleteAsset(assetPath);
            assetPath = Path.ChangeExtension(assetPath, ".asset");
            AssetDatabase.CreateAsset(copy, assetPath);
            AssetDatabase.SaveAssets();
            var before = File.ReadAllBytes(assetPath);
            var response = JObject.FromObject(InputActionsHandler.CreateActionMap(new JObject
            {
                ["assetPath"] = assetPath, ["mapName"] = "NewMap"
            }));
            Assert.IsNull(response["error"], response.ToString());
            Assert.IsTrue(response.Value<bool>("success"));
            CollectionAssert.AreNotEqual(before, File.ReadAllBytes(assetPath));
            Reimport();
            Assert.IsTrue(ReadState()["actionMaps"].Any(map => map.Value<string>("name") == "NewMap"));
        }

        [Test]
        public void ReadOnlySource_ReturnsErrorInsteadOfSuccess()
        {
            var before = File.ReadAllText(assetPath);
            var attributes = File.GetAttributes(assetPath);
            try
            {
                File.SetAttributes(assetPath, attributes | FileAttributes.ReadOnly);
                var response = JObject.FromObject(InputActionsHandler.CreateActionMap(new JObject
                {
                    ["assetPath"] = assetPath, ["mapName"] = "Unsaved"
                }));
                Assert.IsNotNull(response["error"], response.ToString());
                Assert.IsFalse(response.Value<bool>("success"));
                Assert.AreEqual(before, File.ReadAllText(assetPath));
            }
            finally
            {
                File.SetAttributes(assetPath, attributes);
            }
        }

        [Test]
        public void InvalidEdit_DoesNotChangeSource()
        {
            var before = File.ReadAllText(assetPath);
            var result = JObject.FromObject(InputActionsHandler.AddInputBinding(new JObject
            {
                ["assetPath"] = assetPath, ["mapName"] = "Missing", ["actionName"] = "Jump", ["path"] = "<Keyboard>/enter"
            }));
            Assert.IsNotNull(result["error"]);
            Assert.AreEqual(before, File.ReadAllText(assetPath));
        }

        private JObject ReadState()
        {
            var state = JObject.FromObject(InputActionsHandler.GetInputActionsState(new JObject { ["assetPath"] = assetPath }));
            Assert.IsNull(state["error"], state.ToString());
            // Input System JSON roundtrips unspecified binding settings as empty strings.
            foreach (var property in state.Descendants().OfType<JProperty>())
            {
                if (property.Value.Type == JTokenType.Null &&
                    (property.Name == "groups" || property.Name == "interactions" || property.Name == "processors"))
                    property.Value = "";
            }
            return state;
        }

        private void Reimport()
        {
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
#endif
