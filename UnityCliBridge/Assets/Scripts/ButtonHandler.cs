using UnityEngine;
using UnityEngine.UI;

public class ButtonHandler : MonoBehaviour
{
    private DiceController diceController;
    
    void Start()
    {
        // Diceオブジェクトを探してDiceControllerを取得
        GameObject dice = GameObject.Find("Dice");
        if (dice != null)
        {
            diceController = dice.GetComponent<DiceController>();
            if (diceController != null)
            {
                Debug.Log("DiceController found and connected to button");
            }
            else
            {
                Debug.LogWarning("DiceController component not found on Dice object");
            }
        }
        else
        {
            Debug.LogWarning("Dice GameObject not found in scene");
        }
        
        // ボタンのクリックイベントにRollDiceメソッドを登録
        Button button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.AddListener(RollDice);
            Debug.Log("Button click listener added");
        }
        else
        {
            Debug.LogError("Button component not found on " + gameObject.name);
        }
    }
    
    void RollDice()
    {
        if (diceController != null)
        {
            if (!diceController.IsRolling())
            {
                diceController.Roll();
                Debug.Log("Dice roll triggered by button click");
            }
            else
            {
                Debug.Log("Dice is already rolling");
            }
        }
        else
        {
            Debug.LogWarning("Cannot roll dice - DiceController not connected");
        }
    }
    
    void OnDestroy()
    {
        // クリーンアップ：ボタンリスナーを削除
        Button button = GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveListener(RollDice);
        }
    }
}