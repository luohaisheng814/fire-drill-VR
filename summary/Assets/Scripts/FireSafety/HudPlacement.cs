using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 世界空间 HUD 的取景约束：面板按锚点（手柄）摆放后，再做一次"必须在视野内"的修正。
    /// 只解两件事：离眼睛太近会糊成大块并顶到屏幕外；偏向一侧会被视野边缘切掉。
    /// </summary>
    public static class HudPlacement
    {
        /// <summary>
        /// 把 desired 位置换算到相机局部坐标，保证纵深不小于 minDistance、
        /// 横向偏角不超过 maxHalfAngleDeg（纵向按 0.9 倍收一点），再换算回世界坐标。
        /// 纯函数，便于自检直接验证。
        /// </summary>
        public static Vector3 KeepInView(Vector3 desired, Vector3 camPos, Quaternion camRot,
                                         float minDistance, float maxHalfAngleDeg,
                                         float minYAngleDeg = -90f, float maxYAngleDeg = 90f)
        {
            Vector3 local = Quaternion.Inverse(camRot) * (desired - camPos);
            local.z = Mathf.Max(local.z, minDistance);
            float limit = Mathf.Tan(maxHalfAngleDeg * Mathf.Deg2Rad) * local.z;
            local.x = Mathf.Clamp(local.x, -limit, limit);
            float lo = Mathf.Tan(minYAngleDeg * Mathf.Deg2Rad) * local.z;
            float hi = Mathf.Tan(maxYAngleDeg * Mathf.Deg2Rad) * local.z;
            local.y = Mathf.Clamp(local.y, Mathf.Min(lo, hi), Mathf.Max(lo, hi));
            return camPos + camRot * local;
        }
    }
}
