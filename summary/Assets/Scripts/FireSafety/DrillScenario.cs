using UnityEngine;
using UnityEngine.Events;

namespace FireSafety
{
    /// <summary>
    /// 演练总流程编排（本轮重构后的两段式流程）：
    ///   操作演练（原第一阶段）：拿灭火器 → 5 题问答 → 拔销 → 压把 → 喷射灭火，只为熟悉流程；
    ///   正式第 1 阶段：随机出生（2~3 个候选点）→ 立刻出现烟雾 + 设备起火 → 必须先切断电源 → 拿灭火器答题 → 灭火；
    ///   正式第 2 阶段：安全标识 / 箭头引导到出口 → 出口前升起火焰 → 情景问答 → 灭火 → 开门通关。
    /// 状态迁移全部有公开方法，便于批处理自检与播放态探针直接驱动。
    /// </summary>
    [DisallowMultipleComponent]
    public class DrillScenario : MonoBehaviour
    {
        public enum Mode
        {
            None = 0,
            Tutorial = 1,   // 操作演练（原第一阶段）
            Formal = 2      // 正式演练（新第 1 阶段 + 第 2 阶段）
        }

        public enum Phase
        {
            Idle = 0,
            Tutorial = 1,
            Stage1 = 2,     // 设备起火 + 断电 + 灭火
            Stage2 = 3,     // 安全出口 + 门前火情 + 开门
            Done = 4
        }

        public static DrillScenario Instance { get; private set; }

        [Header("流程引用（留空自动查找）")]
        public StartScreen startScreen;
        public StageTwoFlow stageTwo;
        public StageOneCompletePanel stagePanel;
        public GuidanceArrows arrows;
        public DrillCountdown countdown;
        public EndingScreen endingScreen;
        public VRDrillHud hud;

        [Header("操作演练（原第一阶段）")]
        [Tooltip("操作演练的站位（留空则在原地开始）")]
        public Transform tutorialSpawn;
        [Tooltip("操作演练的火焰（场景自带 BaseFire000）")]
        public FireSource tutorialFire;
        [Tooltip("操作演练用的灭火器（Extinguisher01，A 组 5 题）")]
        public Extinguisher tutorialExtinguisher;
        [Tooltip("Extinguisher01 的原位（正式演练开始时放回，避免玩家举着旧瓶出生挡住视线）")]
        public Vector3 tutorialExtinguisherHome = new Vector3(-6.26f, 0.16f, 0.83f);

        [Header("正式第 1 阶段：设备起火 + 电源接口 + 灭火器")]
        public FireSource deviceFire;
        [Tooltip("设备冒烟粒子（开始演练后立刻播放）")]
        public ParticleSystem deviceSmoke;
        public PowerSwitch powerSwitch;
        [Tooltip("阶段一用的灭火器（Extinguisher02，C 组电气火灾 5 题）")]
        public Extinguisher deviceExtinguisher;
        [Tooltip("阶段一的房间烟雾（独立于第二阶段 SmokeZone）")]
        public SmokeZone stage1Smoke;

        [Header("出生点（2~3 个候选，开始演练时随机取一个）")]
        public Transform rigRoot;
        public Transform[] spawnPoints;

        [Header("正式第 2 阶段")]
        [Tooltip("出口阶段用的灭火器（Extinguisher03，D 组疏散情景 5 题）")]
        public Extinguisher exitExtinguisher;

        [Header("规则")]
        [Tooltip("出生点是否随机（关闭则固定第 1 个，自检用）")]
        public bool randomSpawn = true;
        [Tooltip("未切断电源时火焰不灭火（电气火灾硬规则）")]
        public bool requirePowerCutBeforeExtinguish = true;

        public Mode CurrentMode { get; private set; } = Mode.None;
        public Phase CurrentPhase { get; private set; } = Phase.Idle;
        public int LastSpawnIndex { get; private set; } = -1;
        public string CurrentHint { get; private set; } = "";

        public int SpawnCount => spawnPoints != null ? spawnPoints.Length : 0;
        public bool PowerCut => powerSwitch != null && powerSwitch.IsCut;
        public bool Stage1Cleared => deviceFire != null && deviceFire.IsExtinguished;
        public bool TutorialCleared => tutorialFire != null && tutorialFire.IsExtinguished;

        private float _hintFlush;
        private bool _subscribed;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            Resolve();
        }

