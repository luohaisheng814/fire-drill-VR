using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace FireSafety
{
    /// <summary>灭火器主体：衔接步骤 ⑤⑥，管理保险销、喷射、灭火判定与音效。</summary>
    public class Extinguisher : MonoBehaviour
    {
        [Header("引用")]
        public ToggleGrab grab;
        public SafetyPin pin;
        [Tooltip("喷嘴口空物体，Z 轴（蓝轴）朝前作为喷射方向")]
        public Transform nozzle;
        public ParticleSystem sprayParticles;
        [Tooltip("压把：按下压把才输出喷射压力（步骤 ⑦）")]
        public HandleLever handleLever;

        [Header("桌面调试模式")]
        [Tooltip("开启后喷射改由外部输入触发（如鼠标右键双击），压把不再自动持续喷射")]
        public bool desktopMode = false;

        [Header("音效")]
        public AudioSource audioSource;      // 一次性音效（拔销等）
        public AudioSource sprayLoopSource;  // 喷射循环音
        public AudioClip pinPullClip;
        public AudioClip sprayLoopClip;
        public AudioClip blockedClip;        // 未拔销提示音（可选）
        [Range(0f, 1f)] public float sfxVolume = 1f;

        [Header("扳机（Activate）输入")]
        public InputActionReference leftActivateAction;   // XRI LeftHand/Activate
        public InputActionReference rightActivateAction;  // XRI RightHand/Activate

        [Header("喷射参数")]
        public float sprayRange = 3f;
        [Tooltip("喷射有效半角（度）")]
        public float sprayAngle = 20f;
        [Tooltip("每秒灭火量，越大灭得越快")]
        public float extinguishRate = 1f;
        public bool checkOcclusion = false;
        public LayerMask blockingMask = ~0;

        [Header("常识问答（拾取灭火器时弹出）")]
        [Tooltip("拾取灭火器后必须先完成常识问答才能拔销/喷射")]
        public bool quizRequired = true;
        public ExtinguisherQuizPanel quizPanel;
        public bool QuizPassed { get; private set; }
        public bool CanPullPin => !quizRequired || QuizPassed;

        [Header("步骤判定")]
        [Tooltip("玩家离灭火器多近算“移动到灭火器处”")]
        public float reachRadius = 1.2f;
        public string playerTag = "Player";
        public Transform playerOverride;

        [Header("事件")]
        public UnityEvent onPinPulled;
        public UnityEvent onSprayStarted;
        public UnityEvent onSprayStopped;

        public bool PinPulled { get; private set; }
        public bool IsSpraying { get; private set; }

        private Transform _player;
        private bool _reachedReported;
        private bool _prevTrigger;

        private void Awake()
        {
            // 兜底解析：场景里 pin / quizPanel 若是空的，从子物体里找回来（否则鼠标拔销会没有反应）
            if (pin == null) pin = GetComponentInChildren<SafetyPin>(true);
            if (quizPanel == null) quizPanel = GetComponentInChildren<ExtinguisherQuizPanel>(true);
        }

        private void OnEnable()
        {
            if (grab != null)
            {
                grab.onPickedUp.AddListener(OnPickedUp);
                grab.onDropped.AddListener(OnDropped);
            }
            EnableAction(leftActivateAction);
            EnableAction(rightActivateAction);
        }

        private void OnDisable()
        {
            if (grab != null)
            {
                grab.onPickedUp.RemoveListener(OnPickedUp);
                grab.onDropped.RemoveListener(OnDropped);
            }
            DisableAction(leftActivateAction);
            DisableAction(rightActivateAction);
            StopSpray();
        }

        private void Start()
        {
            if (playerOverride) _player = playerOverride;
            else
            {
                var go = GameObject.FindGameObjectWithTag(playerTag);
                if (go) _player = go.transform;
                else Debug.LogWarning($"[Extinguisher] 找不到 Tag 为 {playerTag} 的对象。");
            }

            if (pinPullClip == null) pinPullClip = Resources.Load<AudioClip>("Sounds/SFX/pin_pull");
            if (sprayLoopClip == null) sprayLoopClip = Resources.Load<AudioClip>("Sounds/SFX/spray_loop");

            if (!audioSource) audioSource = GetComponent<AudioSource>();
            if (sprayLoopSource)
            {
                sprayLoopSource.clip = sprayLoopClip;
                sprayLoopSource.loop = true;
                sprayLoopSource.playOnAwake = false;
            }

            // 无论从手柄抓取还是桌面右键进入问答，答完都要把结果记到灭火器上
            if (quizPanel != null)
            {
                quizPanel.onPassed.RemoveListener(OnQuizPassed);
                quizPanel.onPassed.AddListener(OnQuizPassed);
            }
        }

        /// <summary>外部（桌面输入）确认问答已通过。</summary>
        public void MarkQuizPassed() => QuizPassed = true;

        /// <summary>问答未通过，保持不能拔销/喷射。</summary>
        public void MarkQuizFailed() => QuizPassed = false;

        /// <summary>
        /// 第二 / 第三阶段重复灭火流程：清空问答状态并刷成最新 5 题，
        /// 玩家重新右键拿起这台灭火器时会再答一遍题。
        /// </summary>
        public void ResetQuizForStageTwo()
        {
            QuizPassed = false;
            if (quizPanel != null)
            {
                quizPanel.ResetQuiz();
                quizPanel.ResetToDefaultQuestions();
            }
            Debug.Log("[Extinguisher] 第二阶段问答已重置（5 题），重新拾取需再次作答。");
        }

        /// <summary>
        /// 让灭火器「立着」：最长的一轴转到竖直方向，可选把瓶底贴到地面。
        /// 美术 prefab 的 pivot 与瓶身不重合、朝向也不统一，靠写死 rotation 会横躺，
        /// 所以这里按渲染包围盒判断长轴，只在不竖时补转 90°。
        /// </summary>
        public void EnsureUprightStanding(bool dropToFloor)
        {
            var rend = GetComponentInChildren<Renderer>();
            if (rend == null) return;

            Bounds b = rend.bounds;
            int guard = 0;
            while (guard < 3 && Mathf.Max(b.size.x, b.size.z) > b.size.y * 1.05f)
            {
                if (b.size.z >= b.size.x) transform.Rotate(90f, 0f, 0f, Space.World);   // 长轴 Z → Y
                else transform.Rotate(0f, 0f, 90f, Space.World);                        // 长轴 X → Y
                b = rend.bounds;
                guard++;
            }

            if (dropToFloor)
            {
                b = rend.bounds;
                transform.position += new Vector3(0f, -b.min.y, 0f);
            }

            if (guard > 0) Debug.Log("[Extinguisher] 已把 " + name + " 纠正为竖放（转 " + guard + " 次）");
        }

        private void Update()
        {
            CheckReach();   // ④
            CheckSpray();   // 喷射
        }

        private void CheckReach()
        {
            if (_reachedReported || !_player) return;
            if (Vector3.Distance(_player.position, transform.position) <= reachRadius)
            {
                _reachedReported = true;
                FireDrillManager.Instance?.CompleteStep(DrillStep.ReachedExtinguisher);
            }
        }

        private void OnPickedUp()   // ⑤
        {
            var mgr = FireDrillManager.Instance;
            if (mgr != null)
            {
                mgr.ForceCompleteUpTo(DrillStep.ReachedExtinguisher); // 兜底补 ④
                mgr.CompleteStep(DrillStep.ExtinguisherGrabbed);
            }

            // 弹出灭火器常识问答（回答完毕后才能拔销/喷射）
            if (quizRequired && !QuizPassed && quizPanel != null)
            {
                quizPanel.onPassed.RemoveListener(OnQuizPassed);
                quizPanel.onPassed.AddListener(OnQuizPassed);
                quizPanel.Open(transform);
            }
        }

        private void OnQuizPassed()
        {
            QuizPassed = true;
            Debug.Log("[Extinguisher] 常识问答通过，现在可以拔销灭火");
        }

        private void OnDropped() => StopSpray();

        /// <summary>由 SafetyPin 在销子被拉出时调用。</summary>
        public void PullPin()   // ⑥
        {
            if (PinPulled) return;
            if (quizRequired && !QuizPassed)
            {
                Debug.Log("[Extinguisher] 请先完成灭火器常识问答再拔销。");
                return;
            }
            PinPulled = true;

            if (audioSource && pinPullClip) audioSource.PlayOneShot(pinPullClip, sfxVolume);
            onPinPulled?.Invoke();
            FireDrillManager.Instance?.CompleteStep(DrillStep.PinPulled);
            Debug.Log("[Extinguisher] 保险销已拔出");
        }

        private void CheckSpray()
        {
            // 桌面调试模式：喷射由外部（右键双击）触发，这里只持续结算灭火
            if (desktopMode)
            {
                if (IsSpraying) ApplySprayToFires();
                return;
            }

            // 压把按下 = 输出压力；没有压把时退回“扣扳机直接喷射”的旧逻辑
            bool trigger = false;
            if (handleLever != null)
            {
                trigger = handleLever.IsSqueezed;
            }
            else if (grab != null && grab.IsHeld)
            {
                var a = grab.CurrentHandSide == HandSide.Left ? leftActivateAction : rightActivateAction;
                if (a == null) a = leftActivateAction != null ? leftActivateAction : rightActivateAction;
                trigger = a != null && a.action.IsPressed();
            }

            if (trigger && !_prevTrigger && !PinPulled)
            {
                if (handleLever == null && audioSource && blockedClip)
                    audioSource.PlayOneShot(blockedClip, sfxVolume);
                Debug.Log("[Extinguisher] 请先拔出保险销！");
            }

            if (trigger && PinPulled)
            {
                if (quizRequired && !QuizPassed)
                {
                    if (!_prevTrigger) Debug.Log("[Extinguisher] 请先完成灭火器常识问答再喷射！");
                }
                else StartSpray();
            }
            else if (!trigger && IsSpraying) StopSpray();

            if (IsSpraying) ApplySprayToFires();
            _prevTrigger = trigger;
        }

        private void StartSpray()
        {
            if (IsSpraying) return;
            IsSpraying = true;
            if (sprayParticles) sprayParticles.Play();
            if (sprayLoopSource && sprayLoopClip) sprayLoopSource.Play();
            onSprayStarted?.Invoke();

            // ⑦ 按下压把 → ⑧ 开始喷射
            var mgr = FireDrillManager.Instance;
            if (mgr != null)
            {
                if (handleLever == null) mgr.ForceCompleteUpTo(DrillStep.HandlePressed); // 无压把时兜底
                mgr.CompleteStep(DrillStep.Spraying);
            }
            Debug.Log("[Extinguisher] 开始喷射");
        }

        /// <summary>
        /// 外部输入（鼠标右键双击等）尝试开始喷射，逐项校验门控：
        /// 必须手持 → 已拔销 → 问答通过 → 压把已按下。返回是否成功起喷。
        /// </summary>
        public bool TryStartSpray()
        {
            if (IsSpraying) { StopSprayExternal(); return false; }

            if (grab == null || !grab.IsHeld)
            {
                Debug.Log("[Extinguisher] 需先拿起灭火器。");
                return false;
            }
            if (!PinPulled)
            {
                if (audioSource && blockedClip) audioSource.PlayOneShot(blockedClip, sfxVolume);
                Debug.Log("[Extinguisher] 请先拔出保险销。");
                return false;
            }
            if (quizRequired && !QuizPassed)
            {
                Debug.Log("[Extinguisher] 请先完成灭火器常识问答。");
                return false;
            }
            if (handleLever != null && !handleLever.PressedOnce)
            {
                Debug.Log("[Extinguisher] 请先按下压把。");
                return false;
            }

            StartSpray();
            return true;
        }

        /// <summary>外部输入：停止喷射（桌面鼠标右键双击 / 放下灭火器）。</summary>
        public void StopSprayExternal() => StopSpray();

        private void StopSpray()
        {
            if (!IsSpraying) return;
            IsSpraying = false;
            if (sprayParticles) sprayParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (sprayLoopSource) sprayLoopSource.Stop();
            onSprayStopped?.Invoke();
        }

        private void ApplySprayToFires()
        {
            if (!nozzle) return;

            Vector3 origin = nozzle.position;
            Vector3 forward = nozzle.forward;
            float cos = Mathf.Cos(sprayAngle * Mathf.Deg2Rad);

            foreach (var fire in FireSource.Active)
            {
                if (fire == null || fire.IsExtinguished) continue;

                Vector3 toFire = fire.AimPoint - origin;
                float dist = toFire.magnitude;
                if (dist > sprayRange) continue;
                if (Vector3.Dot(forward, toFire.normalized) < cos) continue;

                if (checkOcclusion &&
                    Physics.Raycast(origin, toFire.normalized, out var hit, dist, blockingMask))
                {
                    if (!(hit.transform == fire.transform || hit.transform.IsChildOf(fire.transform)))
                        continue;
                }

                fire.ApplyExtinguish(Time.deltaTime * extinguishRate);
            }
        }

        private static void EnableAction(InputActionReference r) { if (r != null) r.action.Enable(); }
        private static void DisableAction(InputActionReference r) { if (r != null) r.action.Disable(); }
    }
}