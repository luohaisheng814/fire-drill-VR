using UnityEngine;
using UnityEngine.Events;

namespace FireSafety
{
    /// <summary>
    /// 演练总流程编排（本轮重构后的两段式流程）：
    ///   操作演练（原第一阶段）：拿灭火器 → 5 题问答 → 拔销 → 压把 → 喷射灭火，只为熟悉流程；
    ///   正式第 1 阶段：随机出生（2~3 个候选点）→ 立刻出现烟雾 + 设备起火 → 必须先切断电源 → 拿灭火器答题 → 灭火；
    ///   正式第 2 阶段：安全标识 / 箭头引导到出口 → 出口前升起火焰 → 情景问答 → 灭火 → 开门通关。
    /// 教学定位：操作演练属于「技能操作型情景教学」（procedural skill drill）——
    ///   以真实灭火器的取用、问答门控、拔销、压把、对准火焰根部喷射等操作序列为核心，
    ///   通过步骤门控与即时纠错让学习者在情境中形成程序性技能，而不是知识讲授或自由探索。
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

        /// <summary>
        /// 一套「起火情景」：起火点 + 冒烟 + 配套灭火器（含题库组）+ 该情景的出生点。
        /// 三套情景共用同一个终点（安全出口），只是起火位置、用到的灭火器和出生点不同。
        /// </summary>
        [System.Serializable]
        public class FireScenario
        {
            public string title = "电气设备起火";
            [Tooltip("HUD / 日志里的短名，如「电气」「纸张」「茶水间」")]
            public string shortName = "电气";
            [TextArea(1, 4)] public string intro = "";
            public FireSource fire;
            [Tooltip("起火点冒烟粒子（开始即播放）")]
            public ParticleSystem smoke;
            [Tooltip("该情景的房间烟雾区")]
            public SmokeZone roomSmoke;
            [Tooltip("该情景用的灭火器（离起火点最近的那台）")]
            public Extinguisher extinguisher;
            [Tooltip("灭火器原位（切换情景时归位，避免被上一情景举着带走）")]
            public Vector3 extinguisherHome;
            [Tooltip("该情景的题库组：0=A 1=B 2=C 电气 3=D 疏散 4=E 纸张 5=F 小家电/油类")]
            public int quizSetIndex = 2;
            [Tooltip("只有电气火灾需要先切断电源")]
            public PowerSwitch powerSwitch;
            public bool requirePowerCut;
            [Tooltip("该情景的出生点（2~3 个候选）")]
            public Transform[] spawnPoints = new Transform[0];
            [Tooltip("第 25 轮：该情景是否跳过第 2 阶段（撤离大厅）——纸张 / 茶水间就近直奔安全出口门")]
            public bool skipEvacuation = false;
            [Tooltip("第 28 轮：本情景灭火器的灭火剂类型（电气=CO₂ / 纸张=ABC 干粉 / 小家电=水基）")]
            public ExtinguisherKind extinguisherKind = ExtinguisherKind.PowderABC;
            [Tooltip("起火点旁的灭火点位：玩家站进地面青色圆环再对准火焰根部喷射")]
            public Transform extinguishPoint;
        }

        [Header("起火情景（不同情景 = 不同起火点 + 不同灭火器 + 不同出生点，终点相同）")]
        public FireScenario[] scenarios = new FireScenario[0];
        [Tooltip("开始演练时是否随机挑一套情景（关闭则用第 1 套，自检用）")]
        public bool randomScenario = true;

        public int ScenarioCount => scenarios != null ? scenarios.Length : 0;
        public int CurrentScenarioIndex { get; private set; } = -1;
        public FireScenario CurrentScenario =>
            (scenarios != null && CurrentScenarioIndex >= 0 && CurrentScenarioIndex < scenarios.Length)
                ? scenarios[CurrentScenarioIndex] : null;
        public string ScenarioTitle => CurrentScenario != null ? CurrentScenario.title : "";

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
        [Tooltip("出口阶段用的灭火器（离安全出口最近的那台，第 2 阶段会移动到门前）")]
        public Extinguisher exitExtinguisher;
        [Tooltip("场上全部灭火器（三台；第 2 阶段从里面找离出口最近的一台当备用瓶）")]
        public Extinguisher[] allExtinguishers = new Extinguisher[0];

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

            // 操作演练固定用 A 组（基础 5 题）：此时还没有进入任何情景，
            // 而 Extinguisher01 的默认题库组是 C 组（电气），演练时要先切回基础题。
            if (tutorialExtinguisher != null) tutorialExtinguisher.SetQuizSet(0, true);

            // 正式情景的火源一律先收掉，避免操作演练场上同时有两团火
            foreach (var s in scenarios)
            {
                if (s == null || s.fire == null) continue;
                if (s.fire.IsBurning) s.fire.Extinguish();
                s.fire.gameObject.SetActive(false);
                if (s.smoke != null) s.smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (s.roomSmoke != null) s.roomSmoke.Clear();
            }

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
        public void StartFormal(int forceSpawn = -1) => StartFormal(forceSpawn, -1);

