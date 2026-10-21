using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 保险销（拉环）：被 Toggle 抓取后，只要拉开超过阈值距离即判定拔出，
    /// 通知灭火器完成步骤 ⑥，销子自动脱手掉落。
    /// </summary>
    public class SafetyPin : MonoBehaviour
    {
        [Header("引用")]
        public Extinguisher owner;
        public ToggleGrab grab;

        [Header("判定")]
        [Tooltip("销子离开原位多少米算拔出")]
        public float pullDistance = 0.12f;
        [Tooltip("拔出后销子是否掉落")]
        public bool dropAfterPull = true;
        [Tooltip("拔出后延迟多少秒隐藏销子（0 = 不隐藏）")]
        public float hideDelay = 0f;

        public bool IsPulled { get; private set; }

        private Vector3 _homeLocalPos;
        private Rigidbody _rb;

        private void Awake()
        {
            if (!owner) owner = GetComponentInParent<Extinguisher>();
            if (!grab) grab = GetComponent<ToggleGrab>();
            _rb = GetComponent<Rigidbody>();
            _homeLocalPos = owner ? owner.transform.InverseTransformPoint(transform.position)
                                  : transform.localPosition;
        }

        private void Update()
        {
            if (IsPulled || !owner || !grab) return;
            if (!grab.IsHeld) return;

            Vector3 homeWorld = owner.transform.TransformPoint(_homeLocalPos);
            if (Vector3.Distance(transform.position, homeWorld) >= pullDistance)
                Pull();
        }

        private void Pull()
        {
            IsPulled = true;
            grab.ForceRelease();

            if (dropAfterPull && _rb)
            {
                _rb.isKinematic = false;
                _rb.useGravity = true;
            }

            owner.PullPin();
            if (hideDelay > 0f) Destroy(gameObject, hideDelay);
        }
    }
}