using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace FireSafety
{
    /// <summary>
    /// 演练总时长倒计时：点击「开始演练」（步骤推进到 AlarmRinging）后自动启动。
    /// 时长上限 3 分钟（180 秒），到点判定演练失败；演练提前完成则冻结显示「演练完成」。
    /// 世界空间 HUD 跟随头显，不阻挡手柄射线点按其他 UI。
    /// </summary>
    public class DrillCountdown : MonoBehaviour
    {
        public static DrillCountdown Instance { get; private set; }

        [Header("时长（秒，上限 180 秒 = 3 分钟）")]
        [Range(30f, 180f)] public float durationSeconds = 180f;

        [Header("提醒阈值（秒）")]
        public float warningSeconds = 30f;
        public float dangerSeconds = 10f;

        [Header("HUD 位置（相对头显的局部偏移）")]
        public Vector3 hudOffset = new Vector3(0f, 0.35f, 0.6f);
        public float canvasScale = 0.0035f;

        [Header("事件")]
        public UnityEvent onTimeExpired;
        public UnityEvent onDrillFinished;

        public bool IsRunning { get; private set; }
        public bool IsExpired { get; private set; }
        public bool IsFinished { get; private set; }
        public float Remaining { get; private set; }

        Canvas _canvas;
        CanvasGroup _group;
        Text _text;
        Camera _cam;
        AudioSource _src;
        int _lastWholeSecond = -1;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            Remaining = Mathf.Min(180f, durationSeconds);
        }

        private void OnDisable()
        {
            UnsubscribeManager();
        }

        private void Start()
        {
            BuildHud();
            _cam = Camera.main;
            if (_cam == null)
            {
                var camGo = GameObject.FindGameObjectWithTag("MainCamera");
                if (camGo) _cam = camGo.GetComponent<Camera>();
            }

            _src = GetComponent<AudioSource>();
            if (_src == null) _src = gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;

            SubscribeManager();
            SetText(Format(Remaining), Color.white);
        }

        private void SubscribeManager()
        {
            var mgr = FireDrillManager.Instance;
            if (mgr == null) return;
            mgr.onStepChanged.AddListener(OnStepChanged);
            mgr.onDrillCompleted.AddListener(OnCompleted);
        }

        private void UnsubscribeManager()
        {
            var mgr = FireDrillManager.Instance;
            if (mgr == null) return;
            mgr.onStepChanged.RemoveListener(OnStepChanged);
            mgr.onDrillCompleted.RemoveListener(OnCompleted);
        }

        private void OnStepChanged(DrillStep step)
        {
            if (step == DrillStep.AlarmRinging) Begin();
        }

        /// <summary>外部（按钮/编辑器）直接启动倒计时。</summary>
        public void Begin()
        {
            if (IsRunning || IsExpired || IsFinished) return;
            Remaining = Mathf.Min(180f, durationSeconds);
            IsRunning = true;
            _lastWholeSecond = -1;
            if (_group) _group.alpha = 1f;
            Debug.Log("[Countdown] 演练倒计时开始：" + Format(Remaining));
        }

        private void Update()
        {
            if (_cam == null) _cam = Camera.main;

            // HUD 跟随头显并缓慢朝向玩家
            if (_canvas != null && _cam != null)
            {
                _canvas.transform.position = _cam.transform.position + _cam.transform.rotation * hudOffset;
                Quaternion target = Quaternion.LookRotation(_canvas.transform.position - _cam.transform.position);
                _canvas.transform.rotation = Quaternion.Slerp(_canvas.transform.rotation, target, Time.deltaTime * 6f);
            }

            if (!IsRunning) return;

            Remaining -= Time.deltaTime;
            if (Remaining <= dangerSeconds) TickPerSecond();

            if (Remaining <= 0f)
            {
                Remaining = 0f;
                Expire();
                return;
            }

            RefreshText();
        }

        private void RefreshText()
        {
            Color c;
            if (Remaining > warningSeconds) c = Color.white;
            else if (Remaining > dangerSeconds) c = new Color(1f, 0.72f, 0.2f);
            else
            {
                // 危险区间：红色呼吸闪烁
                float k = Mathf.Sin(Time.time * Mathf.PI * 6f) * 0.5f + 0.5f;
                c = Color.Lerp(new Color(0.6f, 0.08f, 0.08f), Color.red, k);
            }
            SetText(Format(Remaining), c, 56);
        }

        private void TickPerSecond()
        {
            int whole = Mathf.CeilToInt(Remaining);
            if (whole == _lastWholeSecond) return;
            _lastWholeSecond = whole;

            var clip = Resources.Load<AudioClip>("Sounds/SFX/ui_click");
            if (clip && _src) _src.PlayOneShot(clip, 0.25f);
        }

        private void Expire()
        {
            IsRunning = false;
            IsExpired = true;
            SetText("时间到 · 演练失败", Color.red, 38);

            var clip = Resources.Load<AudioClip>("Sounds/SFX/fail_buzz");
            if (clip && _src) _src.PlayOneShot(clip, 0.8f);

            onTimeExpired?.Invoke();
            Debug.Log("[Countdown] 倒计时结束，演练失败。");
        }

        private void OnCompleted()
        {
            if (IsExpired) return;
            IsFinished = true;
            IsRunning = false;
            SetText("演练完成", new Color(0.35f, 0.9f, 0.45f), 44);
            onDrillFinished?.Invoke();
            Debug.Log("[Countdown] 演练提前完成，倒计时冻结。");
        }

        // ---------------- HUD 构建 ----------------

        private void BuildHud()
        {
            var go = new GameObject("Canvas_Countdown");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 250;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(420, 140);
            rt.localScale = Vector3.one * canvasScale;
            go.AddComponent<CanvasScaler>();

            _group = go.AddComponent<CanvasGroup>();
            _group.alpha = 1f;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            panel.transform.SetParent(go.transform, false);
            var img = panel.GetComponent<Image>();
            img.color = new Color(0.05f, 0.07f, 0.12f, 0.72f);
            var prt = panel.GetComponent<RectTransform>();
            prt.sizeDelta = new Vector2(400, 120);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;

            var textGo = new GameObject("TimeText", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(panel.transform, false);
            _text = textGo.GetComponent<Text>();
            _text.font = UIFont.Resolve();
            if (_text.font != null) _text.material = _text.font.material;
            _text.fontSize = 56;
            _text.alignment = TextAnchor.MiddleCenter;
            _text.raycastTarget = false;
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
        }

        private void SetText(string content, Color color, int fontSize = 56)
        {
            if (_text == null) return;
            _text.text = content;
            _text.color = color;
            _text.fontSize = fontSize;
        }

        private static string Format(float seconds)
        {
            int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            int m = s / 60;
            int r = s % 60;
            return $"{m:00}:{r:00}";
        }
    }
}
