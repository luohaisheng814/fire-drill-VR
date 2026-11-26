using UnityEngine;
using UnityEngine.Events;

namespace FireSafety
{
    /// <summary>
    /// 第二阶段 / 第三阶段流程编排：
    ///   ① 阶段一完成面板点「继续往下」→ Begin()：开启烟雾 + 引导箭头指向大厅，玩家蹲下潜行（桌面按 C）；
    ///   ② 蹲姿到达大厅 → 进入第三阶段：大厅火源复燃，重新走一遍「拿起 → 拔销 → 压把 → 喷射」；
    ///   ③ 复燃火源扑灭 → 安全出口门 Unlock()，箭头改指出口；
    ///   ④ 玩家走进（相机进入 0.9 米）→ 结束页显示「演练完成 · 成功撤离」。
    /// 所有状态迁移都有公开方法，便于批处理自检直接驱动。
    /// </summary>
    public class StageTwoFlow : MonoBehaviour
    {
        public static StageTwoFlow Instance { get; private set; }

        public enum Stage
        {
            Idle = 0,
            Evacuating = 1,   // 穿烟雾撤离到大厅
            StageThree = 2,   // 大厅复燃，重复灭火流程
            ExitOpen = 3,     // 出口已开，走向出口
            Done = 4
        }

        [Header("引用（留空自动查找）")]
        public GuidanceArrows arrows;
        public SmokeZone smoke;
        public ExitDoor exitDoor;
        public DrillCountdown countdown;
        public EndingScreen endingScreen;
        [Tooltip("留空则用 Camera.main")]
        public Transform player;
        [Tooltip("大厅复燃火源（留空则运行时创建）")]
        public FireSource stageTwoFire;
        [Tooltip("大厅备用灭火器（留空自动把最近且未被拿起的灭火器挪到大厅）")]
        public Extinguisher spareExtinguisher;

        [Header("判定参数")]
        [Tooltip("相机世界高度低于该值视为蹲下（VR 里等同于真的蹲下）")]
        public float crouchHeight = 1.15f;
        [Tooltip("到达大厅的判定半径（米）")]
        public float lobbyEnterDistance = 1.2f;
        [Tooltip("出口门的进入判定半径（米）")]
        public float exitEnterDistance = 0.9f;
        [Tooltip("站立在浓烟中每累计多少秒提示一次")]
        public float smokeWarningInterval = 4f;

        [Header("复燃火源参数")]
        public Vector3 stageTwoFireOffset = new Vector3(0f, 0.25f, 0.75f);
        public float stageTwoExtinguishThreshold = 2.2f;
        [Tooltip("复燃火瞄准点高度。地面火必须抬到玩家持瓶高度附近，否则 20° 喷锥从 2 米外根本压不到 0.3 米的火根")]
        public float stageTwoAimHeight = 0.85f;
        [Tooltip("第三阶段灭火点位到火焰的水平距离（米）")]
        public float stageThreeStandDistance = 2.0f;
        [Tooltip("第三阶段灭火点位（留空则按门/火位置自动生成地面圆环）")]
        public Transform stageThreeStandPoint;
        public bool autoCreateFire = true;
        [Tooltip("第三阶段开始时，把最近的一台空闲灭火器挪到 spareExtinguisherSpot")]
        public bool autoMoveSpareExtinguisher = false;

        [Header("流程开关（本轮重构后的第 2 阶段）")]
        [Tooltip("第 2 阶段是否开浓烟：新流程只要安全标识 + 出口火情，默认关")]
        public bool useSmoke = false;
        [Tooltip("是否必须在浓烟中蹲下才能推进：新流程不强制潜行，默认关")]
        public bool requireCrouch = false;
        [Tooltip("第 2 阶段备用灭火器的题库组：0=A 1=B 2=C(电气) 3=D(疏散情景)")]
        [Range(0, 5)] public int spareQuizSetIndex = 3;
        [Tooltip("第 25 轮：跳过第 2 阶段（撤离到大厅），第一阶段的火一灭就直接在安全出口门前升起第三阶段的火。" +
                 "纸张 / 茶水间两套情景用「就近原则」直接扑向出口，只有电气情景保留完整的两段式撤离。")]
        public bool skipEvacuation = false;
        [Tooltip("头显提示条（留空自动查找）")]
        public VRDrillHud hud;

