using System.Linq;
using System.IO;
using UnityEditor;
using UnityEngine;
#if UNITY_ADDRESSABLES
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
#endif

public static class SampleWorkflows
{
    private const string TempRootName = "UnityCliBridge_Sample_Temp";
    private const string TempAddressablePath = "Assets/UnityCliBridge_Addressable_Sample.prefab";

    public static void RunSceneSample()
    {
        var existing = GameObject.Find(TempRootName);
        if (existing != null) Object.DestroyImmediate(existing);

        var root = new GameObject(TempRootName);
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.SetParent(root.transform);
        cube.transform.position = new Vector3(0, 0.5f, 0);
        Debug.Log("[UnityCliBridge Sample] Created demo cube under UnityCliBridge_Sample_Temp");
    }

    public static void RunAddressablesSample()
    {
#if UNITY_ADDRESSABLES
        var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
        if (settings == null)
        {
            Debug.LogWarning("[UnityCliBridge Sample] Addressables settings not found");
            return;
        }
        var group = settings.groups.FirstOrDefault(g => g != null && g.name == "UnityCliBridge_Sample_Temp")
                   ?? settings.CreateGroup("UnityCliBridge_Sample_Temp", false, false, false, null);

        if (File.Exists(TempAddressablePath)) File.Delete(TempAddressablePath);
        if (File.Exists(TempAddressablePath + ".meta")) File.Delete(TempAddressablePath + ".meta");

        var tempGO = new GameObject("UnityCliBridge_Addressable_Sample");
        PrefabUtility.SaveAsPrefabAsset(tempGO, TempAddressablePath);
        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(TempAddressablePath), group);
        entry.address = "unity-cli/sample";
        AssetDatabase.SaveAssets();
        Debug.Log("[UnityCliBridge Sample] Registered Addressable entry unity-cli/sample in UnityCliBridge_Sample_Temp group");
#else
        Debug.LogWarning("[UnityCliBridge Sample] Addressables package not enabled; sample skipped");
#endif
    }

    public static void Cleanup()
    {
        var existing = GameObject.Find(TempRootName);
        if (existing != null) Object.DestroyImmediate(existing);
#if UNITY_ADDRESSABLES
        var settings = AddressableAssetSettingsDefaultObject.GetSettings(false);
        if (settings != null)
        {
          var group = settings.groups.FirstOrDefault(g => g != null && g.name == "UnityCliBridge_Sample_Temp");
          if (group != null)
          {
              var entry = group.entries.FirstOrDefault(e => e.address == "unity-cli/sample");
              if (entry != null) settings.RemoveAssetEntry(entry.guid);
              settings.RemoveGroup(group);
          }
        }
        if (File.Exists(TempAddressablePath)) File.Delete(TempAddressablePath);
        if (File.Exists(TempAddressablePath + ".meta")) File.Delete(TempAddressablePath + ".meta");
        AssetDatabase.SaveAssets();
#endif
        AssetDatabase.Refresh();
    }
}
