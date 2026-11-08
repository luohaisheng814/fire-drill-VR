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
        public bool autoCreateFire = true;
        [Tooltip("第三阶段开始时，把最近的一台空闲灭火器挪到大厅备用")]
        public bool autoMoveSpareExtinguisher = true;

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

            if (smoke != null) smoke.Begin();
            if (arrows != null) arrows.BeginLobbyGuidance();

            Hint("第二阶段：烟雾扩散，蹲下潜行（桌面按 C），沿安全出口箭头前往大厅", true);
            onStageTwoBegin?.Invoke();
            Debug.Log("[Stage2] 第二阶段开始：烟雾 + 安全出口箭头。");
        }

        /// <summary>到达大厅（crouched 由调用方给出，便于自检）。返回是否推进到第三阶段。</summary>
        public bool TryArriveLobby(bool crouched)
        {
            if (CurrentStage != Stage.Evacuating) return false;
            if (!crouched)
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
            if (stageTwoFire != null && !stageTwoFire.IsExtinguished)
            {
                var fireGo = stageTwoFire.gameObject;
                if (!fireGo.activeSelf) fireGo.SetActive(true);
                foreach (var ps in stageTwoFire.particles)
                    if (ps != null && !ps.isPlaying) ps.Play();
                if (stageTwoFire.fireLight != null) stageTwoFire.fireLight.enabled = true;
            }

            MoveSpareExtinguisherToLobby();

            Hint("大厅复燃！放下手上灭火器，拿起大厅备用灭火器（需再答 5 题）→ 拔销 → 压把 → 对准门前火焰喷射", true);
            onStageThreeBegin?.Invoke();
            Debug.Log("[Stage2] 进入第三阶段：大厅复燃，重复灭火流程。");
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
                Hint("安全出口尚未开启——先扑灭大厅复燃的火源", true);
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

                // 模板自带一个 FireSource，先清掉，避免两个火源同时登记进 Active
                foreach (var old in clone.GetComponentsInChildren<FireSource>(true)) KillObject(old);

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

                var cloneFire = clone.GetComponent<FireSource>();
                if (cloneFire == null) cloneFire = clone.AddComponent<FireSource>();
                cloneFire.particles = cloneParts;
                cloneFire.fireLight = cloneLight;
                cloneFire.extinguishThreshold = stageTwoExtinguishThreshold;
                if (cloneFire.onExtinguished == null) cloneFire.onExtinguished = new UnityEvent();
                cloneFire.onExtinguished.AddListener(OnStageTwoFireExtinguished);
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

            stageTwoFire = fire;
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

        /// <summary>复燃火源位置：安全出口门正前方（朝玩家一侧），而不是大厅中心。</summary>
        private Vector3 FirePosition()
        {
            Transform anchor = null;
            if (exitDoor != null) anchor = exitDoor.transform;
            else if (arrows != null && arrows.exitTarget != null) anchor = arrows.exitTarget;
            else if (arrows != null) anchor = arrows.lobbyTarget;

            Vector3 basePos = anchor != null ? anchor.position : transform.position;
            return new Vector3(basePos.x, 0f, basePos.z) + stageTwoFireOffset;
        }

        // 第三阶段：备用灭火器摆到图 3 的门口位置（瓶身中心目标世界坐标，y 自动贴地）
        public Vector3 spareExtinguisherSpot = new Vector3(3.15f, 0f, 1.05f);

        private void MoveSpareExtinguisherToLobby()
        {
            if (!autoMoveSpareExtinguisher) return;

            if (spareExtinguisher != null && spareExtinguisher.grab != null && spareExtinguisher.grab.IsHeld)
                return;

            Extinguisher best = spareExtinguisher;
            if (best == null)
            {
                float bestDist = float.MaxValue;
                foreach (var ext in FindObjectsOfType<Extinguisher>(true))
                {
                    if (ext == null || (ext.grab != null && ext.grab.IsHeld)) continue;
                    float d = Vector3.Distance(ext.transform.position, spareExtinguisherSpot);
                    if (d < bestDist) { bestDist = d; best = ext; }
                }
            }
            if (best != null && best.grab != null && !best.grab.IsHeld)
            {
                // 先纠正姿态（躺着的瓶身转成竖立），再按「渲染包围盒」落位：
                // 瓶身立在目标点、底部贴地，既不会因为子物体偏移插进墙里，也不会横躺挡路
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
                best.ResetQuizForStageTwo();   // 重新拾取要再答 B 组 5 题
                Debug.Log("[Stage2] 备用灭火器已摆到安全出口门边（图 3 位置），问答已换成 B 组 5 题：" + best.name);
            }
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
        }
    }
}