        [Header("事件")]
        public UnityEvent onStageTwoBegin = new UnityEvent();
        public UnityEvent onStageThreeBegin = new UnityEvent();
        public UnityEvent onExitOpened = new UnityEvent();
        public UnityEvent onCleared = new UnityEvent();

        public Stage CurrentStage { get; private set; } = Stage.Idle;
        public float SmokeExposureSeconds { get; private set; }
        public int SmokeWarnings { get; private set; }
        public string CurrentHint { get; private set; } = "";
        public bool DesktopCrouch { get; private set; }

        /// <summary>是否处于蹲下姿态：桌面切换 或 相机实际高度低于阈值。</summary>
        public bool IsCrouched
        {
            get
            {
                if (DesktopCrouch) return true;
                Camera cam = Camera.main;
                if (cam == null) return false;
                return cam.transform.position.y <= crouchHeight;
            }
        }

        private float _smokeTimer;
        private bool _warnedStandingAtLobby;
        private bool _crouchSaved;
        private float _crouchBaseHeight;
        private Transform _crouchTarget;      // CameraOffset（桌面蹲下时下移）

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            Resolve();
            SubscribeExit();
        }

        /// <summary>订阅出口进入事件（Start 再订一次，RemoveListener 保证不重复）。</summary>
        private void SubscribeExit()
        {
            if (exitDoor == null) return;
            exitDoor.onEntered.RemoveListener(OnExitDoorEntered);
            exitDoor.onEntered.AddListener(OnExitDoorEntered);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (stageTwoFire != null) stageTwoFire.onExtinguished.RemoveListener(OnStageTwoFireExtinguished);
        }

        private void Resolve()
        {
            if (arrows == null) arrows = FindObjectOfType<GuidanceArrows>();
            if (smoke == null) smoke = FindObjectOfType<SmokeZone>(true);
            if (exitDoor == null) exitDoor = FindObjectOfType<ExitDoor>(true);
            if (countdown == null) countdown = FindObjectOfType<DrillCountdown>();
            if (endingScreen == null) endingScreen = FindObjectOfType<EndingScreen>();
            if (hud == null) hud = FindObjectOfType<VRDrillHud>();
            if (player == null && Camera.main != null) player = Camera.main.transform;
        }

        private void Start()
        {
            Resolve();
            SubscribeExit();
        }

        private void Update()
        {
            if (CurrentStage != Stage.Evacuating) return;
            if (player == null) Resolve();

            // 浓烟中站立：累计暴露时间并周期性提示（蹲下即清零）
            // 新流程（useSmoke=false + requireCrouch=false）不统计，直接沿标识走
            if (useSmoke && !requireCrouch)
            {
                // 有烟但不强制蹲：只提示，不阻挡推进
                if (!IsCrouched)
                {
                    _smokeTimer += Time.deltaTime;
                    SmokeExposureSeconds += Time.deltaTime;
                    if (_smokeTimer >= Mathf.Max(1f, smokeWarningInterval))
                    {
                        _smokeTimer = 0f;
                        SmokeWarnings++;
                        Hint("浓烟警告：建议蹲低并沿地面箭头撤离（桌面按 C 蹲下）", true);
                    }
                }
                else _smokeTimer = 0f;
            }
            else if (useSmoke && requireCrouch)
            {
                if (!IsCrouched)
                {
                    _smokeTimer += Time.deltaTime;
                    SmokeExposureSeconds += Time.deltaTime;
                    if (_smokeTimer >= Mathf.Max(1f, smokeWarningInterval))
                    {
                        _smokeTimer = 0f;
                        SmokeWarnings++;
                        Hint("浓烟警告：请蹲下并沿地面箭头潜行（桌面按 C 蹲下）", true);
                    }
                }
                else _smokeTimer = 0f;
            }

            Transform lobby = arrows != null ? arrows.lobbyTarget : null;
            if (lobby == null) return;
            if (FlatDistanceTo(lobby.position) <= lobbyEnterDistance)
                TryArriveLobby(IsCrouched);
        }

