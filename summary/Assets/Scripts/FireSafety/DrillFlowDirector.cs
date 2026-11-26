using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 第 41 轮：演练主流程编排（把原来只有「起火 → 灭火 → 出口」的链路补成 7 步）。
    /// ①事前教育（PreTrainingQuiz，10 题 / 60% 通过，由 StartScreen 在开始前拦住）
    /// ②发现火灾（进入情景后自动识别，拉响警报作为背景音）
    /// ③警示与报告（走到手动报警按钮前注视 0.8 秒，报警 + 119）
    /// ④优先疏散并判断能否初期处置（给出判定结论与理由）
    /// ⑤符合条件时尝试初期灭火（沿用原有取瓶、答题、喷射流程）
    /// ⑥危险增加或失败时疏散（生命值过低 / 超时未灭 → 解锁出口并催撤离）
    /// ⑦到达安全地点并报告（走到广场集合点，清点人数并向负责人报告）
    /// 只做「提示 + 判定 + 兜底解锁」，不替代原有灭火与门的逻辑。
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
            Reported = 6
        }

        [Header("引用（留空自动查找）")]
        public DrillScenario scenario;
        public PlayerVitals vitals;
        public ExitDoor exitDoor;
        public AlarmCallButton alarmButton;
        public FireAlarm fireAlarm;
        public PreTrainingQuiz preTraining;

        [Header("集合点")]
        [Tooltip("门外广场的集合点位置")]
        public Vector3 assemblyPoint = new Vector3(10.40f, 0f, 1.35f);
        [Tooltip("进入这个半径算「到达安全地点」（米）")]
        public float assemblyRadius = 2.4f;

        [Header("判定参数")]
        [Tooltip("灭火超过这个秒数仍未扑灭，判定为「危险增加」，改为撤离")]
        public float maxExtinguishSeconds = 80f;
        [Tooltip("生命值低于等于该值时催促撤离")]
        public int evacuateHealthThreshold = 4;
        [Tooltip("判断为可初期处置的最大火源距离（点到灭火器的水平距离，米）")]
        public float initialAttackMaxDistance = 4.2f;

        [Header("行为")]
        [Tooltip("进入情景后自动拉响警报作为背景音（三套情景通用）")]
        public bool autoAlarmOnDiscover = true;
        [Tooltip("火灭后自动解锁安全出口（与原 StageTwoFlow 一致）")]
        public bool unlockOnExtinguished = true;

        public Stage CurrentStage { get; private set; } = Stage.Idle;
        public bool ReportedAtAssembly { get; private set; }
        public string LastAssessment { get; private set; } = "";
        public bool CanAttemptInitial { get; private set; }
        public float ExtinguishElapsed => _extinguishStart > 0f ? Time.time - _extinguishStart : 0f;

        float _extinguishStart;
        bool _discoveredToast, _alertedToast, _assessed, _evacuated, _extinguishTimeout, _reportToast;

        void Awake()
        {
            Instance = this;
            if (scenario == null) scenario = FindObjectOfType<DrillScenario>();
            if (vitals == null) vitals = FindObjectOfType<PlayerVitals>();
            if (exitDoor == null) exitDoor = FindObjectOfType<ExitDoor>();
            if (alarmButton == null) alarmButton = FindObjectOfType<AlarmCallButton>();
            if (fireAlarm == null) fireAlarm = FindObjectOfType<FireAlarm>();
            if (preTraining == null) preTraining = FindObjectOfType<PreTrainingQuiz>();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (scenario == null) return;

            // ② 发现火灾：正式情景开始后自动进入
            if (CurrentStage == Stage.Idle)
            {
                if (scenario.CurrentMode != DrillScenario.Mode.Formal) return;
                CurrentStage = Stage.Discovered;
                if (!_discoveredToast)
                {
                    _discoveredToast = true;
                    DrillToast.Show(DrillLocale.T(
                        "发现火灾：先大声呼喊提醒同事，再按手动报警按钮并拨打 119。",
                        "Fire discovered: shout to warn colleagues, then press the manual call point and dial 119.",
                        "화재 발견: 동료에게 외쳐 알리고 수동 발신기를 누른 뒤 119에 신고하세요."), null, 5f);
                }
                if (autoAlarmOnDiscover && fireAlarm != null && !fireAlarm.IsRinging)
                    fireAlarm.StartAlarm();          // 警报作为背景音，三套情景都会响
                return;
            }

            // ③ 警示与报告
            if (CurrentStage == Stage.Discovered)
            {
                bool pressed = alarmButton != null && alarmButton.Triggered;
                if (pressed)
                {
                    CurrentStage = Stage.Alerted;
                    if (!_alertedToast)
                    {
                        _alertedToast = true;
                        DrillToast.Show(DrillLocale.T(
                            "已报警：警报已拉响，119 已接报（地址 / 起火物 / 火势 / 有无被困）。",
                            "Alarm raised: 119 notified (address, material, size, trapped people).",
                            "신고 완료: 경보 작동 및 119 신고(주소·연소물·규모·갇힌 인원)."), null, 4f);
                    }
                }
                else if (alarmButton != null && alarmButton.PlayerDistance < 3.2f)
                {
                    // 走近时给一次「按这里」的提示，不刷屏
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
                    CurrentStage = CanAttemptInitial ? Stage.Extinguishing : Stage.Evacuating;
                    if (CanAttemptInitial) _extinguishStart = Time.time;
                    else UnlockForEvacuation("评估结论：火势已不宜自行处置，立即疏散");
                }
                return;
            }

            // ⑤ 初期灭火：监控成功 / 失败
            if (CurrentStage == Stage.Extinguishing)
            {
                var fire = scenario.CurrentScenario != null ? scenario.CurrentScenario.fire : null;
                if (fire != null && fire.IsExtinguished)
                {
                    CurrentStage = Stage.Evacuating;
                    if (unlockOnExtinguished && exitDoor != null && !exitDoor.IsUnlocked) exitDoor.Unlock();
                    DrillToast.Show(DrillLocale.T(
                        "火已扑灭：留守观察 1 分钟防复燃，然后沿绿色安全出口撤离。",
                        "Fire out: watch for re-ignition, then evacuate via the green exit.",
                        "진화 완료: 재점화 확인 후 녹색 비상구로 대피하세요."), null, 4f);
                    return;
                }
                if (!_extinguishTimeout && ShouldGiveUp())
                {
                    _extinguishTimeout = true;
                    CurrentStage = Stage.Evacuating;
                    UnlockForEvacuation("危险增加：烟气/体力不支/超时，立即转为疏散");
                }
                return;
            }

            // ⑥ 疏散：提示路线（门是否解锁由原有逻辑兜底）
            if (CurrentStage == Stage.Evacuating)
            {
                if (!_evacuated)
                {
                    _evacuated = true;
                    if (exitDoor != null && !exitDoor.IsUnlocked) exitDoor.Unlock();
                    DrillToast.Show(DrillLocale.T(
                        "疏散：低姿弯腰，沿绿色安全出口标志撤离，不要乘电梯、不要折返取物。",
                        "Evacuate: stay low, follow the green exit signs, no lifts, no going back for belongings.",
                        "대피: 낮은 자세로 녹색 비상구를 따라 이동, 엘리베이터·물건 회수 금지."), null, 5f);
                }
                if (CheckAssembly()) CurrentStage = Stage.Reported;
                return;
            }

            // ⑦ 到达安全地点并报告
            if (CurrentStage == Stage.Reported && !_reportToast)
            {
                _reportToast = true;
                ReportAtAssembly();
            }
        }

        /// <summary>④ 判定：火源是否仍在初期规模、灭火器是否够近、玩家是否还有体力。</summary>
        void AssessInitialAttack()
        {
            var sc = scenario.CurrentScenario;
            var fire = sc != null ? sc.fire : null;
            var ext = sc != null ? sc.extinguisher : null;
            bool fireAlive = fire != null && fire.IsBurning && !fire.IsExtinguished;
            bool hpOk = vitals == null || vitals.currentHp > evacuateHealthThreshold;
            float dist = (fire != null && ext != null)
                ? Vector2.Distance(new Vector2(fire.transform.position.x, fire.transform.position.z),
                                   new Vector2(ext.transform.position.x, ext.transform.position.z))
                : 999f;
            bool nearEnough = dist <= initialAttackMaxDistance;
            bool powerOk = sc == null || !sc.requirePowerCut || (sc.powerSwitch != null && sc.powerSwitch.IsCut);

            // 未断电只是「附加提醒」，不阻断初期处置：电气情景会在取瓶/断电环节继续教
            CanAttemptInitial = fireAlive && hpOk && nearEnough;
            LastAssessment = CanAttemptInitial
                ? (powerOk
                    ? DrillLocale.T("火势处于初期、灭火器就在 " + dist.ToString("F1") + " 米内、身后有退路 → 可以尝试初期灭火。",
                                    "Incipient fire, extinguisher within " + dist.ToString("F1") + " m, escape route clear → initial attack allowed.",
                                    "초기 화재, 소화기 " + dist.ToString("F1") + " m 이내, 도주로 확보 → 초기 소화 가능.")
                    : DrillLocale.T("火势处于初期、灭火器在 " + dist.ToString("F1") + " 米内 → 可以尝试，但电气火必须先断电。",
                                    "Incipient fire, extinguisher within " + dist.ToString("F1") + " m — allowed, but cut the power first for electrical fires.",
                                    "초기 화재, 소화기 " + dist.ToString("F1") + " m 이내 → 가능하나 전기 화재는 전원 차단이 먼저."))
                : DrillLocale.T("不具备初期处置条件（火势/体力/器材距离）→ 优先疏散。",
                                "Conditions not met (fire size / stamina / distance) → evacuate first.",
                                "초기 소화 조건 미충족(화재 규모/체력/거리) → 대피 우선.");

            DrillToast.Show(LastAssessment, null, 5f);
            Debug.Log("[Flow] ④ 判定：" + (CanAttemptInitial ? "可尝试初期灭火" : "优先疏散")
                      + " 火距=" + dist.ToString("F2") + " 未断电=" + (sc != null && sc.requirePowerCut && !powerOk));
        }

        bool ShouldGiveUp()
        {
            if (vitals != null && vitals.currentHp <= evacuateHealthThreshold) return true;
            if (_extinguishStart > 0f && Time.time - _extinguishStart > maxExtinguishSeconds) return true;
            return false;
        }

        void UnlockForEvacuation(string reason)
        {
            Debug.Log("[Flow] ⑥ 转疏散：" + reason);
            DrillToast.Show(reason, null, 5f);
            if (exitDoor != null && !exitDoor.IsUnlocked) exitDoor.Unlock();
        }

        bool CheckAssembly()
        {
            var cam = Camera.main;
            if (cam == null) return false;
            Vector3 p = cam.transform.position;
            float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(assemblyPoint.x, assemblyPoint.z));
            if (d > assemblyRadius) return false;
            // 必须真的从门出去（门外侧），避免在门内误触发
            if (exitDoor != null && !exitDoor.IsUnlocked) return false;
            return true;
        }

        void ReportAtAssembly()
        {
            ReportedAtAssembly = true;
            DrillToast.Show(DrillLocale.T(
                "已到达安全地点：清点本部门人数，向现场负责人报告失联人员，原地等待消防到场。",
                "Reached the assembly point: count your team, report missing persons to the person in charge, wait for the fire service.",
                "안전 집결지 도착: 인원 점검 후 실종자를 책임자에게 보고하고 소방대를 기다리세요."), null, 6f);
            Debug.Log("[Flow] ⑦ 到达集合点并报告，流程完成");
        }

        /// <summary>自检/探针用：强制推进到某阶段（不改动原有灭火逻辑）。</summary>
        public void ForceStage(Stage stage) { CurrentStage = stage; }

        /// <summary>自检用：当前阶段的可读名称。</summary>
        public string StageName
        {
            get
            {
                switch (CurrentStage)
                {
                    case Stage.Discovered: return DrillLocale.T("发现火灾", "Fire discovered", "화재 발견");
                    case Stage.Alerted: return DrillLocale.T("警示与报告", "Alarm & report", "경보·신고");
                    case Stage.Assessed: return DrillLocale.T("判断初期处置", "Assess initial attack", "초기 대응 판단");
                    case Stage.Extinguishing: return DrillLocale.T("初期灭火", "Initial attack", "초기 소화");
                    case Stage.Evacuating: return DrillLocale.T("疏散", "Evacuation", "대피");
                    case Stage.Reported: return DrillLocale.T("到达安全地点并报告", "Reported at assembly", "집결지 보고");
                    default: return DrillLocale.T("未开始", "Idle", "대기");
                }
            }
        }
    }
}