        private void Start()
        {
            Resolve();
            Subscribe();
        }

        private void OnDestroy()
        {
            if (deviceFire != null) deviceFire.onExtinguished.RemoveListener(OnDeviceFireExtinguished);
            if (deviceFire != null) deviceFire.onExtinguishBlocked.RemoveListener(OnDeviceFireBlocked);
            if (tutorialFire != null) tutorialFire.onExtinguished.RemoveListener(OnTutorialFireExtinguished);
            if (Instance == this) Instance = null;
        }

        private void Resolve()
        {
            if (startScreen == null) startScreen = FindObjectOfType<StartScreen>();
            if (stageTwo == null) stageTwo = FindObjectOfType<StageTwoFlow>();
            if (stagePanel == null) stagePanel = FindObjectOfType<StageOneCompletePanel>();
            if (arrows == null) arrows = FindObjectOfType<GuidanceArrows>();
            if (countdown == null) countdown = FindObjectOfType<DrillCountdown>();
            if (endingScreen == null) endingScreen = FindObjectOfType<EndingScreen>();
            if (hud == null) hud = FindObjectOfType<VRDrillHud>();

            if (rigRoot == null)
            {
                var go = GameObject.Find("XR Origin (Drill)");
                if (go != null) rigRoot = go.transform;
            }
        }

        /// <summary>订阅火源 / 电源事件（幂等）。</summary>
        private void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;

