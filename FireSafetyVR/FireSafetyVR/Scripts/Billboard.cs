using UnityEngine;

namespace FireSafetyVR
{

    /// <summary>
    /// 让物体（通常是 World Space Canvas）持续朝向玩家头部。
    /// 挂在任何需要"始终面对使用者"的 UI / 提示牌上。
    /// </summary>
    public class Billboard : MonoBehaviour
    {
        [Tooltip("留空则自动使用 Camera.main（VR 下即 XR Origin 的主相机）")]
        public Transform target;
        public bool onlyYAxis = false;
        public float smooth = 4f;

        Transform cached;

        void LateUpdate()
        {
            if (target == null)
            {
                if (cached == null)
                {
                    var cam = Camera.main;
                    if (cam == null) return;
                    cached = cam.transform;
                }
                target = cached;
            }

            Vector3 dir = target.position - transform.position;
            if (onlyYAxis) dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;

            Quaternion want = Quaternion.LookRotation(-dir.normalized, Vector3.up);
            transform.rotation = smooth > 0f
                ? Quaternion.Slerp(transform.rotation, want, Time.deltaTime * smooth)
                : want;
        }
    }
}