        // ---------------- 流程 ----------------

        /// <summary>① 阶段一完成面板点「继续往下」时调用。</summary>
        public void Begin()
        {
            Resolve();
            CurrentStage = Stage.Evacuating;
            _warnedStandingAtLobby = false;
            SmokeExposureSeconds = 0f;
            SmokeWarnings = 0;
            _smokeTimer = 0f;

            if (smoke != null && useSmoke) smoke.Begin();

            // 第 25 轮：纸张 / 茶水间两套情景按「就近原则」跳过第 2 阶段——
            // 第一阶段的火一灭，直接把火升在安全出口门前，箭头改为直指出口门。
            if (skipEvacuation)
            {
                if (arrows != null) arrows.BeginExitGuidance();
                Hint("本情景就近撤离：直接前往「安全出口」门，门前已复燃——灭掉它就能开门", true);
                onStageTwoBegin?.Invoke();
                EnterStageThree();
                Debug.Log("[Stage2] 跳过第 2 阶段（就近原则）：直接进入第 3 阶段，火源在安全出口门前。");
                return;
            }

            if (arrows != null) arrows.BeginLobbyGuidance();

            if (requireCrouch)
                Hint("第二阶段：烟雾扩散，蹲下潜行（桌面按 C），沿安全出口箭头前往大厅", true);
            else
                Hint("第 2 阶段：沿「安全出口」标识与地面箭头前往出口，门前会升起火焰——灭掉它，门就会打开", true);
            onStageTwoBegin?.Invoke();
            Debug.Log("[Stage2] 第二阶段开始：安全出口标识 + 箭头（useSmoke=" + useSmoke + " / requireCrouch=" + requireCrouch + "）。");
        }

        /// <summary>到达大厅（crouched 由调用方给出，便于自检）。返回是否推进到第三阶段。</summary>
        public bool TryArriveLobby(bool crouched)
        {
            if (CurrentStage != Stage.Evacuating) return false;
            if (requireCrouch && !crouched)
            {
                if (!_warnedStandingAtLobby)
                {
                    _warnedStandingAtLobby = true;
                    Hint("你已到达大厅，但仍在浓烟中站立——请先蹲下（桌面按 C）再进入", true);
                }
                return false;
            }
            EnterStageThree();
            return true;
        }

        /// <summary>② 进入第三阶段：大厅复燃，重复第一阶段灭火流程。</summary>
        public void EnterStageThree()
        {
            if (CurrentStage == Stage.StageThree || CurrentStage == Stage.ExitOpen || CurrentStage == Stage.Done)
                return;
            CurrentStage = Stage.StageThree;
            _warnedStandingAtLobby = false;

            if (smoke != null) smoke.Stop();

            EnsureFire();
            ArmStageThreeFire();
            EnsureStageThreePoint();

            MoveSpareExtinguisherToLobby();

            Hint("出口前复燃！先站到地面青色圆环「灭火点位」，拿起出口旁的灭火器（需再答 5 题疏散情景题）→ 拔销 → 压把 → 对准门前火焰根部喷射", true);
            onStageThreeBegin?.Invoke();
            Debug.Log("[Stage2] 进入第三阶段：大厅复燃，重复灭火流程。");
        }