        /// <summary>
        /// 正式演练入口。
        /// forceScenario &gt;= 0 时固定情景（自检 / 探针逐套验收用），否则按 randomScenario 随机挑一套。
        /// </summary>
        public void StartFormal(int forceSpawn, int forceScenario)
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

            // 第 27 轮：重开演练时把情景二/三那扇房间门关回去（上一次演练可能已经把它打开了）
            if (RoomDoor.Instance == null) RoomDoor.Instance = FindObjectOfType<RoomDoor>();
            if (RoomDoor.Instance != null) RoomDoor.Instance.ResetDoor();

            // 从操作演练的完成面板进入时，先把面板收掉
            if (stagePanel != null)
            {
                stagePanel.autoShow = false;
                if (stagePanel.IsOpen) stagePanel.HidePanel();
            }

            // 1) 选情景：不同情景 = 不同起火点 + 不同灭火器 + 不同出生点（终点相同）
            int scenarioIndex = forceScenario >= 0 ? forceScenario : PickScenarioIndex();
            ApplyScenario(scenarioIndex);

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

            // 2) 起火：电气火灾锁定（未断电不能灭），其它情景直接可灭
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

            // 3) 电源复位为「未切断」（只有电气情景有电源箱）
            if (powerSwitch != null) powerSwitch.ResetSwitchForTest();

            if (arrows != null) arrows.Begin();
            if (countdown != null) countdown.Begin();

