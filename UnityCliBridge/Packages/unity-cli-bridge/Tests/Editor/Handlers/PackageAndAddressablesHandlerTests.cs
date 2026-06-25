using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityCliBridge.Handlers;

namespace UnityCliBridge.Tests
{
    [TestFixture]
    public class PackageAndAddressablesHandlerTests
    {
        private const string TestFolder = "Assets/Spec151AddressablesTests";
        private const string TestAssetPath = TestFolder + "/AddressableAsset.txt";
        private const string GroupName = "Spec151TestGroup";
        private const string MoveGroupName = "Spec151MoveGroup";

        [TearDown]
        public void TearDown()
        {
            AddressablesHandler.HandleCommand("remove_entry", new JObject
            {
                ["assetPath"] = TestAssetPath
            });
            AddressablesHandler.HandleCommand("remove_group", new JObject
            {
                ["groupName"] = GroupName
            });
            AddressablesHandler.HandleCommand("remove_group", new JObject
            {
                ["groupName"] = MoveGroupName
            });

            if (AssetDatabase.IsValidFolder(TestFolder))
            {
                AssetDatabase.DeleteAsset(TestFolder);
            }
        }

        private static JObject ToJObject(object result)
        {
            return result as JObject ?? JObject.FromObject(result);
        }

        private static void AssertSuccess(JObject result)
        {
            Assert.IsNull(result["error"]?.Value<string>());
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
        }

        private static void CreateTextAsset()
        {
            if (!AssetDatabase.IsValidFolder(TestFolder))
            {
                AssetDatabase.CreateFolder("Assets", "Spec151AddressablesTests");
            }

            File.WriteAllText(TestAssetPath, "spec151");
            AssetDatabase.ImportAsset(TestAssetPath);
        }

        [Test]
        public void PackageManagerAndRegistry_ShouldReturnRecommendationsAndListPackages()
        {
            var packageRecommendations = ToJObject(PackageManagerHandler.HandleCommand("recommend", new JObject
            {
                ["category"] = "essential"
            }));

            AssertSuccess(packageRecommendations);
            Assert.AreEqual("recommend", packageRecommendations.Value<string>("action"));
            Assert.IsTrue(packageRecommendations["packages"]!.Any(package =>
                package["packageId"]?.Value<string>() == "com.unity.addressables"));

            var packageList = ToJObject(PackageManagerHandler.HandleCommand("list", new JObject
            {
                ["includeBuiltIn"] = false
            }));

            AssertSuccess(packageList);
            Assert.AreEqual("list", packageList.Value<string>("action"));
            Assert.Greater(packageList.Value<int>("totalCount"), 0);
            Assert.IsTrue(packageList["packages"]!.Any(package =>
                package["name"]?.Value<string>() == "com.unity.addressables"));

            var registryRecommendations = ToJObject(RegistryConfigHandler.HandleCommand("recommend", new JObject
            {
                ["registry"] = "openupm"
            }));

            AssertSuccess(registryRecommendations);
            Assert.AreEqual("openupm", registryRecommendations.Value<string>("registry"));
            Assert.IsTrue(registryRecommendations["packages"]!.Any(package =>
                package["packageId"]?.Value<string>() == "com.cysharp.unitask"));

            var invalidScope = ToJObject(RegistryConfigHandler.HandleCommand("add_scope", new JObject()));
            StringAssert.Contains("Registry name and scope are required", invalidScope.Value<string>("error"));
        }

        [Test]
        public void SetPackageSetting_ShouldRequireExplicitConfirmation()
        {
            var missingConfirmation = ToJObject(PackageSettingsHandler.SetPackageSetting(new JObject
            {
                ["package"] = "com.example.spec151",
                ["key"] = "feature.enabled",
                ["value"] = true
            }));

            Assert.AreEqual("CONFIRMATION_REQUIRED", missingConfirmation.Value<string>("code"));
            StringAssert.Contains("confirmChanges", missingConfirmation.Value<string>("error"));

            var falseConfirmation = ToJObject(PackageSettingsHandler.SetPackageSetting(new JObject
            {
                ["package"] = "com.example.spec151",
                ["key"] = "feature.enabled",
                ["value"] = true,
                ["confirmChanges"] = false
            }));

            Assert.AreEqual("CONFIRMATION_REQUIRED", falseConfirmation.Value<string>("code"));
            StringAssert.Contains("confirmChanges", falseConfirmation.Value<string>("error"));
        }