        /// <summary>
        /// 第三阶段火源每次进入都必须重新“武装”：
        /// 解锁、重挂熄灭事件、抬瞄准点、清零进度并重新点燃。
        /// 否则同一 Play 会话里重开演练时，上一轮已熄灭的 stageTwoFire 会被直接跳过，
        /// 表现为「门口火看着在、喷半天不灭、门也不开」。
        /// </summary>
        private void ArmStageThreeFire()
        {
            if (stageTwoFire == null) return;

            var fire = stageTwoFire;
            if (!fire.gameObject.activeSelf) fire.gameObject.SetActive(true);

            fire.SetExtinguishLocked(false);       // 出口火不属于电气火，不能被任何模板残留锁住
            fire.startUnlit = false;
            fire.extinguishThreshold = stageTwoExtinguishThreshold;

            if (fire.onExtinguished == null) fire.onExtinguished = new UnityEvent();
            fire.onExtinguished.RemoveListener(OnStageTwoFireExtinguished);
            fire.onExtinguished.AddListener(OnStageTwoFireExtinguished);

            // 低瞄准点是第 3 阶段喷不中的主因：0.3 米火根 + 20° 喷锥 + 2 米站位，
            // 喷嘴（约 1.2 米高）到瞄准点的俯角超过 22°，全程在锥外。
            Transform aim = fire.aimTarget;
            if (aim == null) aim = MakeAimPoint(fire.transform, stageTwoAimHeight);
            Vector3 local = aim.localPosition;
            if (local.y < stageTwoAimHeight)
            {
                local.y = stageTwoAimHeight;
                aim.localPosition = local;
            }
            fire.aimTarget = aim;

            fire.IgniteNow();                      // 清零 progress01 / IsExtinguished，重新开粒子与灯光

            foreach (var ps in fire.particles)
                if (ps != null && !ps.isPlaying) ps.Play();
            if (fire.fireLight != null) fire.fireLight.enabled = true;

            Debug.Log("[Stage2] 第三阶段火源已重新武装：锁定=" + fire.extinguishLocked
                      + " 瞄准高度=" + fire.AimPoint.y.ToString("F2") + " 米");
        }

        /// <summary>生成/刷新第三阶段的地面灭火点位（在火源靠房间一侧 2 米处）。</summary>
        private void EnsureStageThreePoint()
        {
            if (stageTwoFire == null) return;

            Vector3 firePos = stageTwoFire.transform.position;
            Vector3 standPos;
            if (stageThreeStandPoint != null)
            {
                standPos = stageThreeStandPoint.position;
            }
            else
            {
                Vector3 fromDoor = firePos - (exitDoor != null ? exitDoor.transform.position : transform.position);
                fromDoor.y = 0f;
                if (fromDoor.sqrMagnitude < 0.04f)
                {
                    Vector3 fwd = exitDoor != null ? exitDoor.transform.forward : Vector3.forward;
                    fromDoor = -fwd;
                    fromDoor.y = 0f;
                }
                standPos = firePos + fromDoor.normalized * Mathf.Max(1.2f, stageThreeStandDistance);
                standPos.y = 0.02f;

                var go = transform.Find("StageThreeExtinguishPoint");
                if (go == null)
                {
                    var pointGo = new GameObject("StageThreeExtinguishPoint");
                    pointGo.transform.SetParent(transform, false);
                    go = pointGo.transform;
                }
                stageThreeStandPoint = go;
                go.position = standPos;
            }

            var point = stageThreeStandPoint.GetComponent<ExtinguishPoint>();
            if (point == null) point = stageThreeStandPoint.gameObject.AddComponent<ExtinguishPoint>();
            point.fire = stageTwoFire;
            point.radius = 0.5f;
            point.BuildRing();
            stageThreeStandPoint.gameObject.SetActive(true);

            Debug.Log("[Stage2] 第三阶段灭火点位：" + stageThreeStandPoint.position.ToString("F2")
                      + "（距火 " + Vector3.Distance(stageThreeStandPoint.position, firePos).ToString("F2") + " 米）");
        }

        /// <summary>③ 复燃火源扑灭后解锁出口。</summary>
        public void OnStageTwoFireExtinguished()
        {
            if (CurrentStage != Stage.StageThree) return;
            CurrentStage = Stage.ExitOpen;
            if (exitDoor != null) exitDoor.Unlock();
            if (arrows != null) arrows.BeginExitGuidance();
            Hint("安全出口已开启：走进绿色「安全出口 EXIT」门内即通关", true);
            onExitOpened?.Invoke();
            Debug.Log("[Stage2] 复燃火源已扑灭，安全出口开启。");
        }

        /// <summary>④ 走进出口，通关成功。</summary>
        public bool TryEnterExit()
        {
            if (CurrentStage != Stage.ExitOpen) return false;
            if (exitDoor == null || !exitDoor.IsUnlocked) return false;
            if (exitDoor.PlayerEntered) return false;
            exitDoor.TryEnter();
            return true;
        }