            if (deviceFire != null)
            {
                deviceFire.onExtinguished.RemoveListener(OnDeviceFireExtinguished);
                deviceFire.onExtinguished.AddListener(OnDeviceFireExtinguished);
                deviceFire.onExtinguishBlocked.RemoveListener(OnDeviceFireBlocked);
                deviceFire.onExtinguishBlocked.AddListener(OnDeviceFireBlocked);
            }
            if (tutorialFire != null)
            {
                tutorialFire.onExtinguished.RemoveListener(OnTutorialFireExtinguished);
                tutorialFire.onExtinguished.AddListener(OnTutorialFireExtinguished);
            }
            if (powerSwitch != null)
            {
                powerSwitch.onCut.RemoveListener(OnPowerCut);
                powerSwitch.onCut.AddListener(OnPowerCut);
            }
        }

        // ================================================================
        // 操作演练（原第一阶段）
        // ================================================================

        /// <summary>开始界面「操作演练」按钮。</summary>
        public void BeginTutorial()
        {
            if (CurrentMode != Mode.None)
            {
                Hint("当前已在演练中（" + ModeName(CurrentMode) + "）");
                return;
            }

            CurrentMode = Mode.Tutorial;
            CurrentPhase = Phase.Tutorial;

            if (tutorialSpawn != null) TeleportTo(tutorialSpawn);

            if (stagePanel != null)
            {
                stagePanel.autoShow = true;
                stagePanel.continueOverride = BeginFormal;
                stagePanel.SetWording(
                    "操作演练完成",
                    "你已完整走通一次：拿起灭火器 → 5 题问答 → 拔保险销 → 压下压把 → 对准火焰根部喷射 → 扑灭。\n" +
                    "点「进入正式演练」开始正式考核：随机出生 → 设备起火 → 先断电 → 灭火 → 出口火情 → 开门通关。",
                    "进入正式演练",
                    "重新演练");
            }

            if (tutorialFire != null)
            {
                tutorialFire.gameObject.SetActive(true);
                tutorialFire.SetExtinguishLocked(false);
                tutorialFire.IgniteNow();
            }

            if (arrows != null) arrows.Begin();
            if (countdown != null) countdown.Begin();

            Hint("操作演练：右键单击灭火器（VR 按 Grip 抓取）→ 5 题问答 → 拔销 → 压把 → 喷射灭火（本环节只练流程）");
            Debug.Log("[Drill] 进入操作演练（原第一阶段）。");
        }

        private void OnTutorialFireExtinguished()
        {
            if (CurrentMode != Mode.Tutorial) return;
            CurrentPhase = Phase.Done;
            Debug.Log("[Drill] 操作演练火焰已扑灭，流程已走通。");
        }

        // ================================================================
        // 正式演练 · 第 1 阶段
        // ================================================================

        /// <summary>开始界面「开始演练」按钮 → 正式演练第 1 阶段。</summary>
        public void BeginFormal()
        {
            StartFormal(-1);
        }

        /// <summary>正式演练入口（forceSpawn &gt;= 0 时固定出生点，自检 / 探针用）。</summary>
        public void StartFormal(int forceSpawn = -1)
        {
            if (CurrentMode == Mode.Formal)
            {
                Hint("正式演练已在进行中");
                return;
            }

            Resolve();
            Subscribe();

            CurrentMode = Mode.Formal;
            CurrentPhase = Phase.Stage1;

            // 从操作演练的完成面板进入时，先把面板收掉
            if (stagePanel != null)
            {
                stagePanel.autoShow = false;
                if (stagePanel.IsOpen) stagePanel.HidePanel();
            }

            LastSpawnIndex = forceSpawn >= 0 ? forceSpawn : PickSpawnIndex();

            // 从操作演练切过来时，玩家手上可能还举着 Extinguisher01：先放下并归位，
            // 否则它会被一起传送到出生点，正对镜头挡住设备火与烟雾（玩家视角实测）。
            ReleaseHeldExtinguishers();
            TeleportToSpawn(LastSpawnIndex);

            // 0) 操作演练的地板火收掉，避免正式阶段场上同时有两团火、两个血量条
            if (tutorialFire != null)
            {
                if (tutorialFire.IsBurning) tutorialFire.Extinguish();
                tutorialFire.gameObject.SetActive(false);
            }
            if (stagePanel != null && stagePanel.IsOpen) stagePanel.HidePanel();

            // 1) 烟雾：开始即出现
            if (stage1Smoke != null) stage1Smoke.Begin();

            // 2) 设备起火：锁定（未断电不能灭）
            if (deviceFire != null)
            {
                deviceFire.gameObject.SetActive(true);
                deviceFire.SetExtinguishLocked(requirePowerCutBeforeExtinguish);
                deviceFire.IgniteNow();
            }
            if (deviceSmoke != null)
            {
                deviceSmoke.Clear();
                deviceSmoke.Play();
            }

            // 3) 电源复位为「未切断」
            if (powerSwitch != null) powerSwitch.ResetSwitchForTest();

            if (arrows != null) arrows.Begin();
            if (countdown != null) countdown.Begin();

            string spawnText = SpawnCount > 0 ? "出生点 " + (LastSpawnIndex + 1) + "/" + SpawnCount : "出生点未配置";
            Hint("第 1 阶段 · 电气设备起火（" + spawnText + "）：先切断电源，再拿起灭火器答题、拔销、压把、喷射");
            Debug.Log("[Drill] 正式演练第 1 阶段开始，出生点 index=" + LastSpawnIndex
                      + "（共 " + SpawnCount + " 个候选），设备火已点燃，电源未切断。");
        }

        /// <summary>放下玩家手里的灭火器，并把操作演练用的那瓶放回原位。</summary>
        private void ReleaseHeldExtinguishers()
        {
            foreach (var e in FindObjectsByType<Extinguisher>(FindObjectsSortMode.None))
            {
                if (e == null || e.grab == null || !e.grab.IsHeld) continue;
                e.grab.ReleaseExternal();   // 桌面/探针的外部吸附
                e.grab.ForceRelease();      // 手柄实际抓取
                Debug.Log("[Drill] 已放下 " + e.name + "（切换到正式演练）");
            }

            if (tutorialExtinguisher != null)
            {
                var rb = tutorialExtinguisher.GetComponent<Rigidbody>();
                if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                tutorialExtinguisher.transform.position = tutorialExtinguisherHome;
                tutorialExtinguisher.transform.rotation = Quaternion.identity;
            }
        }

        /// <summary>随机挑一个出生点：候选 2~3 个，尽量不与上一次重复。</summary>
        public int PickSpawnIndex()
        {
            int n = SpawnCount;
            if (n <= 0) return -1;
            if (!randomSpawn) return 0;
            int idx = Random.Range(0, n);
            if (n > 1 && idx == LastSpawnIndex) idx = (idx + 1) % n;
            return idx;
        }

        /// <summary>把玩家（XR Origin）传送到指定出生点，朝向与出生点一致。</summary>
        public bool TeleportToSpawn(int index)
        {
            if (spawnPoints == null || spawnPoints.Length == 0) return false;
            int i = Mathf.Clamp(index, 0, spawnPoints.Length - 1);
            return TeleportTo(spawnPoints[i]);
        }

        public bool TeleportTo(Transform target)
        {
            Resolve();
            if (target == null || rigRoot == null) return false;

            var cc = rigRoot.GetComponent<CharacterController>();
            bool ccEnabled = cc != null && cc.enabled;
            if (ccEnabled) cc.enabled = false;

            // 只落 XZ，保持原有离地高度（XROrigin 根节点贴在 y=0 地面）
            float y = rigRoot.position.y;
            rigRoot.position = new Vector3(target.position.x, y, target.position.z);
            rigRoot.rotation = Quaternion.Euler(0f, target.eulerAngles.y, 0f);

            if (ccEnabled) cc.enabled = true;
            Physics.SyncTransforms();
            return true;
        }

        /// <summary>输入层调用：切断电源（桌面右键 / VR 扳机点电源箱）。</summary>
        public bool TryCutPower()
        {
            if (powerSwitch == null)
            {
                Hint("场景中没有电源接口");
                return false;
            }
            if (powerSwitch.IsCut)
            {
                Hint("电源已切断，可以直接灭火");
                return false;
            }

            Transform src = Camera.main != null ? Camera.main.transform : null;
            if (!powerSwitch.CutByInput(src))
            {
                Hint("离电源箱太远：走近到 4 米内再切断（右键单击 / VR 扳机点射）");
                return false;
            }
            return true;
        }

        private void OnPowerCut()
        {
            if (deviceFire != null) deviceFire.SetExtinguishLocked(false);
            Hint("电源已切断 ✓ 现在拿起灭火器：右键单击答题（VR 按 Grip）→ 拔销 → 压把 → 对准火焰根部喷射");
        }

        private void OnDeviceFireBlocked()
        {
            if (CurrentPhase != Phase.Stage1) return;
            Hint("火焰仍带电，喷射无效！先切断电源（就在设备旁边的电源箱）");
        }

        private void OnDeviceFireExtinguished()
        {
            if (CurrentPhase != Phase.Stage1) return;
            CurrentPhase = Phase.Stage2;

            if (deviceSmoke != null)
                deviceSmoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (stage1Smoke != null) stage1Smoke.Stop();

            // 交给第 2 阶段：安全标识 + 出口 → 门前火情 → 开门
            if (stageTwo != null) stageTwo.Begin();
            else if (arrows != null) arrows.BeginLobbyGuidance();

            Hint("设备火已扑灭！第 2 阶段：沿「安全出口」标识撤离，出口前有火情——灭掉它，门就会打开");
            Debug.Log("[Drill] 第 1 阶段完成（断电 + 灭火），进入第 2 阶段。");
        }

        // ================================================================
        // 状态 / 提示
        // ================================================================

        /// <summary>当前阶段给 HUD / 自检用的一句话状态。</summary>
        public string StateText()
        {
            switch (CurrentMode)
            {
                case Mode.None:
                    return "状态：等待选择「开始演练」或「操作演练」";
                case Mode.Tutorial:
                    return CurrentPhase == Phase.Done
                        ? "状态：操作演练完成（可进入正式演练）"
                        : "状态：操作演练 · 拿瓶 → 答题 → 拔销 → 压把 → 喷射";
                case Mode.Formal:
                    if (CurrentPhase == Phase.Stage1)
                    {
                        string power = PowerCut ? "电源已切断 ✓" : "电源未切断（先断电才能灭火）";
                        if (deviceFire != null)
                            power += " · 火焰 HP " + Mathf.RoundToInt((1f - deviceFire.progress01) * 100f) + "%";
                        return "状态：第 1 阶段 · 设备起火 → " + power;
                    }
                    if (CurrentPhase == Phase.Stage2)
                        return "状态：第 2 阶段 · 沿安全出口撤离 → 灭掉门前火 → 开门通关";
                    return "状态：演练完成";
                default:
                    return "";
            }
        }

        /// <summary>把提示同时推到桌面 HUD 与头显提示条。</summary>
        public void Hint(string message)
        {
            CurrentHint = message;
            var desktop = DesktopDrillInput.Instance;
            if (desktop != null) desktop.PushHint(message);
            if (hud != null) hud.SetHint(message);
            Debug.Log("[Drill] " + message);
        }

        private static string ModeName(Mode mode)
        {
            return mode == Mode.Tutorial ? "操作演练" : mode == Mode.Formal ? "正式演练" : "未开始";
        }
    }
}
