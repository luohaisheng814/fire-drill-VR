using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 第 41 轮：手动火灾报警按钮（流程第 ③ 步「警示与报告」）。
    /// 走廊里一个红色立柱，玩家靠近并注视约 0.8 秒即视为按下：
    /// 拉响 FireAlarm、提示已向 119 报警，并把流程推到 Alerted。
    /// </summary>
    public class AlarmCallButton : MonoBehaviour
    {
        public static AlarmCallButton Instance { get; private set; }

        [Header("判定")]
        [Tooltip("玩家进入这个距离内才可能按下（米）")]
        public float engageDistance = 1.6f;
        [Tooltip("视线与按钮夹角小于该角度才算「看着按钮」（度）")]
        public float lookAngle = 32f;
        [Tooltip("需要注视多久才按下（秒）")]
        public float dwellSeconds = 0.8f;
        [Tooltip("留空则用 Camera.main")]
        public Transform player;

        [Header("表现")]
        public FireAlarm alarm;
        public AudioSource clickSource;
        public AudioClip clickClip;
        public Color idleColor = new Color(0.62f, 0.10f, 0.08f);
        public Color pressedColor = new Color(0.12f, 0.72f, 0.34f);

        public bool Triggered { get; private set; }
        public float DwellProgress => Triggered ? 1f : Mathf.Clamp01(_dwell / Mathf.Max(0.01f, dwellSeconds));
        public float PlayerDistance { get; private set; } = float.PositiveInfinity;

        [SerializeField] Renderer _renderer;
        float _dwell;
        float _dwellStart = -1f;
        [SerializeField] Transform _plate;

        void Awake()
        {
            Instance = this;
            // 场景序列化会丢私有引用，运行时按名字兜底找回来（否则面板高度算错、注视判定失真）
            if (_plate == null)
            {
                var t = transform.Find("AlarmPlate");
                if (t != null) _plate = t;
            }
            if (_renderer == null && _plate != null) _renderer = _plate.GetComponent<Renderer>();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        public void ConfigureVisual(Transform plate, Renderer body)
        {
            _plate = plate;
            _renderer = body;
            if (_renderer != null && _renderer.material != null)
                _renderer.material.color = idleColor;
        }

        void Update()
        {
            if (Triggered) return;
            var cam = player != null ? player : (Camera.main != null ? Camera.main.transform : null);
            if (cam == null) return;

            Vector3 target = _plate != null ? _plate.position : transform.position;
            PlayerDistance = Vector3.Distance(cam.position, target);

            bool looking = false;
            if (PlayerDistance <= engageDistance)
            {
                Vector3 dir = (target - cam.position).normalized;
                looking = Vector3.Angle(cam.forward, dir) <= lookAngle;
            }

            if (looking)
            {
                if (_dwellStart < 0f) _dwellStart = Time.realtimeSinceStartup;
                _dwell = Time.realtimeSinceStartup - _dwellStart;   // 用真实时间，避免 deltaTime 在某些运行模式下为 0
            }
            else
            {
                _dwellStart = -1f;
                _dwell = 0f;
            }

            if (_dwell >= dwellSeconds) PressByScript();
        }

        /// <summary>按下按钮：拉响警报并记录「已报警」。</summary>
        public void PressByScript()
        {
            if (Triggered) return;
            Triggered = true;

            if (alarm == null) alarm = FindObjectOfType<FireAlarm>();
            if (alarm != null) alarm.StartAlarm();

            if (clickSource != null)
            {
                if (clickClip == null) clickClip = Resources.Load<AudioClip>("Sounds/SFX/ui_click");
                if (clickClip != null) clickSource.PlayOneShot(clickClip);
            }

            if (_renderer != null && _renderer.material != null)
                _renderer.material.color = pressedColor;

            DrillToast.Show(DrillLocale.T(
                "已按下手动报警按钮：警报响起，同时拨打 119 报告起火位置与火势。",
                "Manual call point pressed: alarm sounding — also dial 119 with location and fire size.",
                "수동 발신기 작동: 경보가 울립니다. 119에 위치와 화재 규모를 신고하세요."), null, 4.0f);
            Debug.Log("[AlarmButton] 手动报警按钮已按下");
        }

        public void ResetButton()
        {
            Triggered = false;
            _dwell = 0f;
            _dwellStart = -1f;
            if (_renderer != null && _renderer.material != null)
                _renderer.material.color = idleColor;
        }
    }
}


