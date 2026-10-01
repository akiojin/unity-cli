var path = "Assets/Tiles/grass.png";
var importer = AssetImporter.GetAtPath(path) as TextureImporter;
if (importer == null) throw new Exception("TextureImporter missing");
importer.textureType = TextureImporterType.Sprite;
importer.spriteImportMode = SpriteImportMode.Single;
importer.spritePixelsPerUnit = 16;
importer.SaveAndReimport();
var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
if (sprite == null) throw new Exception("Sprite missing after import");
return new { importer.spritePixelsPerUnit, mode = importer.spriteImportMode.ToString(),
    filter = importer.filterMode.ToString(), width = sprite.rect.width,
    height = sprite.rect.height };
