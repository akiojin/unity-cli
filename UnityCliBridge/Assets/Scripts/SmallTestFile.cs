using UnityEngine;
using UnityEngine.InputSystem;

// 小サイズテストファイル（約50行）
public class SmallTestFile : MonoBehaviour
{
    private int testValue;
    private string testString;
    
    public void TestMethod()
    {
        testValue = 42;
        testString = "Small test";
        UnityEngine.Debug.LogFormat("Small file test method");
    }
    
    private void Calculate()
    {
        for (int i = 0; i < 10; i++)
        {
            testValue += i;
        }
    }
    
    public int GetValue()
    {
        return testValue;
    }
    
    public void SetValue(int value)
    {
        testValue = value;
    }
    
    void Start()
    {
        TestMethod();
        Calculate();
    }
    
    void Update()
    {
        // Simple update logic (Input System)
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.sKey.wasPressedThisFrame)
        {
            UnityEngine.Debug.LogFormat("Small test file key pressed");
        }
    }
    
    private void OnDestroy()
    {
        UnityEngine.Debug.LogFormat("Small test file destroyed");
    }
}
