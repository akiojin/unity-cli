using UnityEngine;
using UnityEngine.UI;

public class JumpButtonDiceBinder : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private DiceController dice;

    private void Awake()
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }

        if (button == null)
        {
            Debug.LogError("Button component is missing on JumpButtonDiceBinder GameObject.");
            enabled = false;
            return;
        }

        if (dice == null)
        {
            var go = GameObject.Find("Dice");
            if (go != null)
            {
                dice = go.GetComponent<DiceController>();
            }
        }

        if (dice == null)
        {
            Debug.LogError("DiceController not found. Assign it in the inspector or ensure a GameObject named 'Dice' with DiceController exists.");
        }

        button.onClick.AddListener(HandleClick);
    }

    private void OnDestroy()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(HandleClick);
        }
    }

    private void HandleClick()
    {
        if (dice != null)
        {
            dice.Roll();
        }
    }
}
