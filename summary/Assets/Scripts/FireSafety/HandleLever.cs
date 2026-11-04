using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace FireSafety
{
    /// <summary>
    /// 压把（红色握把）：握住灭火器后，用另一只手按向压把，或扣住扳机（Activate），
    /// 即判定「按下压把」；按住不放则持续喷射，松开即停。
    /// 未拔出保险销时按压会被拒绝（播放提示音，压把回弹）。
    /// </summary>
    public class HandleLever : MonoBehaviour
    {
        [Header("引用")]
        public Extinguisher owner;
        [Tooltip("旋转的压把本体（按下的动画对象）")]
        public Transform lever;
        [Tooltip("左右手柄位置，用于「手按到压把」的触碰判定")]
        public Transform[] handPoints = new Transform[2];

        [Header("按压判定")]
        [Tooltip("手柄与压把距离小于该值即视为按住压把")]
        public float touchRadius = 0.09f;
        [Tooltip("允许扣扳机（Activate）代替按压")]
        public bool allowTrigger = true;
        [Tooltip("压把按下时绕本地 X 轴旋转的角度（正值向下）")]
        public float pressAngle = 22f;
        [Tooltip("旋转跟随速度")]
        public float smoothSpeed = 12f;
        [Tooltip("被拒绝时压把轻微下沉的幅度（0~1）")]
        [Range(0f, 1f)] public float deniedDip = 0.35f;

        [Header("输入")]
        public InputActionReference leftActivateAction;
        public InputActionReference rightActivateAction;

        [Header("音效")]
        public AudioSource audioSource;
        public AudioClip pressClip;
        public AudioClip blockedClip;
        [Range(0f, 1f)] public float sfxVolume = 1f;

        [Header("事件")]
        [Tooltip("首次成功按下压把（已拔销）时触发")]
        public UnityEvent onPressed;
        public UnityEvent onReleased;

        /// <summary>压把当前是否被按住（按住期间持续喷射）。</summary>
        public bool IsSqueezed { get; private set; }

        /// <summary>是否已经成功按下过一次（对应步骤 ⑦）。</summary>
        public bool PressedOnce { get; private set; }

        Quaternion _baseRot;
        float _angle;            // 0 = 松开，1 = 按到底
        float _deniedDip;
        bool _prevWant;
        bool _externalPress;     // 桌面鼠标按住的压把（保持按下）

        private void Awake()
        {
            if (!owner) owner = GetComponentInParent<Extinguisher>();
            if (lever) _baseRot = lever.localRotation;
        }

        private void OnEnable()
        {
            Enable(leftActivateAction);
            Enable(rightActivateAction);
        }

        private void OnDisable()
        {
            Disable(leftActivateAction);
            Disable(rightActivateAction);
            IsSqueezed = false;
        }

        private void Start()
        {
            if (audioSource == null)
            {
                var ext = owner;
                if (ext != null && ext.audioSource != null) audioSource = ext.audioSource;
            }
            if (pressClip == null) pressClip = Resources.Load<AudioClip>("Sounds/SFX/ui_click");
            if (blockedClip == null) blockedClip = Resources.Load<AudioClip>("Sounds/SFX/quiz_wrong");
        }

        private void Update()
        {
            bool held = IsHeldByPlayer();

            // 桌面/外部输入按下的压把：灭火器一离手立即复位
            if (_externalPress && !held) _externalPress = false;

            bool want = _externalPress || ReadWant();
            bool allowed = owner == null || owner.PinPulled;

            // 逻辑门槛：必须手持灭火器，压把才起作用
            if (want && !held)
            {
                IsSqueezed = false;
                _deniedDip = 0f;
                AnimateLever(0f);
                _prevWant = want;
                return;
            }

            if (want)
            {
                if (!allowed)
                {
                    // 还没拔销：压把按不动
                    IsSqueezed = false;
                    if (!_prevWant) Reject();
                    _deniedDip = deniedDip;
                    _prevWant = true;
                    AnimateLever(0f);
                    return;
                }

                if (!IsSqueezed)
                {
                    IsSqueezed = true;
                    if (!PressedOnce)
                    {
                        PressedOnce = true;
                        Play(pressClip, sfxVolume);
                        onPressed?.Invoke();
                        FireDrillManager.Instance?.CompleteStep(DrillStep.HandlePressed);
                        Debug.Log("[Handle] 压把已按下（步骤 ⑦），开始输出喷射压力");
                    }
                }
                AnimateLever(1f);
            }
            else
            {
                if (IsSqueezed)
                {
                    IsSqueezed = false;
                    onReleased?.Invoke();
                    Debug.Log("[Handle] 松开压把，停止喷射");
                }
                _deniedDip = 0f;
                AnimateLever(0f);
            }

            _prevWant = want;
        }

        /// <summary>逻辑门槛：必须手持灭火器，压把才允许起作用。</summary>
        public bool IsHeldByPlayer()
        {
            return owner != null && owner.grab != null && owner.grab.IsHeld;
        }

        /// <summary>
        /// 鼠标等外部输入「按下压把」：按住状态保持，直到 ReleaseExternal。
        /// 未手持灭火器或未拔销都会拒绝，返回 false。
        /// </summary>
        public bool PressExternal()
        {
            if (owner == null) return false;
            if (!IsHeldByPlayer())
            {
                Debug.Log("[Handle] 需先手持灭火器，压把才起作用。");
                return false;
            }
            if (!owner.PinPulled)
            {
                Reject();
                _deniedDip = deniedDip;
                return false;
            }

            _externalPress = true;
            if (!IsSqueezed)
            {
                IsSqueezed = true;
                if (!PressedOnce)
                {
                    PressedOnce = true;
                    Play(pressClip, sfxVolume);
                    onPressed?.Invoke();
                    FireDrillManager.Instance?.CompleteStep(DrillStep.HandlePressed);
                    Debug.Log("[Handle] 压把已按下（步骤 ⑦）");
                }
            }
            return true;
        }

        /// <summary>鼠标等外部输入松开压把（放下灭火器时也应调用）。</summary>
        public void ReleaseExternal()
        {
            if (!_externalPress) return;
            _externalPress = false;
            if (IsSqueezed)
            {
                IsSqueezed = false;
                onReleased?.Invoke();
                Debug.Log("[Handle] 松开压把，停止喷射");
            }
        }

        /// <summary>读取按压意图：另只手按到压把，或扣住扳机（Activate）。</summary>
        private bool ReadWant()
        {
            if (owner == null) return false;

            // 触碰判定：排除正在握持灭火器的那只手，避免刚拿起就误触
            if (handPoints != null)
            {
                HandSide holding = owner.grab != null ? owner.grab.CurrentHandSide : HandSide.None;
                for (int i = 0; i < handPoints.Length; i++)
                {
                    var h = handPoints[i];
                    if (h == null || lever == null) continue;
                    HandSide side = i == 0 ? HandSide.Left : HandSide.Right;
                    if (side == holding) continue;
                    if (Vector3.Distance(h.position, lever.position) <= touchRadius) return true;
                }
            }            // 扳机判定：握住灭火器时扣下扳机
            if (allowTrigger && owner.grab != null && owner.grab.IsHeld)
            {
                var a = owner.grab.CurrentHandSide == HandSide.Left ? leftActivateAction : rightActivateAction;
                if (a == null) a = leftActivateAction != null ? leftActivateAction : rightActivateAction;
                if (a != null && a.action.IsPressed()) return true;
            }

            return false;
        }

        private void Reject()
        {
            Play(blockedClip, sfxVolume * 0.7f);
            Debug.Log("[Handle] 保险销未拔出，压把无法按下。");
        }

        private void AnimateLever(float target)
        {
            if (lever == null) return;

            // 被拒绝时压把只轻微下沉，随即回弹
            float t = target > 0f ? target : -_deniedDip;
            _angle = Mathf.Lerp(_angle, t, Time.deltaTime * smoothSpeed);
            lever.localRotation = _baseRot * Quaternion.Euler(_angle * pressAngle, 0f, 0f);
        }

        private void Play(AudioClip clip, float volume)
        {
            if (clip == null) return;
            if (audioSource != null) audioSource.PlayOneShot(clip, volume);
            else AudioSource.PlayClipAtPoint(clip, transform.position, volume);
        }

        private static void Enable(InputActionReference r) { if (r != null) r.action.Enable(); }
        private static void Disable(InputActionReference r) { if (r != null) r.action.Disable(); }
    }
}