        [Test]
        public void AddressablesManageAndAnalyze_ShouldHandleEntryLifecycle()
        {
            CreateTextAsset();

            var createGroup = ToJObject(AddressablesHandler.HandleCommand("create_group", new JObject
            {
                ["groupName"] = GroupName
            }));
            AssertSuccess(createGroup);
            Assert.AreEqual(GroupName, createGroup["data"]?["groupName"]?.Value<string>());

            var createMoveGroup = ToJObject(AddressablesHandler.HandleCommand("create_group", new JObject
            {
                ["groupName"] = MoveGroupName
            }));
            AssertSuccess(createMoveGroup);

            var addEntry = ToJObject(AddressablesHandler.HandleCommand("add_entry", new JObject
            {
                ["assetPath"] = TestAssetPath,
                ["groupName"] = GroupName,
                ["address"] = "spec151/original",
                ["labels"] = new JArray("spec151")
            }));
            AssertSuccess(addEntry);
            Assert.AreEqual(TestAssetPath, addEntry["data"]?["assetPath"]?.Value<string>());
            Assert.AreEqual("spec151/original", addEntry["data"]?["address"]?.Value<string>());
            Assert.AreEqual(GroupName, addEntry["data"]?["groupName"]?.Value<string>());

            var setAddress = ToJObject(AddressablesHandler.HandleCommand("set_address", new JObject
            {
                ["assetPath"] = TestAssetPath,
                ["newAddress"] = "spec151/renamed"
            }));
            AssertSuccess(setAddress);
            Assert.AreEqual("spec151/renamed", setAddress["data"]?["address"]?.Value<string>());

            var addLabel = ToJObject(AddressablesHandler.HandleCommand("add_label", new JObject
            {
                ["assetPath"] = TestAssetPath,
                ["label"] = "runtime"
            }));
            AssertSuccess(addLabel);
            Assert.IsTrue(addLabel["data"]?["labels"]!.Any(label => label.Value<string>() == "runtime"));

            var listEntries = ToJObject(AddressablesHandler.HandleCommand("list_entries", new JObject
            {
                ["groupName"] = GroupName,
                ["pageSize"] = 10,
                ["offset"] = 0
            }));
            AssertSuccess(listEntries);
            Assert.IsTrue(listEntries["data"]?["entries"]!.Any(entry =>
                entry["assetPath"]?.Value<string>() == TestAssetPath &&
                entry["address"]?.Value<string>() == "spec151/renamed"));

            var dependencies = ToJObject(AddressablesHandler.HandleCommand("analyze_dependencies", new JObject
            {
                ["assetPath"] = TestAssetPath
            }));
            AssertSuccess(dependencies);
            Assert.IsNotNull(dependencies["data"]?["dependencies"]?[TestAssetPath]);

            var moveEntry = ToJObject(AddressablesHandler.HandleCommand("move_entry", new JObject
            {
                ["assetPath"] = TestAssetPath,
                ["targetGroupName"] = MoveGroupName
            }));
            AssertSuccess(moveEntry);
            Assert.AreEqual(MoveGroupName, moveEntry["data"]?["groupName"]?.Value<string>());

            var removeEntry = ToJObject(AddressablesHandler.HandleCommand("remove_entry", new JObject
            {
                ["assetPath"] = TestAssetPath
            }));
            AssertSuccess(removeEntry);

            var removeOriginalGroup = ToJObject(AddressablesHandler.HandleCommand("remove_group", new JObject
            {
                ["groupName"] = GroupName
            }));
            AssertSuccess(removeOriginalGroup);

            var removeMoveGroup = ToJObject(AddressablesHandler.HandleCommand("remove_group", new JObject
            {
                ["groupName"] = MoveGroupName
            }));
            AssertSuccess(removeMoveGroup);
        }
    }
}