        private void OnExitDoorEntered()
        {
            if (CurrentStage == Stage.Done) return;
            if (CurrentStage != Stage.ExitOpen)
            {
                // 门没解锁就走到门口：提示先灭火
                Hint("安全出口尚未开启——先扑灭门前的火焰", true);
                return;
            }
            Complete();
        }

        private void Complete()
        {
            CurrentStage = Stage.Done;
            if (countdown != null) countdown.Pause();
            if (arrows != null) arrows.StopGuidance();
            if (smoke != null) smoke.Stop();
            Hint("演练完成：成功撤离！", true);
            if (endingScreen != null) endingScreen.Show(true);
            onCleared?.Invoke();
            Debug.Log("[Stage2] 通关成功，已弹出结束页。");
        }

        // ---------------- 桌面蹲下 ----------------

        /// <summary>桌面模式蹲下切换：把 CameraOffset 下移，让相机高度落到蹲姿判定以下。</summary>
        public void SetDesktopCrouch(bool crouch)
        {
            if (crouch == DesktopCrouch) return;
            ResolveCrouchTarget();
            DesktopCrouch = crouch;

            if (_crouchTarget != null)
            {
                if (!_crouchSaved)
                {
                    _crouchBaseHeight = _crouchTarget.localPosition.y;
                    _crouchSaved = true;
                }
                var lp = _crouchTarget.localPosition;
                float target = crouch ? _crouchBaseHeight - 0.62f : _crouchBaseHeight;
                _crouchTarget.localPosition = new Vector3(lp.x, target, lp.z);
            }
            Hint(crouch ? "已切换为蹲姿（潜行）" : "已恢复站立", false);
        }

        public void ToggleDesktopCrouch() => SetDesktopCrouch(!DesktopCrouch);

        /// <summary>自检用：绕过相机高度直接指定蹲姿。</summary>
        public void SetCrouchOverrideForTest(bool crouched) => DesktopCrouch = crouched;

        private void ResolveCrouchTarget()
        {
            if (_crouchTarget != null) return;
            var offset = GameObject.Find("Camera Offset");
            if (offset == null) offset = GameObject.Find("CameraOffset");
            if (offset != null) { _crouchTarget = offset.transform; return; }
            Camera cam = Camera.main;
            if (cam != null && cam.transform.parent != null) _crouchTarget = cam.transform.parent;
        }

        // ---------------- 复燃火源 ----------------