            string spawnText = SpawnCount > 0 ? "出生点 " + (LastSpawnIndex + 1) + "/" + SpawnCount : "出生点未配置";
            string title = CurrentScenario != null ? CurrentScenario.title : "设备起火";
            Hint("第 1 阶段 · " + title + "（" + spawnText + "）：" + ScenarioStepHint());
            Debug.Log("[Drill] 正式演练第 1 阶段开始，情景 index=" + CurrentScenarioIndex
                      + "（" + title + "），出生点 index=" + LastSpawnIndex
                      + "（共 " + SpawnCount + " 个候选），起火点="
                      + (deviceFire != null ? deviceFire.transform.position.ToString("F2") : "null")
                      + "，电源锁定=" + requirePowerCutBeforeExtinguish);
        }

        /// <summary>当前情景的操作提示（电气要断电，其它情景直接取瓶答题灭火）。</summary>
        private string ScenarioStepHint()
        {
            if (requirePowerCutBeforeExtinguish)
                return "先切断电源，再站到起火点旁的地面青色圆环「灭火点位」，拿起灭火器答题、拔销、压把、对准火焰根部喷射";
            return "站到起火点旁的地面青色圆环「灭火点位」，拿起灭火器答题、拔销、压把、对准火焰根部喷射（本情景不需要断电）";
        }

        /// <summary>随机挑一套情景：尽量不与上一次重复。</summary>
        public int PickScenarioIndex()
        {
            int n = ScenarioCount;
            if (n <= 0) return -1;
            if (!randomScenario) return 0;
            int idx = Random.Range(0, n);
            if (n > 1 && idx == CurrentScenarioIndex) idx = (idx + 1) % n;
            return idx;
        }

        /// <summary>
        /// 把指定情景变成「当前情景」：其余情景的火焰熄灭并停烟，
        /// 同时把 deviceFire / deviceSmoke / stage1Smoke / deviceExtinguisher / powerSwitch / spawnPoints
        /// 这些「当前生效」字段指向本情景，后续所有流程代码无需分支。
        /// </summary>
        public bool ApplyScenario(int index)
        {
            if (scenarios == null || scenarios.Length == 0) return false;
            int i = Mathf.Clamp(index, 0, scenarios.Length - 1);
            var sc = scenarios[i];
            if (sc == null) return false;

            CurrentScenarioIndex = i;

            // 其余情景收火、停烟（避免场上同时两团火 / 两个血量条）
            for (int k = 0; k < scenarios.Length; k++)
            {
                var other = scenarios[k];
                if (other == null || k == i) continue;
                if (other.fire != null)
                {
                    other.fire.onExtinguished.RemoveListener(OnDeviceFireExtinguished);
                    other.fire.onExtinguishBlocked.RemoveListener(OnDeviceFireBlocked);
                    other.fire.gameObject.SetActive(false);
                }
                if (other.smoke != null) other.smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                if (other.roomSmoke != null) other.roomSmoke.Clear();
                if (other.extinguishPoint != null) other.extinguishPoint.gameObject.SetActive(false);
            }

            // 本情景生效
            deviceFire = sc.fire;
            deviceSmoke = sc.smoke;
            stage1Smoke = sc.roomSmoke;
            deviceExtinguisher = sc.extinguisher;
            powerSwitch = sc.powerSwitch;
            requirePowerCutBeforeExtinguish = sc.requirePowerCut;
            spawnPoints = sc.spawnPoints;

            // 第 25 轮：本情景是否跳过第 2 阶段（就近原则直达安全出口门）
            if (stageTwo == null) stageTwo = FindObjectOfType<StageTwoFlow>();
            if (stageTwo != null)
            {
                stageTwo.skipEvacuation = sc.skipEvacuation;
                if (stageTwo.stageThreeStandPoint != null)
                    stageTwo.stageThreeStandPoint.gameObject.SetActive(false);
            }

            if (sc.extinguishPoint != null)
            {
                sc.extinguishPoint.gameObject.SetActive(true);
                var point = sc.extinguishPoint.GetComponent<ExtinguishPoint>();
                if (point != null)
                {
                    point.fire = sc.fire;
                    point.BuildRing();
                }
            }

            if (deviceFire != null)
            {
                deviceFire.onExtinguished.RemoveListener(OnDeviceFireExtinguished);
                deviceFire.onExtinguished.AddListener(OnDeviceFireExtinguished);
                deviceFire.onExtinguishBlocked.RemoveListener(OnDeviceFireBlocked);
                deviceFire.onExtinguishBlocked.AddListener(OnDeviceFireBlocked);
            }

            // 本情景电源箱：只有电气情景有，且需要重新订阅（切换情景时电源箱可能不同）
            if (powerSwitch != null)
            {
                powerSwitch.onCut.RemoveListener(OnPowerCut);
                powerSwitch.onCut.AddListener(OnPowerCut);
            }

            // 本情景灭火器的题库组 + 答题状态重置（换情景必须重新答题）
            if (deviceExtinguisher != null)
            {
                deviceExtinguisher.SetQuizSet(sc.quizSetIndex, true);
                // 第 28 轮：同一瓶灭火器切到本情景的灭火剂类型（喷射形态 / 颜色 / 音效随之变化）
                deviceExtinguisher.SetKind(sc.extinguisherKind);
            }

            Debug.Log("[Drill] 情景切换到 " + sc.title + "（" + sc.shortName + "）：起火点="
                      + (sc.fire != null ? sc.fire.transform.position.ToString("F2") : "null")
                      + "，灭火器=" + (sc.extinguisher != null ? sc.extinguisher.name : "null")
                      + "，题库组=" + sc.quizSetIndex
                      + "，出生点=" + (sc.spawnPoints != null ? sc.spawnPoints.Length : 0) + " 个"
                      + "，需断电=" + sc.requirePowerCut);
            return true;
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
            if (stageTwo != null)
            {
                // 第 2 阶段的备用灭火器：从场上三台里挑离安全出口最近的一台，题库沿用 D 组（疏散情景）
                var spare = NearestExtinguisherToExit();
                if (spare != null)
                {
                    stageTwo.spareExtinguisher = spare;
                    spare.SetQuizSet(3, true);
                }
                stageTwo.Begin();
            }
            else if (arrows != null) arrows.BeginLobbyGuidance();

            Hint("「" + ScenarioTitle + "」已扑灭！第 2 阶段：沿「安全出口」标识撤离，出口前有火情——灭掉它，门就会打开");
            Debug.Log("[Drill] 第 1 阶段完成（情景=" + ScenarioTitle + "），进入第 2 阶段。");
        }

        /// <summary>离安全出口最近的一台灭火器（第 2 阶段备用瓶）。</summary>
        public Extinguisher NearestExtinguisherToExit()
        {
            if (exitExtinguisher != null) return exitExtinguisher;
            Vector3 exit = stageTwo != null && stageTwo.exitDoor != null
                ? stageTwo.exitDoor.transform.position : new Vector3(5.25f, 0f, 1.02f);
            Extinguisher best = null;
            float bestD = float.MaxValue;
            if (allExtinguishers != null)
            {
                foreach (var e in allExtinguishers)
                {
                    if (e == null) continue;
                    float d = new Vector3(e.transform.position.x - exit.x, 0f, e.transform.position.z - exit.z).magnitude;
                    if (d < bestD) { bestD = d; best = e; }
                }
            }
            if (best == null) best = exitExtinguisher;
            return best;
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
                {
                    // 第 46 轮：状态行前面挂当前课程阶段与本次随机身份（Pre-briefing → … → Debriefing）
                    var flow = DrillFlowDirector.Instance;
                    string prefix = flow != null ? ("【" + flow.StageName + " · " + flow.RoleName + "】 ") : "";
                    if (CurrentPhase == Phase.Stage1)
                    {
                        string title = CurrentScenario != null ? CurrentScenario.title : "设备起火";
                        string power = requirePowerCutBeforeExtinguish
                            ? (PowerCut ? "电源已切断 ✓" : "电源未切断（先断电才能灭火）")
                            : "本情景无需断电，直接灭火";
                        string hp = "";
                        if (deviceFire != null)
                            hp = " · 火焰剩余 " + Mathf.RoundToInt((1f - deviceFire.progress01) * 100f) + "%";
                        string ext = deviceExtinguisher != null
                            ? " · 灭火器：" + deviceExtinguisher.KindLabel + "（适用 " + deviceExtinguisher.SuitableLabel + "）"
                            : "";
                        return "状态：" + prefix + "第 1 阶段 · " + title + " → " + power + hp + ext;
                    }
                    if (CurrentPhase == Phase.Stage2)
                        return "状态：" + prefix + "第 2 阶段 · 沿安全出口撤离 → 灭掉门前火 → 开门通关";
                    return "状态：" + prefix + "演练完成";
                }
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
