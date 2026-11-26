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

        /// <summary>第 35 轮：Grip 按下但离瓶身太远时广播「手到瓶的距离」，由 ExtinguisherCoach 转成提示。</summary>
        public event System.Action<float> GrabRejected;

        [Header("距离提示（第 43 轮）")]
        [Tooltip("只有手在这个距离内按下 Grip 才会提示「还差 X 米」；站得再远就不弹提示")]
        public float rejectHintMaxHandDistance = 3.0f;
        [Tooltip("同一次按下/抖动导致的最短提示间隔（秒），避免按住手柄时刷屏")]
        public float rejectHintCooldown = 1.2f;
        float _lastRejectHintAt = -10f;

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
            // 第 44 轮：答题面板开着时（事前教育 / 灭火器常识题均已取消，这里作为兜底）
            // 一律不处理抓取按键，也就不会再弹「手柄离瓶身还差 X 米」压在题目上。
            if (QuizUiOpen()) return;
            // 第 44 轮：桌面鼠标操作时手柄按键不参与判定 —— 桌面右键点灭火器会被当成 Grip 按下，
            // 手部锚点离瓶 2-3 米，于是每点一次就弹一次「还差 2.73 米」。桌面模式的距离提示交给
            // DesktopDrillInput.TryPickupExtinguisher 的距离校验（点灭火器时才提示）。
            if (DesktopInputActive) return;
            // 桌面鼠标接管时（_holding 为空），忽略手柄按键，避免两套输入互相打架
            if (IsHeld && _holding == null) return;

            foreach (var h in hands)
            {
                if (h == null || h.attachPoint == null || h.grabAction == null) continue;

                // 第 43 轮：按下判定改用 Input System 的 WasPressedThisFrame()。
                // 原来自己维护 prevPressed，在模拟量 Grip 有阈值抖动时会一帧 true 一帧 false，
                // 于是每帧都被当成「刚按下」，离瓶很远也反复广播 GrabRejected，
                // 玩家看到的就是「没点灭火器也一直提示距离」。
                bool justPressed = h.grabAction.action.WasPressedThisFrame();
                if (!justPressed) continue;

                if (!IsHeld)
                {
                    if (!_locked && InRange(h)) Grab(h);
                    else if (!InRange(h)) ReportGrabRejected(h);
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

        /// <summary>桌面鼠标输入接管（没有头显控制器）时为真。</summary>
        static bool DesktopInputActive
        {
            get
            {
                var desktop = DesktopDrillInput.Instance;
                if (desktop != null && desktop.isActiveAndEnabled) return true;
                return RuntimeDeviceMode.HasInstance && !RuntimeDeviceMode.XRControlActive;
            }
        }

        static float _quizCheckAt = -10f;
        static bool _quizOpenCache;

        /// <summary>任答题面板（事前教育 / 灭火器常识）打开时为真（0.25 秒缓存，避免每帧全场查找）。</summary>
        public static bool QuizUiOpen()
        {
            if (Time.unscaledTime - _quizCheckAt < 0.25f) return _quizOpenCache;
            _quizCheckAt = Time.unscaledTime;
            bool open = false;
            var pre = PreTrainingQuiz.Instance;
            if (pre != null && pre.IsOpen) open = true;
            if (!open)
            {
                var desktops = FindObjectsByType<DesktopDrillInput>(FindObjectsSortMode.None);
                foreach (var d in desktops)
                    if (d != null && d.quizPanel != null && d.quizPanel.IsOpen) { open = true; break; }
            }
            if (!open)
            {
                foreach (var e in FindObjectsByType<Extinguisher>(FindObjectsSortMode.None))
                    if (e != null && e.quizPanel != null && e.quizPanel.IsOpen) { open = true; break; }
            }
            _quizOpenCache = open;
            return open;
        }

        /// <summary>
        /// 第 43 轮：「距离灭火器还有多远」只在玩家真的按 Grip 去抓、且手离瓶不太远时才提示，
        /// 并且同一只手 1.2 秒内只提示一次 —— 不再出现「没碰灭火器也一直提醒」。
        /// </summary>
        void ReportGrabRejected(HandBinding h)
        {
            if (!rangePivot) rangePivot = transform;
            if (h == null || h.attachPoint == null) return;
            float handDistance = Vector3.Distance(h.attachPoint.position, rangePivot.position);
            if (handDistance > grabRange + rejectHintMaxHandDistance) return;   // 站得太远：不提示，避免刷屏
            if (Time.unscaledTime - _lastRejectHintAt < rejectHintCooldown) return;
            _lastRejectHintAt = Time.unscaledTime;
            GrabRejected?.Invoke(handDistance);
        }

        private void Grab(HandBinding h)
        {
            ReleaseOthers(h.side);
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

            ReleaseOthers(side);
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

        /// <summary>一只手只拿一件：抓起新物体时，把同侧手上已有的一件放下。</summary>
        private void ReleaseOthers(HandSide side)
        {
            foreach (var g in FindObjectsByType<ToggleGrab>(FindObjectsSortMode.None))
            {
                if (g == null || g == this || !g.IsHeld) continue;
                if (g.CurrentHandSide != side) continue;
                g.ForceRelease();
            }
        }

        private void Release(HandBinding h)
        {            if (_snap != null) { StopCoroutine(_snap); _snap = null; }

            transform.SetParent(_originalParent, true);
            _rb.isKinematic = _wasKinematic;
            _rb.useGravity = _usedGravity;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            // 放下的瓶子必须当场落回去：刚体若还停在休眠态，会留在半空「悬着」
            if (!_rb.isKinematic) _rb.WakeUp();

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