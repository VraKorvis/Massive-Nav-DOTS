using Map.Grid;
using UnityEngine;

namespace Core
{
    public static class GridValidator 
    {
        public static bool Validate(GridDataAsset asset)
        {
            if (asset.Dimensions.x <= 0 || asset.Dimensions.y <= 0) return true;

            int expectedLength = asset.Dimensions.x * asset.Dimensions.y;
            
            bool isValid = true;
            isValid &= CheckArrayOrEmpty(asset.WallPush, "WallPush", expectedLength);
            isValid &= CheckArrayOrEmpty(asset.Heights, "Heights", expectedLength);
            isValid &= CheckArrayOrEmpty(asset.Normals, "Normals", expectedLength);

            return isValid;
        }

        private static bool CheckArrayOrEmpty<T>(T[] array, string name, int expected)
        {
            if (array == null || array.Length == 0) return true;

            if (array.Length != expected)
            {
                Debug.LogError($"[Baking] {name} array size mismatch! Expected: {expected}, Actual: {array.Length}. Run Baking again.");
                return false;
            }
            return true;
        }
    }
}
