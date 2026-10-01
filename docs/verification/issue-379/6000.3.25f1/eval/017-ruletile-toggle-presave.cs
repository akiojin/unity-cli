var map = GameObject.Find("/Grid/Tilemap").GetComponent<UnityEngine.Tilemaps.Tilemap>();
var rule = AssetDatabase.LoadAssetAtPath<UnityEngine.RuleTile>("Assets/Tiles/GrassRule.asset");
var left = new Vector3Int(0, 2, 0); var right = new Vector3Int(1, 2, 0);
var withNeighbor = map.GetSprite(left).name;
map.SetTile(right, null);
var withoutNeighbor = map.GetSprite(left).name;
map.SetTile(right, rule);
var restored = map.GetSprite(left).name;
var rightSprite = map.GetSprite(right).name;
return new { leftWithRightNeighbor = withNeighbor, leftAfterRemovingNeighbor = withoutNeighbor, leftAfterRestoringNeighbor = restored, rightCellNoNeighbor = rightSprite,
    pass = withNeighbor == "edge" && withoutNeighbor == "grass" && restored == "edge" && rightSprite == "grass" };
