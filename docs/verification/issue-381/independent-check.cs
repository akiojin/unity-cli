if (UnityEditor.EditorApplication.isPlaying) throw new System.InvalidOperationException("Expected Edit Mode after client verification");
var go = UnityEngine.GameObject.Find("/LocalizationCanvas/Greeting");
if (go == null) throw new System.InvalidOperationException("Missing verified label");
var label = go.GetComponent<UnityEngine.UI.Text>();
var binding = go.GetComponent<UnityEngine.Localization.Components.LocalizeStringEvent>();
var collection = UnityEditor.Localization.LocalizationEditorSettings.GetStringTableCollection("DemoStrings");
var en = (UnityEngine.Localization.Tables.StringTable)collection.GetTable(new UnityEngine.Localization.LocaleIdentifier("en"));
var ja = (UnityEngine.Localization.Tables.StringTable)collection.GetTable(new UnityEngine.Localization.LocaleIdentifier("ja"));
var state = new {
 unity = UnityEngine.Application.unityVersion,
 project = UnityEngine.Application.dataPath,
 isPlaying = UnityEditor.EditorApplication.isPlaying,
 scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path,
 sceneDirty = UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty,
 settingsPath = UnityEditor.AssetDatabase.GetAssetPath(UnityEditor.Localization.LocalizationEditorSettings.ActiveLocalizationSettings),
 locales = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(UnityEditor.Localization.LocalizationEditorSettings.GetLocales(), l => new {code=l.Identifier.Code,path=UnityEditor.AssetDatabase.GetAssetPath(l)})),
 collectionPath = UnityEditor.AssetDatabase.GetAssetPath(collection),
 sharedDataPath = UnityEditor.AssetDatabase.GetAssetPath(collection.SharedData),
 tables = new [] {new {locale="en",path=UnityEditor.AssetDatabase.GetAssetPath(en),keyId=en.GetEntry("greeting").KeyId,text=en.GetEntry("greeting").LocalizedValue},new {locale="ja",path=UnityEditor.AssetDatabase.GetAssetPath(ja),keyId=ja.GetEntry("greeting").KeyId,text=ja.GetEntry("greeting").LocalizedValue}},
 tableReference = binding.StringReference.TableReference.ToString(),
 keyId = binding.StringReference.TableEntryReference.KeyId,
 listenerCount = binding.OnUpdateString.GetPersistentEventCount(),
 listenerMethod = binding.OnUpdateString.GetPersistentMethodName(0),
 listenerTargetsLabel = binding.OnUpdateString.GetPersistentTarget(0) == label,
 listenerState = binding.OnUpdateString.GetPersistentListenerState(0).ToString(),
 savedLabelText = label.text,
 fontPath = UnityEditor.AssetDatabase.GetAssetPath(label.font)
};
return state;
