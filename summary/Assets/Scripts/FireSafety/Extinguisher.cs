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
            bool trigger = false;
            if (grab != null && grab.IsHeld)
            {
                var a = grab.CurrentHandSide == HandSide.Left ? leftActivateAction : rightActivateAction;
                if (a == null) a = leftActivateAction != null ? leftActivateAction : rightActivateAction;
                trigger = a != null && a.action.IsPressed();
            }

            if (trigger && !_prevTrigger && !PinPulled)
            {
                if (audioSource && blockedClip) audioSource.PlayOneShot(blockedClip, sfxVolume);
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
        }

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