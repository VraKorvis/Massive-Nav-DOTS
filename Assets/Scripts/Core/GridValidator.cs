using Map;
using PFStar;
using UnityEngine;

namespace Core
{
    public static class GridValidator 
    {
        public static bool Validate(GridDataAsset asset)
        {
            int expectedLength = asset.Dimensions.x * asset.Dimensions.y;
            bool isValid = true;

            isValid &= CheckArray(asset.WallPush, "WallPush", expectedLength);
            isValid &= CheckArray(asset.Heights, "Heights", expectedLength);
            isValid &= CheckArray(asset.Normals, "Normals", expectedLength);

            return isValid;
        }

        private static bool CheckArray<T>(T[] array, string name, int expected)
        {
            if (array == null || array.Length != expected)
            {
                Debug.LogError($"[Baking] {name} array is invalid! Expected: {expected}, Actual: {array?.Length ?? 0}");
                return false;
            }
            return true;
        }
    }
}
