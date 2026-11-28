using System;
using UnityEngine;
using UnityEngine.UI;

namespace FireSafety
{
    /// <summary>
    /// 第 38 轮：玩家生命值（20 点）。
    ///   · 烟雾区中每 10 秒扣 1 点；
    ///   · 进入火源危险半径立即烧伤 -1，并弹出警告；继续停留每 2 秒再 -1；
    ///   · 生命值归零弹出失败结束页。
    /// HUD 是相机前方的世界空间小面板，跟随头部，不挡问答面板中心。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerVitals : MonoBehaviour
    {
        public static PlayerVitals Instance { get; private set; }

        [Header("生命值")]
        public int maxHp = 20;
        public int currentHp = 20;
        [Tooltip("生命值归零时是否弹出失败结算页（自检时可关掉，避免清场）")]
        public bool showFailureOnZero = true;

        [Header("烟雾伤害")]
        [Tooltip("在烟雾中每多少秒扣 1 点")]
        public float smokeDamageInterval = 10f;

        [Header("火源烧伤")]
        [Tooltip("玩家水平位置进入该半径内即视为离火太近")]
        public float fireDangerRadius = 1.55f;
        [Tooltip("首次烧伤后，继续停留每隔多少秒再扣 1 点")]
        public float fireRepeatInterval = 2f;

        [Header("HUD（跟随头部）")]
        public Transform head;
        public float distance = 0.78f;
        public float rightOffset = -0.30f;
        public float downOffset = -0.30f;
        public float followSpeed = 10f;
        public float canvasScale = 0.00052f;

        /// <summary>当前是否在烟雾区。</summary>
        public bool InSmoke { get; private set; }
        /// <summary>当前是否在火源危险半径内。</summary>
        public bool NearFire { get; private set; }
        /// <summary>到最近一处正在燃烧火源的水平距离（没有火源时为 +∞）。</summary>
        public float NearestFireDistance { get; private set; } = float.PositiveInfinity;
        /// <summary>烟雾伤害计时（0~10），探针断言用。</summary>
        public float SmokeTimer => _smokeTimer;
        /// <summary>距离下一次持续烧伤还剩多少秒。</summary>
        public float FireRepeatLeft => Mathf.Max(0f, _fireTick);
        public int SmokeDamageCount { get; private set; }
        public int FireDamageCount { get; private set; }
        public bool IsDead { get; private set; }

        /// <summary>每扣一次血广播（amount, reason: "smoke"/"fire"）。</summary>
        public event Action<int, string> onDamaged;
        public event Action onDied;

        Canvas _canvas;
        CanvasGroup _group;
        Text _valueText;
        Text _statusText;
        Image _fill;
        RectTransform _fillRect;
        float _fillWidth;
        Transform _head;
        float _smokeTimer;
        float _fireTick;
        bool _wasNearFire;
        bool _wasInSmoke;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            currentHp = Mathf.Clamp(currentHp, 0, Mathf.Max(1, maxHp));
            BuildUI();
        }

