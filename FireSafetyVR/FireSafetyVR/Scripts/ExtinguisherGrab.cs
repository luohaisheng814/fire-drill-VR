using UnityEngine;
using UnityEngine.Events;

namespace FireSafetyVR
{

    /// <summary>
    /// 灭火器的"手柄抓取"：
    ///   · 手柄（Grip）在范围内单击 → 灭火器立即吸附到手柄，被手柄完全控制
    ///   · 放下必须"双击"（0.4 秒内按两下），单击不会掉落
    ///   · 拿起 / 放下都播放音效
    ///   · 拿起瞬间通知 GameManager 弹出消防安全步骤排序页面
    /// 挂在灭火器上（与 FireExtinguisher 同一物体）。
    ///
    /// 依赖：UnityEngine.XR（内置）+ 你自己准备的 AudioManager / GameManager 脚本。
    /// 不依赖 Input System、XR Interaction Toolkit。
    /// </summary>
    public class ExtinguisherGrab : MonoBehaviour
    {
        [Header("手柄（XR Origin 下左右 Controller 的真实手柄/握持点）")]
        public Transform rightHand;
        public Transform leftHand;

        [Header("抓取设置")]
        [Range(0.05f, 1f)] public float grabRange = 0.35f;
        [Tooltip("双击判定窗口（秒）")]
        public float doubleClickWindow = 0.4f;
        [Tooltip("用扳机键代替握持键抓取（部分手柄没有 gripButton）")]
        public bool useTriggerInsteadOfGrip = false;

        [Header("握持姿态（相对手柄的本地偏移，用来调手感）")]
        public Vector3 holdPositionOffset = new Vector3(0f, -0.05f, 0.1f);
        public Vector3 holdRotationOffset = new Vector3(0f, 0f, 0f);
        public float attachLerpTime = 0.12f;

        [Header("放下")]
        public bool keepUprightOnDrop = true;
        public Transform homePoint;          // 可选：放下后飞回原位

        [Header("调试（无头显）")]
        public bool keyboardDebug = true;
        public KeyCode debugGrabKey = KeyCode.F;

        [Header("事件（可选在 Inspector 里连线）")]
        public UnityEvent onPickedUp;
        public UnityEvent onDropped;

        public bool IsHeld { get; private set; }

        Rigidbody rb;
        Transform heldHand;
        bool heldByRight;
        float lastPressTime = -10f;
        int pressCount;
        Coroutine attachRoutine;

        // 手柄按键边沿检测状态
        bool prevRight, prevLeft;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = gameObject.AddComponent<Rigidbody>();
                rb.mass = 4f;
                rb.drag = 0.2f;
                rb.angularDrag = 1f;
            }
        }

        void Update()
        {
            XRHandInput.Btn btn = useTriggerInsteadOfGrip ? XRHandInput.Btn.Trigger : XRHandInput.Btn.Grip;

            bool rDown = XRHandInput.WasPressed(UnityEngine.XR.XRNode.RightHand, btn, ref prevRight);
            bool lDown = XRHandInput.WasPressed(UnityEngine.XR.XRNode.LeftHand, btn, ref prevLeft);
            bool debugPress = keyboardDebug && Input.GetKeyDown(debugGrabKey);

            if (!IsHeld)
            {
                bool r = rDown || debugPress;
                bool l = lDown;
                if (r && InRange(rightHand)) Attach(rightHand, true);
                else if (l && InRange(leftHand)) Attach(leftHand, false);
                return;
            }

            // 已持有：只有"持有那只手"的按键才算数
            bool press = debugPress || (heldByRight ? rDown : lDown);
            if (!press) return;

            if (Time.time - lastPressTime <= doubleClickWindow) pressCount++;
            else pressCount = 1;
            lastPressTime = Time.time;

            if (pressCount >= 2)
            {
                pressCount = 0;
                Detach();
            }
        }

        bool InRange(Transform hand)
        {
            if (hand == null) return false;
            return Vector3.Distance(hand.position, transform.position) <= grabRange;
        }

        void Attach(Transform hand, bool isRight)
        {
            IsHeld = true;
            heldHand = hand;
            heldByRight = isRight;
            pressCount = 0;

            if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }

            if (attachRoutine != null) StopCoroutine(attachRoutine);
            attachRoutine = StartCoroutine(AttachLerp(hand));

            if (AudioManager.Instance != null)
                AudioManager.Instance.PlayAt(AudioManager.Instance.pickupClip, transform.position);

            Debug.Log("[灭火器] 已拾取，双击 Grip 放下");
            onPickedUp?.Invoke();

            if (GameManager.Instance != null) GameManager.Instance.OnExtinguisherPicked(this);
        }

        void Detach()
        {
            IsHeld = false;
            heldHand = null;

            transform.SetParent(null);

            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            if (keepUprightOnDrop)
                transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            if (homePoint != null)
                transform.position = homePoint.position;

            if (AudioManager.Instance != null)
                AudioManager.Instance.PlayAt(AudioManager.Instance.dropClip, transform.position);

            Debug.Log("[灭火器] 已放下");
            onDropped?.Invoke();
        }

        System.Collections.IEnumerator AttachLerp(Transform hand)
        {
            transform.SetParent(null);
            transform.SetParent(hand, true);   // 保持当前世界位姿，再平滑吸附到握持点

            Vector3 startPos = transform.localPosition;
            Quaternion startRot = transform.localRotation;
            Quaternion endRot = Quaternion.Euler(holdRotationOffset);
            float t = 0f;

            while (t < 1f)
            {
                t += Time.deltaTime / Mathf.Max(0.01f, attachLerpTime);
                transform.localPosition = Vector3.Lerp(startPos, holdPositionOffset, t);
                transform.localRotation = Quaternion.Slerp(startRot, endRot, t);
                yield return null;
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, grabRange);
        }
    }
}
