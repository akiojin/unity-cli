var pk = new System.Collections.Generic.List<string>();
foreach (var p in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
    if (p.name.Contains("2d") || p.name.Contains("tilemap") || p.name.Contains("unity-cli")) pk.Add(p.name + "@" + p.version);
return new { unity = Application.unityVersion, pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null ? "Built-in" : UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.GetType().FullName,
    graphicsDevice = SystemInfo.graphicsDeviceType.ToString(), packages = pk.ToArray(), spritePackerMode = EditorSettings.spritePackerMode.ToString() };
