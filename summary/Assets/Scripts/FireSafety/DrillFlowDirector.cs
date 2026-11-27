using System.Collections.Generic;
using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 第 42 轮：演练主流程编排（决策训练线）。
    /// 七步口径：
    /// ①事前教育（PreTrainingQuiz，按情景 10 题 / 60%，只作教学记录，不作为进入演练或疏散的条件）
    /// ②发现火灾（进入情景后自动识别，拉响警报作为背景音，提示呼喊提醒同事）
    /// ③警示与报告（按手动报警按钮 → 全楼警报 + 119；按钮缺席时明确作为「情境前提」自动成立）
    /// ④优先安全疏散并判断是否可初期处置（给出结论与理由；拿不准就疏散）
    /// ⑤符合条件时才尝试初期灭火（只在小规模初期火灾、适当器材、充分视野、安全撤离路线均具备时）
    /// ⑥危险增加或灭火失败时立即疏散（烟气持续、火势停滞、超时三种信号；不使用剩余生命值做门槛）
    ///     也允许「不灭火直接走」「喷了一半停下来走」，两者都记为成功应对
    /// ⑦到达集合地点并报告（清点人数、报告未撤离人员、不返楼，输出行为回顾）＝ 结束状态
    /// 绕行、向 119 求助等支线留待专家审查后再补，本轮先保证核心疏散分支可用。
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class DrillFlowDirector : MonoBehaviour
    {
        public static DrillFlowDirector Instance { get; private set; }

        public enum Stage
        {
            Idle = 0,
            Discovered = 1,
            Alerted = 2,
            Assessed = 3,
            Extinguishing = 4,
            Evacuating = 5,
            Reported = 6,
            Debriefed = 7
        }

        /// <summary>第 46 轮：每次运行随机分配「普通员工 / 初期应对负责人」，决定本次允许的行程分支。</summary>
        public enum DrillRole
        {
            Employee = 0,       // 普通办公室人员：按现场消防计划优先疏散，不尝试灭火
            ResponseLead = 1    // 初期应对负责人：条件全部满足时才允许有条件初期应对
        }

        [Header("引用（留空自动查找）")]
        public DrillScenario scenario;
        public PlayerVitals vitals;
        public ExitDoor exitDoor;
        public AlarmCallButton alarmButton;
        public FireAlarm fireAlarm;
        public PreTrainingQuiz preTraining;
        public DrillEndPanel endPanel;

        [Header("身份（第 46 轮：每次运行随机分配）")]
        [Tooltip("true = 每次进入正式演练随机分配普通员工 / 初期应对负责人；false = 固定用 fixedRole")]
        public bool randomizeRoleEachRun = true;
        [Tooltip("randomizeRoleEachRun=false 时使用的固定身份（自检/演示用）")]
        public DrillRole fixedRole = DrillRole.Employee;

        [Header("集合点")]
        [Tooltip("门外广场的集合点位置")]
        public Vector3 assemblyPoint = new Vector3(10.40f, 0f, 1.35f);
        [Tooltip("进入这个半径算「到达安全地点」（米）")]
        public float assemblyRadius = 2.4f;

        [Header("④ 判断是否可初期处置")]
        [Tooltip("判断为可初期处置的最大火源距离（起火点到灭火器的水平距离，米）")]
        public float initialAttackMaxDistance = 4.2f;

        [Header("⑥ 危险增加判定（第 42 轮：不使用剩余生命值）")]
        [Tooltip("连续处于烟气中的秒数达到该值 → 危险增加，立即疏散")]
        public float smokeDangerSeconds = 8f;
        [Tooltip("首次喷射后超过该秒数仍未扑灭 → 危险增加，立即疏散")]
        public float maxExtinguishSeconds = 60f;
        [Tooltip("持续喷射但灭火进度停滞超过该秒数 → 危险增加，立即疏散")]
        public float stallSeconds = 14f;
        [Tooltip("灭火进度的最小有效增长（低于它算停滞）")]
        public float stallTolerance = 0.02f;
        [Tooltip("允许「不灭火直接疏散」：玩家没喷过火、又已远离火源，就判定为选择疏散")]
        public bool allowDirectEvacuation = true;
        [Tooltip("不灭火直接疏散的「远离火源」距离（米）")]
        public float directEvacuateDistance = 6f;
        [Tooltip("还要已经走到安全出口附近（距门 2.2 米内）才认定是「选择直接疏散」，避免刚按完报警就被判疏散")]
        public float directEvacuateExitDistance = 2.2f;
        [Tooltip("已经喷过又停下，连续多少秒不喷就判定「停止灭火并疏散」")]
        public float stopAndEvacuateSeconds = 5f;

        [Header("行为")]
        [Tooltip("进入情景后自动拉响警报作为背景音（三套情景通用）")]
        public bool autoAlarmOnDiscover = true;
        [Tooltip("火灭后自动解锁安全出口（与原 StageTwoFlow 一致）")]
        public bool unlockOnExtinguished = true;
        [Tooltip("第 42 轮新口径下，疏散时总会解锁安全出口")]
        public bool unlockOnEvacuate = true;

        // ---------------- 对外状态 ----------------

        public Stage CurrentStage { get; private set; } = Stage.Idle;
        public bool ReportedAtAssembly { get; private set; }
        public string LastAssessment { get; private set; } = "";
        public bool CanAttemptInitial { get; private set; }

        /// <summary>本次演练随机分配到的身份。</summary>
        public DrillRole AssignedRole { get; private set; } = DrillRole.Employee;
        /// <summary>普通员工只能走 B（立即疏散）；初期应对负责人满足全部条件时才可走 A。</summary>
        public bool RoleAllowsAttack => AssignedRole == DrillRole.ResponseLead;
        public bool RoleAssigned { get; private set; }
        /// <summary>身份的可读名称（三语）。</summary>
        public string RoleName => AssignedRole == DrillRole.ResponseLead
            ? DrillLocale.T("初期应对负责人", "Initial response lead", "초기 대응 담당자")
            : DrillLocale.T("普通员工", "General employee", "일반 직원");
        /// <summary>事后反馈里记录的行为回顾（第 46 轮新增：警报 / 判断 / 操作 / 路线）。</summary>
        public string DebriefRecord { get; private set; } = "";
        public float ExtinguishElapsed => _attackStart > 0f ? Time.time - _attackStart : 0f;

        /// <summary>是否在灭火前就选择疏散（用户要求：不灭火直接疏散也算成功）。</summary>
        public bool EvacuatedWithoutExtinguishing { get; private set; }
        /// <summary>是否喷过火（拿过灭火器并实际喷射）。</summary>
        public bool HasSprayed { get; private set; }
        /// <summary>是否「喷到一半主动停止并撤离」。</summary>
        public bool StoppedAttackAndEvacuated { get; private set; }
        /// <summary>火是否被扑灭。</summary>
        public bool FireExtinguished { get; private set; }
        /// <summary>是否因危险增加而转疏散。</summary>
        public bool DangerIncreased { get; private set; }
        /// <summary>转疏散的原因（危险增加的具体信号 / 主动撤离 / 评估不满足）。</summary>
        public string EvacuationReason { get; private set; } = "";
        /// <summary>报警是否已完成（按钮或情境前提）。</summary>
        public bool AlarmReported { get; private set; }
        /// <summary>行为回顾（用于结束状态与后续训练）。</summary>
        public string ReportRecord { get; private set; } = "";
        public List<string> BehaviorLog { get; } = new List<string>();

        /// <summary>第 42 轮口径：剩余生命值不参与「能否继续灭火」的判定（固定 false，供自检与文档引用）。</summary>
        public bool UsesVitalsAsDangerGate => false;

        /// <summary>第 42 轮保留字段（旧场景序列化用），不参与任何判定：不再用剩余生命值做灭火门槛。</summary>
        [HideInInspector] public int evacuateHealthThreshold = 4;

        float _attackStart;
        float _firstSprayAt = -1f;
        float _notSprayingSince = -1f;
        float _discoveredAt = -1f;
        float _smokeEnterAt = -1f;
        float _lastProgress = -1f;
        float _lastProgressAt = -1f;
        bool _discoveredToast, _alertedToast, _assessed, _assessedToast, _evacuated, _evacuateToast, _reportToast;

        void Awake()
        {
            Instance = this;
            if (scenario == null) scenario = FindObjectOfType<DrillScenario>();
            if (vitals == null) vitals = FindObjectOfType<PlayerVitals>();
            if (exitDoor == null) exitDoor = FindObjectOfType<ExitDoor>();
            if (alarmButton == null) alarmButton = FindObjectOfType<AlarmCallButton>();
            if (fireAlarm == null) fireAlarm = FindObjectOfType<FireAlarm>();
            if (preTraining == null) preTraining = FindObjectOfType<PreTrainingQuiz>();
            if (endPanel == null) endPanel = FindObjectOfType<DrillEndPanel>();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        /// <summary>进入正式演练时分配身份：普通员工 / 初期应对负责人（每次运行随机）。</summary>
        public void AssignRoleForRun()
        {
            AssignedRole = randomizeRoleEachRun
                ? (UnityEngine.Random.value < 0.5f ? DrillRole.Employee : DrillRole.ResponseLead)
                : fixedRole;
            RoleAssigned = true;
            Debug.Log("[Flow] ② 身份分配：" + RoleName);
        }

        /// <summary>自检/探针用：固定身份，不影响随机配置。</summary>
        public void SetRole(DrillRole role)
        {
            AssignedRole = role;
            RoleAssigned = true;
        }

        void Update()
        {
            if (scenario == null) return;
            TrackDangerClocks();

            // ② 发现火灾
            if (CurrentStage == Stage.Idle)
            {
                if (scenario.CurrentMode != DrillScenario.Mode.Formal) return;
                if (!RoleAssigned) AssignRoleForRun();
                CurrentStage = Stage.Discovered;
                _discoveredAt = Time.time;
                Log("① 事前教学完成 → ② 发现火灾与警示");
                if (!_discoveredToast)
                {
                    _discoveredToast = true;
                    string roleLine = RoleAllowsAttack
                        ? DrillLocale.T("本次身份：初期应对负责人 —— 只有当火势是初期小火、器材到位、视野清楚、身后退路安全时才可以尝试灭火；任何一条不确定就疏散。",
                                        "Role: initial response lead — attempt an attack only if the fire is incipient, equipment is at hand, the view is clear and your escape route is safe; if any is uncertain, evacuate.",
                                        "이번 역할: 초기 대응 담당자 — 초기 소규모 화재, 장비 확보, 시야 확보, 뒤쪽 도주로 안전이 모두 충족될 때만 소화를 시도하세요. 하나라도 불확실하면 대피합니다.")
                        : DrillLocale.T("本次身份：普通员工 —— 按现场消防计划优先疏散，不尝试灭火；边走边报警，不因报告延误撤离。",
                                        "Role: general employee — evacuate first per the site fire plan, do not attempt an attack; raise the alarm on the way, never delay evacuation to report.",
                                        "이번 역할: 일반 직원 — 현장 소방 계획에 따라 대피 우선, 소화 시도 금지; 이동 중 경보·신고, 신고 때문에 대피 지연 금지.");
                    DrillToast.Show(roleLine + "\n" + DrillLocale.T(
                        "发现火灾：先大声呼喊提醒同事，再按手动报警按钮并拨打 119。",
                        "Fire discovered: shout to warn colleagues, then press the manual call point and dial 119.",
                        "화재 발견: 동료에게 외쳐 알리고 수동 발신기를 누른 뒤 119에 신고하세요."), null, 8f);
                }
                if (autoAlarmOnDiscover && fireAlarm != null && !fireAlarm.IsRinging)
                    fireAlarm.StartAlarm();          // 警报作为背景音，三套情景都会响
                return;
            }

            // ③ 警示与报告
            if (CurrentStage == Stage.Discovered)
            {
                if (alarmButton != null)
                {
                    if (alarmButton.Triggered) MoveToAlerted("手动报警按钮");
                }
                else if (Time.time - _discoveredAt >= 2f)
                {
                    // 按钮缺席：明确作为情境前提（现场负责人已按响报警并报 119）
                    MoveToAlerted("情境前提：报警与 119 报告已由现场负责人完成");
                }
                return;
            }

            // ④ 判断能否初期处置
            if (CurrentStage == Stage.Alerted)
            {
                if (!_assessed)
                {
                    _assessed = true;
                    AssessInitialAttack();
                    CurrentStage = Stage.Assessed;
                }
                return;
            }

            if (CurrentStage == Stage.Assessed)
            {
                CurrentStage = CanAttemptInitial ? Stage.Extinguishing : Stage.Evacuating;
                if (CanAttemptInitial)
                {
                    _attackStart = Time.time;
                    Log("④ 判定：可尝试初期灭火（也可以选择直接疏散）");
                }
                else
                {
                    Log("④ 判定：不具备初期处置条件 → 优先疏散");
                    BeginEvacuation(DrillLocale.T("评估结论：不具备初期处置条件，立即沿安全出口疏散（同样属于正确应对）。",
                                                  "Assessment: conditions not met — evacuate via the safe exit. This is a correct response too.",
                                                  "평가 결과: 초기 대응 조건 미충족 → 비상구로 즉시 대피하세요(이 또한 올바른 대응)."), false, false);
                }
                return;
            }

            // ⑤ 初期灭火：成功 / 危险增加 / 主动停止
            if (CurrentStage == Stage.Extinguishing)
            {
                var fire = scenario.CurrentScenario != null ? scenario.CurrentScenario.fire : null;
                var ext = scenario.CurrentScenario != null ? scenario.CurrentScenario.extinguisher : null;

                if (fire != null && fire.IsExtinguished)
                {
                    FireExtinguished = true;
                    Log("⑤ 初期灭火成功 → ⑥ 转移至安全地点");
                    DrillToast.Show(DrillLocale.T(
                        "火已扑灭：留守观察 1 分钟防复燃，然后沿绿色安全出口转移至集合点。",
                        "Fire out: watch for re-ignition, then move to the assembly point via the green exit.",
                        "진화 완료: 재점화 확인 후 녹색 비상구로 집결지로 이동하세요."), null, 4f);
                    BeginEvacuation("", true, false);
                    return;
                }

                if (ext != null && ext.IsSpraying)
                {
                    if (!HasSprayed) _firstSprayAt = Time.time;
                    HasSprayed = true;
                    _notSprayingSince = -1f;
                }
                else if (HasSprayed && _notSprayingSince < 0f)
                {
                    _notSprayingSince = Time.time;
                }

                // 从未喷射、已远离火源并走到安全出口附近 → 玩家选择直接疏散
                if (!HasSprayed && allowDirectEvacuation
                    && PlayerToFireDistance() > directEvacuateDistance && PlayerNearExit())
                {
                    EvacuatedWithoutExtinguishing = true;
                    Log("⑤ 未进行灭火，选择直接疏散（成功路径）");
                    BeginEvacuation(DrillLocale.T("未进行灭火，直接沿安全出口疏散：这是允许且正确的选择。",
                                                  "No attack attempted — evacuating directly via the safe exit is allowed and correct.",
                                                  "소화를 시도하지 않고 안전 출구로 바로 대피하는 것도 허용된 올바른 선택입니다."), false, false);
                    return;
                }

                // 喷到一半停下 → 停止灭火并疏散
                if (HasSprayed && _notSprayingSince > 0f && Time.time - _notSprayingSince >= stopAndEvacuateSeconds)
                {
                    StoppedAttackAndEvacuated = true;
                    Log("⑤ 停止灭火并疏散（成功路径）");
                    BeginEvacuation(DrillLocale.T("已停止灭火：立刻沿安全出口疏散，把火场交给消防员（停止灭火并撤离算成功应对）。",
                                                  "Attack stopped: evacuate now and leave the fire to the fire service — stopping and leaving counts as success.",
                                                  "소화 중단: 즉시 대피하고 화재는 소방대에 맡기세요(중단 후 대피도 성공 대응)."), false, false);
                    return;
                }

                string danger = DangerSignal();
                if (!string.IsNullOrEmpty(danger))
                {
                    DangerIncreased = true;
                    Log("⑥ 危险增加（" + danger + "）→ 立即疏散");
                    BeginEvacuation(DrillLocale.T(
                        "危险增加：" + danger + "，继续灭火已不安全 → 立即停止灭火，沿绿色安全出口疏散。",
                        "Danger increasing: " + danger + " — continuing the attack is unsafe. Stop and evacuate via the green exit.",
                        "위험 증가: " + danger + " → 계속 소화는 안전하지 않습니다. 즉시 중단하고 녹색 비상구로 대피하세요."), true, false);
                }
                return;
            }

            // ⑥ 疏散
            if (CurrentStage == Stage.Evacuating)
            {
                if (!_evacuated)
                {
                    _evacuated = true;
                    if (unlockOnEvacuate && exitDoor != null && !exitDoor.IsUnlocked) exitDoor.Unlock();
                }
                if (CheckAssembly())
                {
                    CurrentStage = Stage.Reported;
                }
                return;
            }

            // ⑦ 到达安全地点并报告（结束状态）
            if (CurrentStage == Stage.Reported && !_reportToast)
            {
                _reportToast = true;
                ReportAtAssembly();
            }
        }

        void MoveToAlerted(string how)
        {
            AlarmReported = true;
            CurrentStage = Stage.Alerted;
            Log("③ 警示与报告：" + how + "（警报 + 119）");
            if (!_alertedToast)
            {
                _alertedToast = true;
                DrillToast.Show(DrillLocale.T(
                    "已报警：全楼警报已拉响，119 已接报（地址 / 起火物 / 火势 / 有无被困）。报告不延误疏散，边走边报。",
                    "Alarm raised: building alarm sounding and 119 notified (address, material, size, trapped). Reporting must not delay evacuation.",
                    "신고 완료: 전관 경보 작동 및 119 신고(주소·연소물·규모·갇힌 인원). 신고 때문에 대피가 늦어지면 안 됩니다."), null, 4f);
            }
        }

        /// <summary>④ 判定：火势是否仍在初期、灭火器是否够近（第 42 轮去掉生命值条件）。</summary>
        void AssessInitialAttack()
        {
            var sc = scenario.CurrentScenario;
            var fire = sc != null ? sc.fire : null;
            var ext = sc != null ? sc.extinguisher : null;
            bool fireAlive = fire != null && fire.IsBurning && !fire.IsExtinguished;
            float dist = (fire != null && ext != null)
                ? Vector2.Distance(new Vector2(fire.transform.position.x, fire.transform.position.z),
                                   new Vector2(ext.transform.position.x, ext.transform.position.z))
                : 999f;
            bool nearEnough = dist <= initialAttackMaxDistance;
            bool powerOk = sc == null || !sc.requirePowerCut || (sc.powerSwitch != null && sc.powerSwitch.IsCut);

            CanAttemptInitial = fireAlive && nearEnough && RoleAllowsAttack;
            if (!RoleAllowsAttack)
            {
                // 第 46 轮：普通员工没有初期处置权限 —— 条件再好也走 B（立即疏散）
                CanAttemptInitial = false;
                LastAssessment = DrillLocale.T(
                    "本次身份为普通员工：按现场消防计划优先疏散（普通员工不尝试灭火）。火势 "
                    + (fireAlive ? "仍在初期" : "已变化") + "、灭火器距离 " + dist.ToString("F1")
                    + " 米 —— 即使条件看似具备，也走「B 立即疏散」。",
                    "Role is general employee: evacuate first per the site fire plan (employees do not attempt an attack). Fire "
                    + (fireAlive ? "still incipient" : "changed") + ", extinguisher " + dist.ToString("F1")
                    + " m — even if conditions look adequate, follow route B (immediate evacuation).",
                    "이번 역할은 일반 직원: 현장 소방 계획에 따라 대피 우선(일반 직원은 소화 시도 금지). 화재 "
                    + (fireAlive ? "초기 단계" : "상황 변화") + ", 소화기 " + dist.ToString("F1")
                    + " m — 조건이 충족돼 보여도 B(즉시 대피)를 따릅니다.");
            }
            else
            {
                LastAssessment = CanAttemptInitial
                    ? (powerOk
                        ? DrillLocale.T("火势处于初期、灭火器在 " + dist.ToString("F1") + " 米内、视野清楚、身后有退路 → 可以尝试初期灭火（A）；也可以选择直接疏散（B）。",
                                        "Incipient fire, extinguisher within " + dist.ToString("F1") + " m, clear view, escape route behind → initial attack allowed (A); evacuating directly (B) is also valid.",
                                        "초기 화재, 소화기 " + dist.ToString("F1") + " m 이내, 시야 확보, 뒤쪽 도주로 확보 → 초기 소화 가능(A); 바로 대피(B)도 가능합니다.")
                        : DrillLocale.T("火势处于初期、灭火器在 " + dist.ToString("F1") + " 米内 → 可以尝试，但电气火必须先断电。",
                                        "Incipient fire, extinguisher within " + dist.ToString("F1") + " m — allowed, but cut the power first for electrical fires.",
                                        "초기 화재, 소화기 " + dist.ToString("F1") + " m 이내 → 가능하나 전기 화재는 전원 차단이 먼저."))
                    : DrillLocale.T("不具备初期处置条件（火势规模 / 器材距离 / 视野）→ 优先疏散（B），不要尝试灭火。",
                                    "Conditions not met (fire size / equipment distance / visibility) → evacuate (B); do not attempt an attack.",
                                    "초기 대응 조건 미충족(화재 규모/장비 거리/시야) → 대피 우선(B), 소화 시도 금지.");
            }

            if (!_assessedToast)
            {
                _assessedToast = true;
                DrillToast.Show(LastAssessment, null, 5f);
            }
            Debug.Log("[Flow] ④ 判定：" + (CanAttemptInitial ? "可尝试初期灭火（不强制）" : "优先疏散")
                      + " 火距=" + dist.ToString("F2") + " 未断电=" + (sc != null && sc.requirePowerCut && !powerOk));
        }

        /// <summary>⑥ 危险信号：烟气持续 / 灭火超时 / 进度停滞。刻意不含剩余生命值。</summary>
        string DangerSignal()
        {
            if (_smokeEnterAt > 0f && Time.time - _smokeEnterAt >= smokeDangerSeconds)
                return DrillLocale.T("烟气持续累积、能见度下降", "accumulating smoke and falling visibility", "연기 누적·가시거리 저하");

            if (_firstSprayAt > 0f && Time.time - _firstSprayAt >= maxExtinguishSeconds)
                return DrillLocale.T("灭火超过 " + Mathf.RoundToInt(maxExtinguishSeconds) + " 秒仍未扑灭",
                                     "still burning after " + Mathf.RoundToInt(maxExtinguishSeconds) + " s of attack",
                                     maxExtinguishSeconds + "초 경과에도 진화되지 않음");

            var sc = scenario.CurrentScenario;
            var fire = sc != null ? sc.fire : null;
            var ext = sc != null ? sc.extinguisher : null;
            if (fire != null && ext != null && ext.IsSpraying)
            {
                if (_lastProgress < 0f || fire.progress01 - _lastProgress > stallTolerance)
                {
                    _lastProgress = fire.progress01;
                    _lastProgressAt = Time.time;
                }
                else if (_lastProgressAt > 0f && Time.time - _lastProgressAt >= stallSeconds)
                {
                    return DrillLocale.T("持续喷射但火势不再减弱（" + Mathf.RoundToInt(stallSeconds) + " 秒无进展）",
                                         "spraying but the fire is not shrinking (" + Mathf.RoundToInt(stallSeconds) + " s no progress)",
                                         "분사 중이나 불세가 줄지 않음(" + Mathf.RoundToInt(stallSeconds) + "초 무진전)");
                }
            }
            return "";
        }

        void TrackDangerClocks()
        {
            if (vitals == null) { _smokeEnterAt = -1f; return; }
            if (vitals.InSmoke)
            {
                if (_smokeEnterAt < 0f) _smokeEnterAt = Time.time;
            }
            else
            {
                _smokeEnterAt = -1f;
            }
        }

        float PlayerToFireDistance()
        {
            var cam = Camera.main;
            var sc = scenario.CurrentScenario;
            if (cam == null || sc == null || sc.fire == null) return 0f;
            Vector3 p = cam.transform.position;
            Vector3 aim = sc.fire.AimPoint;
            return Vector2.Distance(new Vector2(p.x, p.z), new Vector2(aim.x, aim.z));
        }

        /// <summary>玩家是否已经走到安全出口附近（用于区分「去灭火」与「直接疏散」）。</summary>
        public bool PlayerNearExit()
        {
            var cam = Camera.main;
            if (cam == null || exitDoor == null) return false;
            Vector3 p = cam.transform.position;
            Vector3 d = exitDoor.transform.position;
            return Vector2.Distance(new Vector2(p.x, p.z), new Vector2(d.x, d.z)) <= directEvacuateExitDistance;
        }

        /// <summary>进入疏散：解锁出口、给出一次提示、记入行为日志。dangering=true 时文案强调危险增加。</summary>
        public void BeginEvacuation(string reason, bool danger, bool silent)
        {
            if (CurrentStage == Stage.Evacuating || CurrentStage == Stage.Reported || CurrentStage == Stage.Debriefed) return;
            CurrentStage = Stage.Evacuating;
            EvacuationReason = reason;
            if (!string.IsNullOrEmpty(reason)) Log("⑥ " + reason);
            if (unlockOnEvacuate && exitDoor != null && !exitDoor.IsUnlocked) exitDoor.Unlock();
            if (!_evacuateToast || danger)
            {
                _evacuateToast = true;
                string text;
                if (FireExtinguished)
                    text = DrillLocale.T("疏散：低姿弯腰，沿绿色安全出口标志撤离，不乘电梯、不折返取物。",
                                         "Evacuate: stay low, follow the green exit signs, no lifts, no going back for belongings.",
                                         "대피: 낮은 자세로 녹색 비상구를 따라 이동, 엘리베이터·물건 회수 금지.");
                else
                    text = DrillLocale.T("疏散：低姿弯腰、用湿毛巾捂住口鼻，沿绿色安全出口标志撤离；不乘电梯、不折返。",
                                         "Evacuate: stay low, cover your mouth with a wet cloth, follow the green exit signs; no lifts, no going back.",
                                         "대피: 낮은 자세, 젖은 수건으로 코·입을 막고 녹색 비상구를 따라 이동; 엘리베이터·되돌아가기 금지.");
                if (!string.IsNullOrEmpty(reason) && !silent) text = reason + "\n" + text;
                DrillToast.Show(text, null, 6f);
            }
        }

        bool CheckAssembly()
        {
            var cam = Camera.main;
            if (cam == null) return false;
            Vector3 p = cam.transform.position;
            float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(assemblyPoint.x, assemblyPoint.z));
            if (d > assemblyRadius) return false;
            if (exitDoor != null && !exitDoor.IsUnlocked) return false;
            return true;
        }

        void ReportAtAssembly()
        {
            ReportedAtAssembly = true;
            Log("⑦ 到达集合地点并报告");

            string alarm = AlarmReported
                ? DrillLocale.T("✓ 已按报警装置并报 119", "✓ Alarm raised and 119 notified", "✓ 경보 작동·119 신고")
                : DrillLocale.T("! 未确认报警（应在安全地点补报 119）", "! Alarm unconfirmed (call 119 from safety)", "! 경보 미확인(안전한 곳에서 119 신고)");
            string judge = CanAttemptInitial
                ? DrillLocale.T("✓ 判断可初期处置，但仍可选择直接疏散", "✓ Initial attack was possible, direct evacuation still valid", "✓ 초기 대응 가능, 바로 대피도 유효")
                : DrillLocale.T("✓ 判断不具备初期处置条件 → 未尝试灭火", "✓ Judged unsuitable → no attack attempted", "✓ 초기 대응 불가 판단 → 소화 미시도");
            string action = FireExtinguished
                ? DrillLocale.T("✓ 初期灭火：成功扑灭", "✓ Initial attack: extinguished", "✓ 초기 소화: 성공")
                : EvacuatedWithoutExtinguishing
                    ? DrillLocale.T("✓ 未灭火，直接沿安全路线疏散（正确应对）", "✓ No attack, evacuated directly (correct)", "✓ 소화 없이 안전 대피(올바른 대응)")
                    : StoppedAttackAndEvacuated
                        ? DrillLocale.T("✓ 喷到一半主动停止并疏散（正确应对）", "✓ Stopped mid-attack and evacuated (correct)", "✓ 분사 중단 후 대피(올바른 대응)")
                        : DangerIncreased
                            ? DrillLocale.T("✓ 危险增加时停止灭火并疏散（正确应对）", "✓ Stopped when danger rose (correct)", "✓ 위험 증가 시 소화 중단·대피(올바른 대응)")
                            : DrillLocale.T("✓ 沿安全出口撤离", "✓ Evacuated via the safe exit", "✓ 안전 출구로 대피");
            string route = DrillLocale.T("✓ 走安全出口与楼梯，未乘电梯、未折返", "✓ Used the safe exit and stairs, no lift, no re-entry", "✓ 비상구·계단 이용, 엘리베이터·재입장 없음");
            string tail = DrillLocale.T("集合流程依现场消防计划执行：清点人数、向负责人报告未撤离人员、留在安全区等待消防到场，不擅自返楼。",
                                        "The assembly procedure follows the site fire plan: count people, report the missing to the person in charge, stay clear and wait for the fire service; never re-enter.",
                                        "집결 절차는 현장 소방 계획을 따릅니다: 인원 점검, 미대피자 보고, 안전 구역 대기, 재입장 금지.");

            ReportRecord = alarm + "\n" + judge + "\n" + action + "\n" + route + "\n" + tail;

            // 第 46 轮 ⑦ 事后反馈（Debriefing）：把「警报 / 疏散判断 / 操作错误 / 路线选择」四条记录摊开
            string roleLine = RoleAllowsAttack
                ? DrillLocale.T("身份：初期应对负责人（允许在条件满足时走 A）", "Role: initial response lead (route A allowed when conditions are met)", "역할: 초기 대응 담당자(조건 충족 시 A 허용)")
                : DrillLocale.T("身份：普通员工（按计划走 B，未尝试灭火）", "Role: general employee (route B per plan, no attack)", "역할: 일반 직원(계획대로 B, 소화 미시도)");
            string decisionLine = CanAttemptInitial
                ? DrillLocale.T("判断：条件满足，可尝试初期应对（A）/ 也可选择 B", "Decision: conditions met — A allowed, B also valid", "판단: 조건 충족 — A 가능, B도 유효")
                : DrillLocale.T("判断：走 B 立即疏散（" + (RoleAllowsAttack ? "条件不足" : "普通员工无初期处置权限") + "）",
                                "Decision: route B immediate evacuation (" + (RoleAllowsAttack ? "conditions not met" : "employees have no attack authority") + ")",
                                "판단: B 즉시 대피(" + (RoleAllowsAttack ? "조건 미충족" : "일반 직원은 초기 대응 권한 없음") + ")");
            string errorLine = FireExtinguished || EvacuatedWithoutExtinguishing || StoppedAttackAndEvacuated || DangerIncreased
                ? DrillLocale.T("操作：无危险操作记录（灭火器流程按「拔销 → 压把 → 对准根部」执行）", "Actions: no unsafe operation recorded", "조작: 위험 조작 기록 없음")
                : DrillLocale.T("操作：未完成灭火器操作即撤离（属于允许选择，不计为错误）", "Actions: left without completing extinguisher operation (allowed, not an error)", "조작: 소화기 조작 미완료 후 대피(허용된 선택, 오류 아님)");
            string routeLine = DrillLocale.T("路线：安全出口 + 楼梯，未乘电梯、未折返", "Route: safe exit + stairs, no lift, no re-entry", "경로: 비상구+계단, 엘리베이터·재입장 없음");
            DebriefRecord = roleLine + "\n" + decisionLine + "\n" + errorLine + "\n" + routeLine;

            string debriefHeader = DrillLocale.T("【事后反馈 · Debriefing】回顾本次警报、判断、操作与路线，用于下一次训练：",
                                                "[Debriefing] Review the alarm, decision, actions and route from this run:",
                                                "[사후 피드백] 이번 경보·판단·조작·경로를 검토해 다음 훈련에 반영:");
            ReportRecord = ReportRecord + "\n\n" + debriefHeader + "\n" + DebriefRecord;

            DrillToast.Show(DrillLocale.T(
                "已到达安全地点并报告：清点本部门人数、向现场负责人报告未撤离人员，原地等待消防到场。",
                "Reached the assembly point and reported: count your team, report missing persons to the person in charge, wait for the fire service.",
                "안전 집결지 도착·보고: 인원 점검, 미대피자를 책임자에게 보고, 소방대 대기."), null, 6f);

            if (endPanel == null) endPanel = FindObjectOfType<DrillEndPanel>();
            if (endPanel != null) endPanel.Show(ReportRecord);

            // 报告完成即进入事后反馈阶段（Debriefing），HUD 与结束页都停在这一步
            CurrentStage = Stage.Debriefed;
            Log("⑦ 到达集合地点并报告 → ⑧ 事后反馈（" + RoleName + "）");
            Debug.Log("[Flow] ⑦ 到达集合点并报告，流程完成（" + (EndingSuccess ? "成功" : "待补报警") + "）");
        }

        /// <summary>本次演练是否算成功应对（不灭火直接疏散、停止灭火、危险增加转疏散都算）。</summary>
        public bool EndingSuccess =>
            ReportedAtAssembly && AlarmReported &&
            (FireExtinguished || EvacuatedWithoutExtinguishing || StoppedAttackAndEvacuated || DangerIncreased || CanAttemptInitial == false);

        void Log(string entry)
        {
            BehaviorLog.Add(entry);
            Debug.Log("[Flow] " + entry);
        }

        /// <summary>教学演示与探针用：直接走「危险增加 → 立即停止并疏散」通道（不改判定参数）。</summary>
        public void RaiseDangerAndEvacuate(string signal = null)
        {
            DangerIncreased = true;
            string sig = string.IsNullOrEmpty(signal)
                ? DrillLocale.T("烟气持续累积、能见度下降", "accumulating smoke and falling visibility", "연기 누적·가시거리 저하")
                : signal;
            EvacuationReason = DrillLocale.T("危险增加：" + sig + "，继续灭火已不安全 → 立即停止灭火并疏散。",
                                             "Danger increasing: " + sig + " — stop the attack and evacuate now.",
                                             "위험 증가: " + sig + " → 소화를 중단하고 즉시 대피하세요.");
            if (CurrentStage == Stage.Reported) return;
            if (CurrentStage == Stage.Evacuating)
            {
                // 已在疏散阶段也要把「危险增加」这件事讲出来，不能只写日志
                DrillToast.Show(EvacuationReason, null, 6f);
                Log("⑥ 危险增加（演示）：保持疏散状态并提示危险增加");
                return;
            }
            BeginEvacuation(EvacuationReason, true, false);
        }

        /// <summary>自检/探针用：强制推进到某阶段（不改动原有灭火逻辑）。</summary>
        public void ForceStage(Stage stage) { CurrentStage = stage; }

        /// <summary>自检用：当前阶段的可读名称（按第 46 轮课程口径：Pre-briefing → … → Debriefing）。</summary>
        public string StageName
        {
            get
            {
                switch (CurrentStage)
                {
                    case Stage.Discovered: return DrillLocale.T("发现火灾与警示", "Detection & Alert", "화재 인지 및 전파");
                    case Stage.Alerted: return DrillLocale.T("初期应对或疏散判断", "Decision", "초기 대응 또는 대피 판단");
                    case Stage.Assessed: return DrillLocale.T("初期应对或疏散判断", "Decision", "초기 대응 또는 대피 판단");
                    case Stage.Extinguishing: return DrillLocale.T("A · 有条件初期应对", "A · Conditional attack", "A · 조건부 초기 대응");
                    case Stage.Evacuating: return DrillLocale.T("B · 立即疏散", "B · Immediate evacuation", "B · 즉시 대피");
                    case Stage.Reported: return DrillLocale.T("到达安全地点与报告", "Assembly & Reporting", "안전한 장소 도착 및 보고");
                    case Stage.Debriefed: return DrillLocale.T("事后反馈", "Debriefing", "사후 피드백");
                    default: return DrillLocale.T("事前教学", "Pre-briefing", "사전 교육");
                }
            }
        }
    }
}
