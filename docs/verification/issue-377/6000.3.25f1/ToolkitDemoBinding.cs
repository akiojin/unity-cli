using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class ToolkitDemoBinding : MonoBehaviour
{
    private Button submit;
    private TextField playerName;
    private Label status;

    private void OnEnable()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;
        submit = root.Q<Button>("submit");
        playerName = root.Q<TextField>("player-name");
        status = root.Q<Label>("status");
        if (submit == null || playerName == null || status == null)
        {
            Debug.LogError("ToolkitDemo: required UXML controls missing");
            return;
        }
        submit.clicked += Submit;
    }

    private void OnDisable()
    {
        if (submit != null) submit.clicked -= Submit;
    }

    private void Submit()
    {
        status.text = "Hello, " + playerName.value + "!";
    }
}