        private void EnsureFire()
        {
            if (stageTwoFire != null) return;
            if (!autoCreateFire) return;

            Vector3 pos = FirePosition();

            // ① 优先复用工程里现成的火焰（BaseFire000）：外观与第一阶段完全一致。
            //    运行时手搓的粒子在 URP 下容易退化成橙色硬边方块，门口的「火焰」看着像色块。
            GameObject template = FindSceneFireTemplate();
            if (template != null)
            {
                var clone = Instantiate(template);
                clone.name = "StageTwoFire";
                clone.SetActive(true);
                clone.transform.SetParent(transform, true);
                clone.transform.position = pos;
                clone.transform.rotation = Quaternion.identity;

                // 模板自带一个 FireSource：必须在删任何东西之前先抓住它。
                // 播放态 Destroy 是延迟到帧末才生效的，若先删再 GetComponent，会拿到「即将销毁」的那个，
                // 于是不会补建新组件，下一帧它变成空引用 —— 火看着还在烧，但灭火进度再也涨不了、门永远不开。
                var cloneFire = clone.GetComponent<FireSource>();
                if (cloneFire == null) cloneFire = clone.AddComponent<FireSource>();

                // 只清掉多余的火源（模板挂在子物体上的那些），保留上面这个
                foreach (var old in clone.GetComponentsInChildren<FireSource>(true))
                    if (old != cloneFire) KillObject(old);

                // 模板的巨包围盒碰撞体（包住整间屋子）会把带 Rigidbody 的灭火器顶飞，火是纯视觉，直接去掉
                foreach (var c in clone.GetComponentsInChildren<Collider>(true))
                {
                    c.enabled = false;
                    KillObject(c);
                }

                var cloneParts = clone.GetComponentsInChildren<ParticleSystem>(true);
                var cloneLight = clone.GetComponentInChildren<Light>(true);
                if (cloneLight == null) cloneLight = AddFireLight(clone.transform);
                else
                {
                    cloneLight.enabled = true;
                    cloneLight.type = LightType.Point;
                    cloneLight.color = new Color(1f, 0.55f, 0.2f);
                    cloneLight.range = 5f;
                    cloneLight.intensity = 2.4f;
                }

                cloneFire.particles = cloneParts;
                cloneFire.fireLight = cloneLight;
                cloneFire.extinguishThreshold = stageTwoExtinguishThreshold;
                // 模板在第一阶段可能已被扑灭：克隆会把 progress01 一起复制过来（=1），
                // 新火一被碰就立刻判定熄灭，门瞬间就开。这里必须清零并点燃粒子。
                cloneFire.progress01 = 0f;
                if (cloneLight != null) cloneLight.enabled = true;
                if (cloneFire.onExtinguished == null) cloneFire.onExtinguished = new UnityEvent();
                cloneFire.onExtinguished.RemoveAllListeners();   // 克隆会连事件表一起复制，先清掉模板留下的监听
                cloneFire.onExtinguished.AddListener(OnStageTwoFireExtinguished);
                cloneFire.aimTarget = MakeAimPoint(clone.transform, stageTwoAimHeight);
                EnsureHealthBar(cloneFire);
                // 模板本轮已改为「未点燃」外观（startUnlit=true），克隆会继承 IsBurning=false；
                // 这里必须显式点燃，否则喷射会被 ApplySprayToFires 跳过 —— 火在烧但进度永远 0。
                cloneFire.startUnlit = false;
                cloneFire.IgniteNow();
                stageTwoFire = cloneFire;
                Debug.Log("[Stage2] 复燃火源复用场景火焰模板 " + template.name + "（" + cloneParts.Length + " 个粒子系统）");
                return;
            }

            // ② 兜底：模板缺失时才新建粒子系统
            var go = new GameObject("StageTwoFire");
            go.transform.SetParent(transform, false);
            go.transform.position = pos;

            var ps = go.AddComponent<ParticleSystem>();
            // 新建的粒子系统先 Stop 一次，避免编辑模式下访问模块抛
            // "Do not create your own module instances" 空引用
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.maxParticles = 90;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;
            main.startColor = new Color(1f, 0.62f, 0.16f, 0.95f);

            var em = ps.emission;
            em.rateOverTime = 55f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.16f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.88f, 0.42f), 0f),
                    new GradientColorKey(new Color(0.95f, 0.35f, 0.08f), 0.6f),
                    new GradientColorKey(new Color(0.35f, 0.1f, 0.05f), 1f)
                },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.1f));

            var mat = SmokeZone.CreateParticleMaterial(SmokeZone.CreateSoftTexture(), true);
            var rend = go.GetComponent<ParticleSystemRenderer>();
            if (rend != null) rend.material = mat;

            var lightGo = new GameObject("FireLight");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            var lt = lightGo.AddComponent<Light>();
            lt.type = LightType.Point;
            lt.color = new Color(1f, 0.55f, 0.2f);
            lt.range = 5f;
            lt.intensity = 2.2f;

            var fire = go.AddComponent<FireSource>();
            fire.particles = new[] { ps };
            fire.fireLight = lt;
            fire.extinguishThreshold = stageTwoExtinguishThreshold;
            if (fire.onExtinguished == null) fire.onExtinguished = new UnityEvent();
            fire.onExtinguished.AddListener(OnStageTwoFireExtinguished);
            fire.aimTarget = MakeAimPoint(go.transform, stageTwoAimHeight);
            EnsureHealthBar(fire);

            stageTwoFire = fire;
        }

        /// <summary>出口阶段的火焰也挂血量条：玩家能看到「HP 100 → 0」的灭火进度。</summary>
        private static void EnsureHealthBar(FireSource fire)
        {
            if (fire == null) return;
            var bar = fire.GetComponent<FireHealthBar>();
            if (bar == null) bar = fire.gameObject.AddComponent<FireHealthBar>();
            bar.fire = fire;
            bar.RefreshNow();   // 新建的条当帧 Start 还没跑，先刷成 HP 100，免得玩家第一眼看到 0/100
        }

        /// <summary>
        /// 复燃火的瞄准点：火焰根部（离地约 0.32 米），不是火源物体的地面原点。
        /// 玩家是「对着看得见的火焰」喷的，瞄准点若压在 y=0，喷向火焰中上部就会擦着 20° 锥边丢判定。
        /// </summary>
        private static Transform MakeAimPoint(Transform fireRoot)
        {
            return MakeAimPoint(fireRoot, 0.32f);
        }

        private static Transform MakeAimPoint(Transform fireRoot, float localHeight)
        {
            Transform t = fireRoot.Find("AimPoint");
            if (t == null)
            {
                var go = new GameObject("AimPoint");
                go.transform.SetParent(fireRoot, false);
                go.transform.localPosition = new Vector3(0f, localHeight, 0f);
                t = go.transform;
            }
            return t;
        }

        /// <summary>在场景里找一簇现成的火焰当模板（BaseFire000 是 Office Set 导入后自带的火）。</summary>
        private GameObject FindSceneFireTemplate()
        {
            foreach (var f in FindObjectsOfType<FireSource>(true))
            {
                if (f == null || f.gameObject == gameObject) continue;
                if (f.gameObject.name.Contains("BaseFire")) return f.gameObject;
            }
            return GameObject.Find("BaseFire000");
        }

        private static Light AddFireLight(Transform parent)
        {
            var lightGo = new GameObject("FireLight");
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            var lt = lightGo.AddComponent<Light>();
            lt.type = LightType.Point;
            lt.color = new Color(1f, 0.55f, 0.2f);
            lt.range = 5f;
            lt.intensity = 2.2f;
            return lt;
        }

        private static void KillObject(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }

        /// <summary>
        /// 复燃火源位置：安全出口门正前方（朝玩家一侧），而不是大厅中心。
        /// 偏移量按「出口门的本地坐标」解释：门朝哪边开，火就落在哪边，换门后不必手改 x/z。
        /// </summary>
        private Vector3 FirePosition()
        {
            Transform anchor = null;
            if (exitDoor != null) anchor = exitDoor.transform;
            else if (arrows != null && arrows.exitTarget != null) anchor = arrows.exitTarget;
            else if (arrows != null) anchor = arrows.lobbyTarget;

            Vector3 basePos = anchor != null ? anchor.position : transform.position;
            if (exitDoor != null && exitDoor.transform != null)
                return exitDoor.transform.TransformPoint(new Vector3(stageTwoFireOffset.x, 0f, stageTwoFireOffset.z));
            return new Vector3(basePos.x, 0f, basePos.z) + stageTwoFireOffset;
        }

        // 第三阶段：备用灭火器的可选搬移落点（autoMoveSpareExtinguisher 关闭时不使用）
        public Vector3 spareExtinguisherSpot = new Vector3(1.70f, 0f, 0.52f);

        private void MoveSpareExtinguisherToLobby()
        {
            // 第39轮根因：茶水间情景 skipEvacuation=true，第一阶段的火一灭就会立刻 Begin→EnterStageThree；
            // 此时 Extinguisher03 还在玩家手里（自动放下要等 0.9 秒），旧逻辑看到 spareExtinguisher 被手持
            // 就直接 return，出口旁自然一台瓶都没有。现在改成：
            //   1) 优先用 spareExtinguisher（空闲时）；
            //   2) 它被手持时，改选现场另一台空闲瓶搬过去；
            //   3) 实在没有空闲瓶，再强制放开 spareExtinguisher 并搬它，保证出口一定有瓶。
            Extinguisher best = null;
            bool spareHeld = spareExtinguisher != null
                             && spareExtinguisher.grab != null
                             && spareExtinguisher.grab.IsHeld;

            if (spareExtinguisher != null && !spareHeld)
            {
                best = spareExtinguisher;
            }
            else
            {
                float bestDist = float.MaxValue;
                foreach (var ext in FindObjectsOfType<Extinguisher>(true))
                {
                    if (ext == null || !ext.gameObject.activeInHierarchy) continue;
                    if (ext.grab != null && ext.grab.IsHeld) continue;
                    float d = Vector3.Distance(ext.transform.position, spareExtinguisherSpot);
                    if (d < bestDist) { bestDist = d; best = ext; }
                }

                if (best == null && spareExtinguisher != null)
                {
                    // 三台瓶都在手上/异常状态：至少把出口专用瓶夺回来，不能让玩家走到门口无瓶可用。
                    best = spareExtinguisher;
                    if (best.grab != null && best.grab.IsHeld)
                    {
                        if (best.IsSpraying) best.StopSprayExternal();
                        best.grab.ReleaseExternal();
                        best.grab.ForceRelease();
                        Debug.Log("[Stage2] 备用灭火器仍被手持，已强制放回并搬到出口：" + best.name);
                    }
                }
            }

            if (best == null)
            {
                Debug.LogError("[Stage2] 找不到可搬到出口的灭火器，第三阶段将无瓶可用。");
                return;
            }

            // 第 2 阶段用独立题库（默认 D 组：出口 / 疏散情景），必须在重置问答前切组
            if (best.quizPanel != null) best.quizPanel.questionSetIndex = spareQuizSetIndex;

            // 默认（autoMoveSpareExtinguisher = false）不动这台灭火器：它运到第三阶段就该待在原地。
            // 只有显式打开开关时才搬移，并按「渲染包围盒」落位，避免横躺或插进家具。
            if (autoMoveSpareExtinguisher)
            {
                best.EnsureUprightStanding(false);
                Vector3 target = new Vector3(spareExtinguisherSpot.x, 0f, spareExtinguisherSpot.z);
                var rend = best.GetComponentInChildren<Renderer>();
                if (rend != null)
                {
                    Bounds b = rend.bounds;
                    Vector3 delta = new Vector3(target.x - b.center.x, -b.min.y, target.z - b.center.z);
                    best.transform.position += delta;
                }
                else
                {
                    best.transform.position = target;
                }
                best.EnsureUprightStanding(true);   // 落位后再确认一次竖立 + 贴地

                // 第 39 轮：落位后立刻冻住刚体。否则物理会在随后几帧把瓶子推倒，
                // 玩家到出口看到的是「躺在地上的灭火器」，等同于「出口没有灭火器」。
                var rb = best.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.useGravity = false;
                    rb.isKinematic = true;
                }
                Debug.Log("[Stage2] 备用灭火器已搬到落点（" + best.name + "），问答已换成 D 组 5 题："
                          + "spareHeld=" + spareHeld + "，落点=" + spareExtinguisherSpot.ToString("F2"));
            }
            else
            {
                // 不搬也要保证站得住：原地只纠正姿态，不碰位置
                best.EnsureUprightStanding(false);
                Debug.Log("[Stage2] 备用灭火器保持原地（不搬移），问答已换成 D 组 5 题：" + best.name);
            }

            best.ResetQuizForStageTwo();   // 重新拾取都要再答一遍（默认 D 组疏散情景题）
        }

        // ---------------- 工具 ----------------

        private float FlatDistanceTo(Vector3 worldPos)
        {
            Transform p = player != null ? player : (Camera.main != null ? Camera.main.transform : null);
            if (p == null) return float.MaxValue;
            Vector3 a = new Vector3(p.position.x, 0f, p.position.z);
            Vector3 b = new Vector3(worldPos.x, 0f, worldPos.z);
            return Vector3.Distance(a, b);
        }

        private void Hint(string msg, bool force)
        {
            CurrentHint = msg;
            Debug.Log("[Stage2] " + msg);
            var desktop = DesktopDrillInput.Instance;
            if (desktop != null) desktop.PushHint(msg);
            if (hud == null) hud = FindObjectOfType<VRDrillHud>();
            if (hud != null) hud.SetHint(msg);
        }
    }
}
