using TMPro;
using UnityEngine;

namespace FireSafety
{
    /// <summary>演练提示 UI：显示当前步骤、下一步提示与临时 Toast。挂到 World Space Canvas。</summary>
    public class DrillUIPresenter : MonoBehaviour
    {
        [Header("引用")]
        public FireDrillManager manager;
        public TMP_Text stepText;
        public TMP_Text hintText;

        [Header("行为")]
        [Tooltip("勾选后 Canvas 始终缓慢朝向玩家（头显）")]
        public bool billboardToCamera = true;
        public float billboardLerp = 4f;
        public float toastDuration = 2.5f;

        private float _toastTimer;
        private Camera _cam;

        private void Awake()
        {
            if (!manager) manager = FireDrillManager.Instance;
            if (!_cam) _cam = Camera.main;
        }

        private void OnEnable()
        {
            if (!manager) manager = FireDrillManager.Instance;
            if (!manager) return;
            manager.onStepChanged.AddListener(OnStepChanged);
            manager.onDrillCompleted.AddListener(OnCompleted);
        }

        private void OnDisable()
        {
            if (!manager) return;
            manager.onStepChanged.RemoveListener(OnStepChanged);
            manager.onDrillCompleted.RemoveListener(OnCompleted);
        }

        private void Start()
        {
            Refresh(DrillStep.None);
            if (manager) Refresh(manager.CurrentStep);
        }

        private void Update()
        {
            if (!_cam) _cam = Camera.main;

            if (billboardToCamera && _cam)
            {
                Quaternion target = Quaternion.LookRotation(transform.position - _cam.transform.position);
                transform.rotation = Quaternion.Slerp(transform.rotation, target, Time.deltaTime * billboardLerp);
            }

            if (_toastTimer > 0f)
            {
                _toastTimer -= Time.deltaTime;
                if (_toastTimer <= 0f && manager) Refresh(manager.CurrentStep);
            }
        }

        private void OnStepChanged(DrillStep step) => Refresh(step);

        private void OnCompleted() => ShowToast("演练完成：拿起 → 拔销 → 压下压把 → 喷射，火焰已扑灭！", 6f);

        private void Refresh(DrillStep step)
        {
            int i = (int)step;
            int last = (int)FireDrillManager.LastStep;
            if (stepText)
            {
                string done = step == FireDrillManager.LastStep ? "（已完成）" : "";
                string next = i < last ? FireDrillManager.StepNames[i + 1] : "—";
                stepText.text = $"{FireDrillManager.StepNames[i]} {done}\n下一步：{next}";
            }
            if (hintText)
                hintText.text = FireDrillManager.StepHints[Mathf.Min(i + 1, last)];
        }

        /// <summary>显示一条临时提示（几秒后自动恢复为当前步骤提示）。</summary>
        public void ShowToast(string message, float duration = -1f)
        {
            if (hintText) hintText.text = message;
            _toastTimer = duration > 0f ? duration : toastDuration;
        }
    }
}