        private void Start()
        {
            ResolveHead();
            RefreshHud();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_canvas != null)
            {
                if (Application.isPlaying) Destroy(_canvas.gameObject);
                else DestroyImmediate(_canvas.gameObject);
            }
        }

        private void Update()
        {
            if (IsDead) return;
            Evaluate(Time.deltaTime);
        }

        private void LateUpdate()
        {
            FollowHead();
        }

        /// <summary>正常运行时每帧调用；也供自检 / 探针按固定步长驱动。</summary>
        public void Evaluate(float dt)
        {
            if (IsDead) return;
            ResolveHead();
            if (_head == null) return;

            EvaluateAt(_head.position, dt, out bool inSmoke, out bool nearFire, out float fireDist);
            InSmoke = inSmoke;
            NearFire = nearFire;
            NearestFireDistance = fireDist;
            ApplyTimers(dt, inSmoke, nearFire);
            RefreshHud();
        }

        /// <summary>用指定玩家位置跑一帧（探针可把玩家瞬移到烟雾里/火边后直接断言）。</summary>
        public void EvaluateAtPosition(Vector3 playerPos, float dt)
        {
            if (IsDead) return;
            EvaluateAt(playerPos, dt, out bool inSmoke, out bool nearFire, out float fireDist);
            InSmoke = inSmoke;
            NearFire = nearFire;
            NearestFireDistance = fireDist;
            ApplyTimers(dt, inSmoke, nearFire);
            RefreshHud();
        }

        /// <summary>逻辑自检：直接喂「是否在烟雾 / 是否靠火」信号，不依赖场景物理对象。</summary>
        public void EvaluateSignalsForTest(float dt, bool inSmoke, bool nearFire)
        {
            if (IsDead) return;
            InSmoke = inSmoke;
            NearFire = nearFire;
            ApplyTimers(dt, inSmoke, nearFire);
            RefreshHud();
        }

        void EvaluateAt(Vector3 pos, float dt, out bool inSmoke, out bool nearFire, out float fireDist)
        {
            inSmoke = false;
            var zones = SmokeZone.All;
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                if (z != null && z.ContainsPlayer(pos)) { inSmoke = true; break; }
            }

            fireDist = float.PositiveInfinity;
            var fires = FireSource.Active;
            for (int i = fires.Count - 1; i >= 0; i--)
            {
                var f = fires[i];
                if (f == null || !f.isActiveAndEnabled || !f.IsBurning || f.IsExtinguished) continue;
                Vector3 aim = f.AimPoint;
                float d = new Vector2(pos.x - aim.x, pos.z - aim.z).magnitude;
                if (d < fireDist) fireDist = d;
            }
            nearFire = fireDist <= Mathf.Max(0.1f, fireDangerRadius);
        }

        void ApplyTimers(float dt, bool inSmoke, bool nearFire)
        {
            if (dt <= 0f) return;

            // 烟雾：进入后累计，满 10 秒扣 1；离开烟雾清零
            if (inSmoke)
            {
                if (!_wasInSmoke) _smokeTimer = 0f;
                _smokeTimer += dt;
                while (_smokeTimer >= Mathf.Max(0.1f, smokeDamageInterval))
                {
                    _smokeTimer -= Mathf.Max(0.1f, smokeDamageInterval);
                    SmokeDamageCount++;
                    Damage(1, "smoke");
                    if (IsDead) return;
                }
            }
            else
            {
                _smokeTimer = 0f;
            }
            _wasInSmoke = inSmoke;

            // 火源：刚进入危险半径先扣 1，随后每 fireRepeatInterval 秒再扣 1
            if (nearFire)
            {
                if (!_wasNearFire)
                {
                    _fireTick = Mathf.Max(0.1f, fireRepeatInterval);
                    FireDamageCount++;
                    Damage(1, "fire-first");
                    if (IsDead) return;
                }
                else
                {
                    _fireTick -= dt;
                    if (_fireTick <= 0f)
                    {
                        _fireTick = Mathf.Max(0.1f, fireRepeatInterval);
                        FireDamageCount++;
                        Damage(1, "fire-repeat");
                        if (IsDead) return;
                    }
                }
            }
            else
            {
                _fireTick = 0f;
            }
            _wasNearFire = nearFire;
        }

        /// <summary>直接扣血（自检 / 外部脚本也可调用）。</summary>
        public void Damage(int amount, string reason = "manual")
        {
            if (IsDead || amount <= 0) return;
            currentHp = Mathf.Max(0, currentHp - amount);
            LastDamageReason = reason;

            if (string.Equals(reason, "smoke", StringComparison.Ordinal))
            {
                DrillToast.Show("烟雾吸入：生命值 -1（在烟雾中每 10 秒扣 1）",
                    "Smoke exposure: -1 HP every 10 seconds", 3.4f);
            }
            else if (string.Equals(reason, "fire-first", StringComparison.Ordinal))
            {
                DrillToast.Show("警告：离火源太近，已烧伤 -1 生命值；立即后退！",
                    "Warning: too close to the fire, -1 HP. Step back!", 3.6f);
            }
            else if (string.Equals(reason, "fire-repeat", StringComparison.Ordinal))
            {
                DrillToast.Show("仍在火源附近，持续烧伤 -1 生命值",
                    "Still too close to the fire: -1 HP", 2.4f);
            }

            onDamaged?.Invoke(amount, reason);
            RefreshHud();
            if (currentHp <= 0) Die();
        }

        public string LastDamageReason { get; private set; } = "";

        void Die()
        {
            IsDead = true;
            InSmoke = false;
            NearFire = false;
            DrillToast.Show("生命值耗尽，演练失败", "HP depleted — drill failed", 4f);
            onDied?.Invoke();
            var ending = showFailureOnZero
                ? (EndingScreen.Instance != null ? EndingScreen.Instance : FindObjectOfType<EndingScreen>())
                : null;
            if (ending != null && !ending.IsOpen) ending.Show(false, "生命值耗尽");
            Debug.Log("[Vitals] 生命值归零，演练失败");
        }

        /// <summary>教学/开始面板阅读期间隐藏生命值条，避免压在正文上。</summary>
        public void SetHudVisible(bool visible)
        {
            if (_canvas != null) _canvas.gameObject.SetActive(visible);
            else if (!visible && _valueText != null && _valueText.transform.root != null)
                _valueText.transform.root.gameObject.SetActive(false);
        }

        /// <summary>重开演练用：恢复 20 点并把计时清零。</summary>
        public void ResetVitals()
        {
            IsDead = false;
            currentHp = Mathf.Clamp(maxHp, 1, 999);
            _smokeTimer = 0f;
            _fireTick = 0f;
            _wasNearFire = false;
            _wasInSmoke = false;
            SmokeDamageCount = 0;
            FireDamageCount = 0;
            LastDamageReason = "";
            RefreshHud();
        }

        void ResolveHead()
        {
            if (head == null)
            {
                var cam = Camera.main;
                if (cam != null) head = cam.transform;
            }
            _head = head;
        }

        void FollowHead()
        {
            if (_canvas == null) return;
            ResolveHead();
            if (head == null) return;
            Vector3 target = head.position + head.forward * distance
                             + head.right * rightOffset + head.up * downOffset;
            float k = 1f - Mathf.Exp(-Mathf.Max(0.1f, followSpeed) * Time.deltaTime);
            _canvas.transform.position = Vector3.Lerp(_canvas.transform.position, target, k);
            Vector3 look = _canvas.transform.position - head.position;
            if (look.sqrMagnitude > 0.0001f)
            {
                Quaternion desired = Quaternion.LookRotation(look.normalized, Vector3.up);
                _canvas.transform.rotation = Quaternion.Slerp(_canvas.transform.rotation, desired, k);
            }
        }

        void RefreshHud()
        {
            if (_valueText == null) return;
            _valueText.text = currentHp.ToString("00") + " / " + maxHp.ToString("00");

            float ratio = (float)currentHp / Mathf.Max(1, maxHp);
            if (_fill != null)
                _fill.color = ratio > 0.55f ? new Color(0.28f, 0.86f, 0.45f)
                    : (ratio > 0.25f ? new Color(1f, 0.72f, 0.22f) : new Color(0.95f, 0.26f, 0.22f));
            if (_fillRect != null && _fillWidth > 0f)
                _fillRect.sizeDelta = new Vector2(Mathf.Max(2f, _fillWidth * ratio), _fillRect.sizeDelta.y);

            if (_statusText != null)
            {
                if (IsDead) _statusText.text = "生命值耗尽 · 演练失败";
                else if (NearFire) _statusText.text = "火源过近 · 持续烧伤（" + FireRepeatLeft.ToString("0.0") + " 秒后 -1）";
                else if (InSmoke) _statusText.text = "烟雾中 · " + (smokeDamageInterval - _smokeTimer).ToString("0.0") + " 秒后 -1";
                else _statusText.text = "状态正常 · 远离烟雾与火源";
                _statusText.color = IsDead || NearFire
                    ? new Color(1f, 0.42f, 0.32f)
                    : (InSmoke ? new Color(1f, 0.78f, 0.34f) : new Color(0.68f, 0.82f, 0.92f));
            }
        }

        // ---------------- UI ----------------

        void BuildUI()
        {
            if (_canvas != null) return;
            var go = new GameObject("Canvas_PlayerVitals", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 310;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(560f, 132f);
            rt.localScale = Vector3.one * canvasScale;
            _group = go.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var bg = new GameObject("BG", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(go.transform, false);
            var bgImg = bg.GetComponent<Image>();
            bgImg.color = new Color(0.03f, 0.06f, 0.09f, 0.88f);
            bgImg.raycastTarget = false;
            Stretch(bg.GetComponent<RectTransform>());

            var title = MakeText(go.transform, "生命值", 24, new Color(0.75f, 0.92f, 1f),
                new Vector2(220f, 32f), new Vector2(16f, -8f), TextAnchor.MiddleLeft);
            title.raycastTarget = false;

            _valueText = MakeText(go.transform, "20 / 20", 26, Color.white,
                new Vector2(180f, 32f), new Vector2(-16f, 8f), TextAnchor.MiddleRight);

            var barBg = new GameObject("BarBG", typeof(RectTransform), typeof(Image));
            barBg.transform.SetParent(go.transform, false);
            var barImg = barBg.GetComponent<Image>();
            barImg.color = new Color(0.12f, 0.18f, 0.16f, 1f);
            barImg.raycastTarget = false;
            var brt = barBg.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 1f);
            brt.anchorMax = new Vector2(0f, 1f);
            brt.pivot = new Vector2(0f, 1f);
            brt.anchoredPosition = new Vector2(14f, -42f);
            _fillWidth = 532f;
            brt.sizeDelta = new Vector2(_fillWidth, 24f);

            var fill = new GameObject("BarFill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(barBg.transform, false);
            _fill = fill.GetComponent<Image>();
            _fill.color = new Color(0.28f, 0.86f, 0.45f);
            _fill.raycastTarget = false;
            _fillRect = fill.GetComponent<RectTransform>();
            _fillRect.anchorMin = new Vector2(0f, 0f);
            _fillRect.anchorMax = new Vector2(0f, 1f);
            _fillRect.pivot = new Vector2(0f, 0.5f);
            _fillRect.anchoredPosition = Vector2.zero;
            _fillRect.sizeDelta = new Vector2(_fillWidth, 0f);

            _statusText = MakeText(go.transform, "状态正常 · 远离烟雾与火源", 20,
                new Color(0.68f, 0.82f, 0.92f), new Vector2(532f, 28f),
                new Vector2(14f, -74f), TextAnchor.MiddleLeft);
        }

        static Text MakeText(Transform parent, string content, int size, Color color,
            Vector2 sizeDelta, Vector2 anchoredPos, TextAnchor anchor)
        {
            var t = new GameObject("Text", typeof(RectTransform), typeof(Text));
            t.transform.SetParent(parent, false);
            var text = t.GetComponent<Text>();
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.font = UIFont.Resolve();
            if (text.font != null) text.material = text.font.material;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            var rt = t.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = anchoredPos;
            return rt.GetComponent<Text>();
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
