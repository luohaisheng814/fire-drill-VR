using UnityEngine;

namespace FireSafetyVR
{

    public enum ExtinguisherStage
    {
        Locked,       // 0：保险销还在，不可喷射
        PinRemoved,   // 1：已拔出保险销
        NozzleReady,  // 2：灭火口已展开，等待对准根部
        Spraying      // 3：正在喷射
    }

    /// <summary>
    /// 灭火器三段式操作流程：
    ///   ① 扳机点击 → 拔出保险销
    ///   ② 扳机点击 → 形态转换，灭火口出现
    ///   ③ 灭火口对准火焰根部 → 扳机点击 → 喷射带粒子效果的干粉（并削减火焰生命值）
    /// 与 ExtinguisherGrab 挂在同一物体（灭火器）上。
    ///
    /// 依赖：UnityEngine.XR（内置）+ FireTarget / AudioManager / GameManager。
    /// 不依赖 Input System、XR Interaction Toolkit。
    /// </summary>
    public class FireExtinguisher : MonoBehaviour
    {
        [Header("部件引用")]
        public GameObject pinObject;        // 保险销（初始显示，拔掉后隐藏）
        public GameObject nozzleGroup;      // 灭火口/软管（初始隐藏）
        public Transform nozzleTip;         // 喷射口，蓝色轴(Z/forward)为喷射方向
        public ParticleSystem sprayParticle;
        public AudioSource sprayAudio;      // 循环喷射音，clip 由 AudioManager 指定

        [Header("目标火焰")]
        public FireTarget targetFire;
        public float aimMaxDistance = 3.0f;
        public float aimMaxAngle = 35f;
        public float damagePerSecond = 40f;
        [Tooltip("必须先通过消防步骤排序小测才能喷射")]
        public bool requireQuizPassed = true;

        [Header("操作按键")]
        [Tooltip("用握持键代替扳机键触发三步流程")]
        public bool useGripInsteadOfTrigger = false;
        [Tooltip("未戴头显时用键盘调试")]
        public bool keyboardDebug = true;
        public KeyCode debugUseKey = KeyCode.E;

        [Header("喷射量（可选）")]
        public bool limitSprayTime = false;
        public float spraySeconds = 12f;

        public ExtinguisherStage Stage { get; private set; } = ExtinguisherStage.Locked;

        ExtinguisherGrab grab;
        float sprayLeft;
        bool aimingLastFrame;
        bool prevRight, prevLeft;

        void Awake()
        {
            grab = GetComponent<ExtinguisherGrab>();
            if (nozzleGroup != null) nozzleGroup.SetActive(false);
            if (pinObject != null) pinObject.SetActive(true);
            if (sprayParticle != null) sprayParticle.Stop();
            if (sprayAudio != null)
            {
                sprayAudio.loop = true;
                sprayAudio.playOnAwake = false;
                if (AudioManager.Instance != null) sprayAudio.clip = AudioManager.Instance.sprayClip;
            }
            sprayLeft = spraySeconds;
        }

        void OnDisable() => StopSpray();

        void Update()
        {
            if (UsePressedOnce()) AdvanceStage();

            if (Stage == ExtinguisherStage.Spraying)
            {
                bool aiming = IsAimingAtRoot();
                if (aiming && targetFire != null)
                    targetFire.ApplyDamage(damagePerSecond * Time.deltaTime);

                if (aiming != aimingLastFrame)
                {
                    aimingLastFrame = aiming;
                    Hint(aiming ? "命中根部！持续喷射" : "未对准火焰根部，请把灭火口压低对准火根");
                }

                if (limitSprayTime)
                {
                    sprayLeft -= Time.deltaTime;
                    if (sprayLeft <= 0f)
                    {
                        StopSpray();
                        Stage = ExtinguisherStage.NozzleReady;
                        Hint("干粉已用尽");
                    }
                }
            }
        }

        bool UsePressedOnce()
        {
            XRHandInput.Btn btn = useGripInsteadOfTrigger ? XRHandInput.Btn.Grip : XRHandInput.Btn.Trigger;
            bool r = XRHandInput.WasPressed(UnityEngine.XR.XRNode.RightHand, btn, ref prevRight);
            bool l = XRHandInput.WasPressed(UnityEngine.XR.XRNode.LeftHand, btn, ref prevLeft);
            bool pressed = r || l || (keyboardDebug && Input.GetKeyDown(debugUseKey));

            // 必须已经被手柄握住才能操作
            return pressed && (grab == null || grab.IsHeld);
        }

        void AdvanceStage()
        {
            var audio = AudioManager.Instance;

            switch (Stage)
            {
                case ExtinguisherStage.Locked:
                    if (pinObject != null) pinObject.SetActive(false);
                    if (audio != null) audio.PlayAt(audio.pinClip, transform.position);
                    Stage = ExtinguisherStage.PinRemoved;
                    Hint("第 1 步完成：保险销已拔出。再次点击展开灭火口");
                    break;

                case ExtinguisherStage.PinRemoved:
                    if (nozzleGroup != null) nozzleGroup.SetActive(true);
                    if (audio != null) audio.PlayAt(audio.transformClip, transform.position);
                    Stage = ExtinguisherStage.NozzleReady;
                    Hint("第 2 步完成：灭火口已出现。对准火焰根部后点击喷射");
                    break;

                case ExtinguisherStage.NozzleReady:
                    if (requireQuizPassed && GameManager.Instance != null && !GameManager.Instance.QuizPassed)
                    {
                        Hint("请先完成消防安全步骤排序小测");
                        return;
                    }
                    if (!IsAimingAtRoot())
                    {
                        Hint("灭火口没有对准火焰根部（距离不能超过 " + aimMaxDistance + " 米）");
                        return;
                    }
                    StartSpray();
                    Stage = ExtinguisherStage.Spraying;
                    if (GameManager.Instance != null) GameManager.Instance.NotifySpraying();
                    Hint("第 3 步：喷射中！左右扫射火焰根部");
                    break;

                case ExtinguisherStage.Spraying:
                    StopSpray();
                    Stage = ExtinguisherStage.NozzleReady;
                    Hint("已停止喷射");
                    break;
            }
        }

        bool IsAimingAtRoot()
        {
            if (targetFire == null || nozzleTip == null) return false;
            return targetFire.IsAimedBy(nozzleTip, aimMaxDistance, aimMaxAngle);
        }

        void StartSpray()
        {
            if (sprayParticle != null && !sprayParticle.isPlaying) sprayParticle.Play();
            if (sprayAudio != null && !sprayAudio.isPlaying) sprayAudio.Play();
        }

        void StopSpray()
        {
            if (sprayParticle != null && sprayParticle.isPlaying) sprayParticle.Stop();
            if (sprayAudio != null && sprayAudio.isPlaying) sprayAudio.Stop();
        }

        void Hint(string msg)
        {
            if (GameManager.Instance != null) GameManager.Instance.SetHint(msg);
            else Debug.Log("[灭火器] " + msg);
        }
    }
}
