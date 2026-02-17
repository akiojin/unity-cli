using UnityEngine;
using UnityEditor;

public static class GUIStyles
{
    private static GUIStyle _log;
    public static GUIStyle Log => _log ??= new GUIStyle(EditorStyles.label)
    {
        wordWrap = true
    };
}
