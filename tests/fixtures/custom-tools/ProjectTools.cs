using System;
using System.Threading;
using UnityEditor;
using UnityEngine;
using UnityCliBridge.Tools;

[InitializeOnLoad]
public static class ProjectTools
{
    private static readonly int EditorThread = Thread.CurrentThread.ManagedThreadId;
    private static int spawnCalls;

    [UnityCliTool("spawn_light", Description = "Create a scene light")]
    public static object SpawnLight([UnityCliArg("GameObject name")] string name, float intensity = 1f)
    {
        spawnCalls++;
        var gameObject = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(gameObject, "Custom spawn light");
        var light = gameObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        return new { name = gameObject.name, instanceId = gameObject.GetInstanceID(),
            mainThread = Thread.CurrentThread.ManagedThreadId == EditorThread, spawnCalls };
    }

    [UnityCliTool("custom_tool_stats", Mutating = false, Description = "Read fixture invocation count")]
    public static object Stats() => new { spawnCalls };

    [UnityCliTool("throw_custom", Description = "Throw a controlled test exception")]
    public static object Throw() => throw new InvalidOperationException("custom failure 442");
}
