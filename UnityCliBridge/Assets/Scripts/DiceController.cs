using UnityEngine;
using UnityEngine.InputSystem;
public class DiceController : MonoBehaviour
{
    private Rigidbody rb;
    private bool isRolling = false;
    
    [Header("Roll Settings")]
    [SerializeField] private float minForce = 5f;
    [SerializeField] private float maxForce = 10f;
    [SerializeField] private float torqueStrength = 500f;
    [SerializeField] private float stopThreshold = 0.1f;
    
    [Header("Status")]
    [SerializeField] private bool canRoll = true;
    private int currentFaceUp = 0;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            UnityEngine.Debug.LogError("Rigidbody component is missing on " + gameObject.name);
        }
    }
    
    void Update()
    {
        // スペースキーでサイコロを転がす（新Input System対応）
        if (Keyboard.current != null && 
            Keyboard.current.spaceKey.wasPressedThisFrame && 
            canRoll && !isRolling)
        {
            Roll();
        }
        
        // 転がり状態の監視
        if (isRolling)
        {
            CheckIfStopped();
        }
    }
    
    public void Roll()
    {
        if (!canRoll || isRolling || rb == null) return;
        
        isRolling = true;
        
        // 位置を少し上げる（床から離す）
        transform.position = new Vector3(transform.position.x, transform.position.y + 0.5f, transform.position.z);
        
        // ランダムな上向きの力を加える
        float force = Random.Range(minForce, maxForce);
        rb.AddForce(Vector3.up * force, ForceMode.Impulse);
        
        // ランダムな回転力（トルク）を加える
        Vector3 randomTorque = new Vector3(
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f)
        ).normalized * torqueStrength;
        
        rb.AddTorque(randomTorque);
        
        UnityEngine.Debug.LogFormat("サイコロを転がしました！");
    }
    
    private void CheckIfStopped()
    {
        // 速度と角速度が閾値以下になったら停止と判定
        if (rb.linearVelocity.magnitude < stopThreshold && rb.angularVelocity.magnitude < stopThreshold)
        {
            isRolling = false;
            DetermineUpwardFace();
        }
    }
    
    private void DetermineUpwardFace()
    {
        // 各面の方向ベクトル（Cubeの場合）
        Vector3[] faceDirections = new Vector3[]
        {
            Vector3.up,      // 1 (上面)
            Vector3.down,    // 6 (下面)
            Vector3.forward, // 2 (前面)
            Vector3.back,    // 5 (背面)
            Vector3.right,   // 3 (右面)
            Vector3.left     // 4 (左面)
        };
        
        int[] faceValues = new int[] { 1, 6, 2, 5, 3, 4 };
        
        float maxDot = -1f;
        int upFace = 1;
        
        // どの面が最も上を向いているか判定
        for (int i = 0; i < faceDirections.Length; i++)
        {
            Vector3 worldDirection = transform.TransformDirection(faceDirections[i]);
            float dot = Vector3.Dot(worldDirection, Vector3.up);
            
            if (dot > maxDot)
            {
                maxDot = dot;
                upFace = faceValues[i];
            }
        }
        
        currentFaceUp = upFace;
        UnityEngine.Debug.LogFormat($"サイコロの出目: {currentFaceUp}");
    }
    
    // 外部から呼び出し可能なメソッド
    public int GetCurrentFaceUp()
    {
        return currentFaceUp;
    }
    
    public bool IsRolling()
    {
        return isRolling;
    }
    
    public void EnableRolling(bool enable)
    {
        canRoll = enable;
    }
}
