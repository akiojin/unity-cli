using UnityEngine;
using UnityEditor;

public class DiceTextureGenerator : MonoBehaviour
{
    [MenuItem("Tools/Generate Dice Texture")]
    public static void GenerateDiceTexture()
    {
        int textureSize = 512;
        int dotRadius = 30;
        
        Texture2D diceTexture = new Texture2D(textureSize * 4, textureSize * 3);
        
        // 背景を白で塗りつぶす
        Color[] pixels = new Color[diceTexture.width * diceTexture.height];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = Color.white;
        }
        diceTexture.SetPixels(pixels);
        
        // 各面の配置 (展開図形式)
        // +Y面（1）- 上段中央
        DrawDiceFace(diceTexture, 1, textureSize * 1, textureSize * 2, textureSize, dotRadius);
        
        // -Z面（2）- 中段左
        DrawDiceFace(diceTexture, 2, textureSize * 0, textureSize * 1, textureSize, dotRadius);
        
        // +X面（3）- 中段左から2番目
        DrawDiceFace(diceTexture, 3, textureSize * 1, textureSize * 1, textureSize, dotRadius);
        
        // -X面（4）- 中段右から2番目
        DrawDiceFace(diceTexture, 4, textureSize * 2, textureSize * 1, textureSize, dotRadius);
        
        // +Z面（5）- 中段右
        DrawDiceFace(diceTexture, 5, textureSize * 3, textureSize * 1, textureSize, dotRadius);
        
        // -Y面（6）- 下段中央
        DrawDiceFace(diceTexture, 6, textureSize * 1, textureSize * 0, textureSize, dotRadius);
        
        // 枠線を描画
        DrawBorders(diceTexture, textureSize);
        
        diceTexture.Apply();
        
        // テクスチャを保存
        byte[] pngData = diceTexture.EncodeToPNG();
        string path = "Assets/Materials/Dice/DiceTexture.png";
        System.IO.File.WriteAllBytes(path, pngData);
        
        UnityCliBridge.Helpers.DebouncedAssetRefresh.Request();
        
        // テクスチャ設定を更新
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }
        
        UnityEngine.Debug.LogFormat("Dice texture generated at: " + path);
    }
    
    static void DrawDiceFace(Texture2D texture, int faceNumber, int offsetX, int offsetY, int faceSize, int dotRadius)
    {
        Color dotColor = Color.black;
        int padding = faceSize / 6;
        
        switch(faceNumber)
        {
            case 1:
                // 中央に1つ
                DrawDot(texture, offsetX + faceSize/2, offsetY + faceSize/2, dotRadius, dotColor);
                break;
                
            case 2:
                // 対角線上に2つ
                DrawDot(texture, offsetX + padding, offsetY + faceSize - padding, dotRadius, dotColor);
                DrawDot(texture, offsetX + faceSize - padding, offsetY + padding, dotRadius, dotColor);
                break;
                
            case 3:
                // 対角線上に3つ
                DrawDot(texture, offsetX + padding, offsetY + faceSize - padding, dotRadius, dotColor);
                DrawDot(texture, offsetX + faceSize/2, offsetY + faceSize/2, dotRadius, dotColor);
                DrawDot(texture, offsetX + faceSize - padding, offsetY + padding, dotRadius, dotColor);
                break;
                
            case 4:
                // 四隅に4つ
                DrawDot(texture, offsetX + padding, offsetY + padding, dotRadius, dotColor);
                DrawDot(texture, offsetX + padding, offsetY + faceSize - padding, dotRadius, dotColor);
                DrawDot(texture, offsetX + faceSize - padding, offsetY + padding, dotRadius, dotColor);
                DrawDot(texture, offsetX + faceSize - padding, offsetY + faceSize - padding, dotRadius, dotColor);
                break;
                
            case 5:
                // 四隅+中央で5つ
                DrawDot(texture, offsetX + padding, offsetY + padding, dotRadius, dotColor);
                DrawDot(texture, offsetX + padding, offsetY + faceSize - padding, dotRadius, dotColor);
                DrawDot(texture, offsetX + faceSize/2, offsetY + faceSize/2, dotRadius, dotColor);
                DrawDot(texture, offsetX + faceSize - padding, offsetY + padding, dotRadius, dotColor);
                DrawDot(texture, offsetX + faceSize - padding, offsetY + faceSize - padding, dotRadius, dotColor);
                break;
                
            case 6:
                // 左右に3つずつ
                int middleY = faceSize / 2;
                // 左側
                DrawDot(texture, offsetX + padding, offsetY + padding, dotRadius, dotColor);
                DrawDot(texture, offsetX + padding, offsetY + middleY, dotRadius, dotColor);
                DrawDot(texture, offsetX + padding, offsetY + faceSize - padding, dotRadius, dotColor);
                // 右側
                DrawDot(texture, offsetX + faceSize - padding, offsetY + padding, dotRadius, dotColor);
                DrawDot(texture, offsetX + faceSize - padding, offsetY + middleY, dotRadius, dotColor);
                DrawDot(texture, offsetX + faceSize - padding, offsetY + faceSize - padding, dotRadius, dotColor);
                break;
        }
    }
    
    static void DrawDot(Texture2D texture, int centerX, int centerY, int radius, Color color)
    {
        for (int x = -radius; x <= radius; x++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                if (x * x + y * y <= radius * radius)
                {
                    int pixelX = centerX + x;
                    int pixelY = centerY + y;
                    
                    if (pixelX >= 0 && pixelX < texture.width && pixelY >= 0 && pixelY < texture.height)
                    {
                        texture.SetPixel(pixelX, pixelY, color);
                    }
                }
            }
        }
    }
    
    static void DrawBorders(Texture2D texture, int faceSize)
    {
        Color borderColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        int borderWidth = 2;
        
        // 横線
        for (int i = 0; i <= 3; i++)
        {
            for (int x = 0; x < texture.width; x++)
            {
                for (int w = 0; w < borderWidth; w++)
                {
                    int y = i * faceSize + w;
                    if (y < texture.height)
                        texture.SetPixel(x, y, borderColor);
                }
            }
        }
        
        // 縦線
        for (int i = 0; i <= 4; i++)
        {
            for (int y = 0; y < texture.height; y++)
            {
                for (int w = 0; w < borderWidth; w++)
                {
                    int x = i * faceSize + w;
                    if (x < texture.width)
                        texture.SetPixel(x, y, borderColor);
                }
            }
        }
    }
}
