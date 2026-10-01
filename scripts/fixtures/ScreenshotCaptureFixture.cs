// Copied into Assets by e2e-screenshot.py in an isolated verification project.
using UnityEngine;
using UnityEngine.UIElements;

[ExecuteAlways]
public class ScreenshotCaptureFixture : MonoBehaviour
{
    public bool alternate;
    public UIDocument document;
    public UnityEngine.UI.Image overlay;

    private void Update()
    {
        if (document == null || overlay == null) return;
        var root = document.rootVisualElement;
        if (root == null) return;
        var label = root.Q<Label>("capture-label");
        if (label == null)
        {
            label = new Label { name = "capture-label" };
            label.style.position = Position.Absolute;
            label.style.left = 30;
            label.style.top = 30;
            label.style.width = 350;
            label.style.height = 140;
            label.style.fontSize = 36;
            label.style.color = Color.white;
            root.Add(label);
        }
        label.text = alternate ? "Hello, Ada!" : "Welcome";
        label.style.backgroundColor = alternate ? Color.red : Color.green;
        overlay.color = alternate ? Color.yellow : Color.blue;
    }
}
