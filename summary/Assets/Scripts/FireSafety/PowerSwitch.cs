using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FireSafety
{
    /// <summary>
    /// 电源接口（配电箱总闸）：电气设备起火时必须先切断电源，火焰才会解除锁定、可以被扑灭。
    ///   · 桌面：鼠标右键单击配电箱（DesktopDrillInput 射线选中）
    ///   · VR  ：右手射线指向配电箱，未持灭火器时按扳机
    /// 切断后：闸柄扳下、指示灯由红转绿、电弧火花熄灭、面板文字变为「电源已切断」。
    /// </summary>
    [DisallowMultipleComponent]
    public class PowerSwitch : MonoBehaviour
    {
        [Header("状态")]
        public bool cutForInspectorOnly = false;

        /// <summary>是否已切断（只允许切一次，切了不会再合闸）。</summary>
        public bool IsCut { get; private set; }

        [Header("文案")]
        public string switchName = "电源总闸";
        public string promptText = "电源总闸 · 待切断";
        public string cutText = "电源已切断 ✓";

        [Header("视觉（留空则由装配脚本补齐）")]
        [Tooltip("闸柄：切断时向下扳动")]
        public Transform lever;
        public float leverCutAngle = -42f;
        [Tooltip("指示灯：未断电红色，断电绿色")]
        public Light indicator;
        [Tooltip("电弧火花：切断后停止")]
        public ParticleSystem sparks;

        [Header("面板（世界空间，始终朝向相机）")]
        public Canvas labelCanvas;
        public Text labelText;

        [Header("音效")]
        public AudioSource audioSource;
        public AudioClip cutClip;

        [Header("交互")]
        [Tooltip("允许交互的最远距离（米）")]
        public float maxInteractDistance = 4f;

        [Header("事件")]
        public UnityEvent onCut = new UnityEvent();

        Transform _leverStart;
        float _nextFaceUpdate;
        bool _warnedTooFar;

        private void Awake()
        {
            if (lever != null) _leverStart = lever;
            ApplyVisualState();
        }

        private void Start()
        {
            ApplyVisualState();
        }

        private void LateUpdate()
        {
            FaceCamera();
        }

        /// <summary>切断电源。重复调用安全（幂等）。</summary>
        public bool Cut()
        {
            if (IsCut) return false;
            IsCut = true;

            if (lever != null)
                lever.localRotation = Quaternion.Euler(0f, 0f, leverCutAngle);

            if (indicator != null)
                indicator.color = new Color(0.25f, 1f, 0.35f);

            if (sparks != null)
                sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            if (labelText != null) labelText.text = cutText;

            if (audioSource != null && cutClip != null)
                audioSource.PlayOneShot(cutClip, 0.8f);

            Debug.Log("[Power] 电源已切断：" + switchName);
            onCut?.Invoke();
            return true;
        }

        /// <summary>输入层调用：距离内则切断；返回是否本次真的切断。</summary>
        public bool CutByInput(Transform source = null)
        {
            Vector3 from = source != null ? source.position : (Camera.main != null ? Camera.main.transform.position : transform.position);
            float dist = Vector3.Distance(from, transform.position);
            if (dist > maxInteractDistance)
            {
                if (!_warnedTooFar)
                {
                    _warnedTooFar = true;
                    Debug.Log("[Power] 距离电源箱 " + dist.ToString("F1") + " 米，太远（需 " + maxInteractDistance.ToString("F1") + " 米内）");
                }
                return false;
            }
            return Cut();
        }

        /// <summary>自检用：恢复未断电状态。</summary>
        public void ResetSwitchForTest()
        {
            IsCut = false;
            if (lever != null && _leverStart != null) lever.localRotation = _leverStart.localRotation;
            if (sparks != null) sparks.Clear();
            ApplyVisualState();
        }

        private void ApplyVisualState()
        {
            if (indicator != null) indicator.color = IsCut ? new Color(0.25f, 1f, 0.35f) : new Color(1f, 0.28f, 0.18f);
            if (labelText != null) labelText.text = IsCut ? cutText : promptText;
            if (lever != null) lever.localRotation = IsCut ? Quaternion.Euler(0f, 0f, leverCutAngle) : Quaternion.identity;
        }

        /// <summary>面板始终朝向相机，避免玩家走到侧面读不到字样。</summary>
        private void FaceCamera()
        {
            if (labelCanvas == null) return;
            if (Time.unscaledTime < _nextFaceUpdate) return;
            _nextFaceUpdate = Time.unscaledTime + 0.05f;

            Camera cam = Camera.main;
            if (cam == null) return;
            Vector3 from = cam.transform.position - labelCanvas.transform.position;
            if (from.sqrMagnitude < 0.0001f) return;
            labelCanvas.transform.rotation = Quaternion.LookRotation(-from.normalized, Vector3.up);
        }
    }
}
