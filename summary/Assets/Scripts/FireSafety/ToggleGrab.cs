using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace FireSafety
{
    public enum HandSide { None = 0, Left = 1, Right = 2 }

    /// <summary>一只手柄的绑定：抓取点 + 抓取按键（XRI LeftHand/Select 或 RightHand/Select，即 Grip）。</summary>
    [Serializable]
    public class HandBinding
    {
        public HandSide side = HandSide.Left;
        public Transform attachPoint;
        public InputActionReference grabAction;
        [HideInInspector] public bool prevPressed;
    }

    /// <summary>
    /// 开关式（Toggle）抓取：按一次 → 吸附到手柄并跟随；再按一次 → 才放下。
    /// 拿起/放下都会播放音效并广播事件。灭火器瓶身与保险销共用此脚本。
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ToggleGrab : MonoBehaviour
    {
        [Header("手柄绑定")]
        public HandBinding[] hands = new HandBinding[2];

        [Header("抓取参数")]
        public float grabRange = 0.4f;
        [Tooltip("开启后：需要连续两次点击才放下（双击窗口内）")]
        public bool doubleClickToRelease = false;
        public float doubleClickWindow = 0.35f;
        public Vector3 heldLocalPosition = Vector3.zero;
        public Vector3 heldLocalEuler = Vector3.zero;
        [Tooltip("吸附过渡时间，0 = 瞬间吸附")]
        public float moveDuration = 0.08f;
        [Tooltip("距离判定参考点，留空则用自身原点")]
        public Transform rangePivot;

        [Header("音效")]
        public AudioSource audioSource;
        public AudioClip pickupClip;
        public AudioClip dropClip;
        [Range(0f, 1f)] public float sfxVolume = 1f;

        [Header("事件")]
        public UnityEvent onPickedUp = new UnityEvent();
        public UnityEvent onDropped = new UnityEvent();

        [Header("限制")]
        [Tooltip("勾选后放下即锁定，不能再抓（保险销拔出后可防止被塞回）")]
        public bool lockAfterRelease = false;

        public bool IsHeld { get; private set; }
        public HandSide CurrentHandSide { get; private set; } = HandSide.None;

        private Rigidbody _rb;
        private Transform _originalParent;
        private bool _wasKinematic, _usedGravity, _locked;
        private HandBinding _holding;
        private Coroutine _snap;
        private float _lastClickTime = -10f;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _originalParent = transform.parent;
            _wasKinematic = _rb.isKinematic;
            _usedGravity = _rb.useGravity;
            if (!rangePivot) rangePivot = transform;
        }

        private void OnEnable()
        {
            if (hands == null) return;
            foreach (var h in hands)
            {
                if (h == null || h.grabAction == null) continue;
                h.grabAction.action.Enable();
                h.prevPressed = false;
            }
        }

        private void OnDisable()
        {
            if (hands == null) return;
            foreach (var h in hands)
                if (h != null && h.grabAction != null) h.grabAction.action.Disable();
        }

        private void Update()
        {
            if (hands == null) return;
            // 桌面鼠标接管时（_holding 为空），忽略手柄按键，避免两套输入互相打架
            if (IsHeld && _holding == null) return;

            foreach (var h in hands)
            {
                if (h == null || h.attachPoint == null || h.grabAction == null) continue;

                bool pressed = h.grabAction.action.IsPressed();
                bool justPressed = pressed && !h.prevPressed;
                h.prevPressed = pressed;
                if (!justPressed) continue;

                if (!IsHeld)
                {
                    if (!_locked && InRange(h)) Grab(h);
                }
                else if (_holding == h)
                {
                    TryRelease(h);              // 第二次点击 → 放下
                }
                else if (InRange(h))
                {
                    Release(_holding); Grab(h); // 另一只手按下 → 换手
                }
            }
        }

        private bool InRange(HandBinding h)
        {
            if (!rangePivot) rangePivot = transform;
            return Vector3.Distance(h.attachPoint.position, rangePivot.position) <= grabRange;
        }

        private void Grab(HandBinding h)
        {
            IsHeld = true; _holding = h; CurrentHandSide = h.side;

            _wasKinematic = _rb.isKinematic;
            _usedGravity = _rb.useGravity;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.useGravity = false;
            _rb.isKinematic = true;

            if (_snap != null) StopCoroutine(_snap);
            transform.SetParent(h.attachPoint, true);
            _snap = StartCoroutine(SnapToHand());

            Play(pickupClip);
            onPickedUp?.Invoke();
        }

        private void TryRelease(HandBinding h)
        {
            if (doubleClickToRelease)
            {
                if (Time.time - _lastClickTime <= doubleClickWindow)
                { _lastClickTime = -10f; Release(h); }
                else _lastClickTime = Time.time;
            }
            else Release(h);
        }

        /// <summary>强制放下（保险销拔出等外部逻辑调用）。</summary>
        public void ForceRelease()
        {
            if (IsHeld) Release(_holding);
        }

        /// <summary>
        /// 桌面鼠标接管：把物体吸附到给定锚点（相机前的手部位置）。
        /// 不经过手柄按键，其余状态（刚体、音效、事件）与正常抓取一致。
        /// </summary>
        public void GrabExternal(Transform anchor, HandSide side = HandSide.Right)
        {
            if (IsHeld || _locked || anchor == null) return;

            IsHeld = true;
            _holding = null;                 // 无手柄绑定
            CurrentHandSide = side;

            _wasKinematic = _rb.isKinematic;
            _usedGravity = _rb.useGravity;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.useGravity = false;
            _rb.isKinematic = true;

            if (_snap != null) StopCoroutine(_snap);
            transform.SetParent(anchor, true);
            if (Application.isPlaying)
            {
                _snap = StartCoroutine(SnapToHand());
            }
            else
            {
                // 编辑模式（自检）下不能跑协程，直接瞬时对齐
                transform.localPosition = heldLocalPosition;
                transform.localRotation = Quaternion.Euler(heldLocalEuler);
            }

            Play(pickupClip);
            onPickedUp?.Invoke();
        }

        /// <summary>桌面鼠标接管：放下当前手持物体（落回原父物体并恢复重力）。</summary>
        public void ReleaseExternal()
        {
            if (!IsHeld || _holding != null) return;
            Release(null);
        }

        private void Release(HandBinding h)
        {
            if (_snap != null) { StopCoroutine(_snap); _snap = null; }

            transform.SetParent(_originalParent, true);
            _rb.isKinematic = _wasKinematic;
            _rb.useGravity = _usedGravity;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;

            IsHeld = false; _holding = null; CurrentHandSide = HandSide.None;
            if (lockAfterRelease) _locked = true;

            Play(dropClip);
            onDropped?.Invoke();
        }

        private IEnumerator SnapToHand()
        {
            Quaternion targetRot = Quaternion.Euler(heldLocalEuler);
            if (moveDuration <= 0f)
            {
                transform.localPosition = heldLocalPosition;
                transform.localRotation = targetRot;
                yield break;
            }

            Vector3 startPos = transform.localPosition;
            Quaternion startRot = transform.localRotation;
            float t = 0f;
            while (t < moveDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / moveDuration);
                transform.localPosition = Vector3.Lerp(startPos, heldLocalPosition, k);
                transform.localRotation = Quaternion.Slerp(startRot, targetRot, k);
                yield return null;
            }
            transform.localPosition = heldLocalPosition;
            transform.localRotation = targetRot;
        }

        private void Play(AudioClip clip)
        {
            if (!clip) return;
            if (audioSource) audioSource.PlayOneShot(clip, sfxVolume);
            else AudioSource.PlayClipAtPoint(clip, transform.position, sfxVolume);
        }
    }
}