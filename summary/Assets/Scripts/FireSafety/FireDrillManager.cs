using UnityEngine;
using UnityEngine.Events;

namespace FireSafety
{
    /// <summary>演练步骤枚举：拿起 → 拔销 → 按下压把 → 喷射 → 火焰扑灭。</summary>
    public enum DrillStep
    {
        None = 0,
        InFireScene = 1,        // ① 用户身处火灾现场
        AlarmRinging = 2,       // ② 火灾警报响起
        FireLocated = 3,        // ③ 用户确认起火位置
        ReachedExtinguisher = 4,// ④ 移动到灭火器处
        ExtinguisherGrabbed = 5,// ⑤ 拿起灭火器
        PinPulled = 6,          // ⑥ 拔出保险销
        HandlePressed = 7,      // ⑦ 按下压把
        Spraying = 8,           // ⑧ 对准火源喷射
        FireExtinguished = 9    // ⑨ 火焰扑灭
    }

    /// <summary>全局演练状态机：所有子系统只负责“上报完成”，由它统一推进并广播。</summary>
    public class FireDrillManager : MonoBehaviour
    {
        public static FireDrillManager Instance { get; private set; }

        /// <summary>流程最后一步，UI 与完成判定都以此为准。</summary>
        public static readonly DrillStep LastStep = DrillStep.FireExtinguished;

        public static readonly string[] StepNames =
        {
            "未开始", "① 身处火灾现场", "② 火灾警报响起", "③ 确认起火位置",
            "④ 移动到灭火器处", "⑤ 拿起灭火器", "⑥ 拔出保险销",
            "⑦ 按下压把", "⑧ 对准火源喷射", "⑨ 火焰扑灭"
        };

        public static readonly string[] StepHints =
        {
            "演练即将开始",
            "保持冷静，留意警报声与红色闪烁灯",
            "请环视四周，看向火焰，确认起火点",
            "沿安全通道走到红色灭火器旁",
            "用手柄扣动 Grip 键握住灭火器（再按一次才松开）",
            "用另一只手握住保险销，向外拉出",
            "保险销已拔出！用另一只手按向红色压把",
            "压把已按下！扣住扳机不放，对准火焰根部",
            "正在喷射！持续对准火焰根部，直到火焰熄灭",
            "火焰已扑灭，演练完成！"
        };

        [Header("运行时状态（只读）")]
        [SerializeField] private DrillStep currentStep = DrillStep.None;

        [Header("是否强制 1→9 顺序推进（关闭则可跳步）")]
        public bool enforceOrder = true;

        [Header("事件")]
        public UnityEvent<DrillStep> onStepChanged = new UnityEvent<DrillStep>();
        public UnityEvent onDrillCompleted = new UnityEvent();

        public DrillStep CurrentStep => currentStep;
        public bool IsCompleted => currentStep == LastStep;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        private void Start()
        {
            // ① 进入场景即视为“身处火灾现场”
            CompleteStep(DrillStep.InFireScene);
        }

        /// <summary>上报完成某一步；顺序不对或重复上报会被忽略。</summary>
        public bool CompleteStep(DrillStep step)
        {
            if (step == DrillStep.None) return false;
            if (IsCompleted) return false;
            if (step <= currentStep) return false;

            if (enforceOrder && step != Next(currentStep))
            {
                Debug.LogWarning($"[FireDrill] 步骤跳跃：当前 {currentStep}，试图完成 {step}。可关闭 enforceOrder。");
                return false;
            }

            currentStep = step;
            Debug.Log($"[FireDrill] ✔ {StepNames[(int)step]}");
            onStepChanged?.Invoke(step);
            if (IsCompleted) onDrillCompleted?.Invoke();
            return true;
        }

        /// <summary>兜底推进：把 currentStep 之后直到 step 的步骤补齐（拿起时自动补“已到达”）。</summary>
        public bool ForceCompleteUpTo(DrillStep step)
        {
            if (step <= currentStep) return false;
            while (currentStep < step)
            {
                DrillStep next = Next(currentStep);
                if (!CompleteStep(next)) return false;
            }
            return true;
        }

        public bool IsStepDone(DrillStep step) => currentStep >= step;

        private static DrillStep Next(DrillStep s) => (DrillStep)((int)s + 1);
    }
}