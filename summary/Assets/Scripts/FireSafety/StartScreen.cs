using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace FireSafety
{
    /// <summary>
    /// 开始界面（游戏标题 + 玩法说明文本框 + 开始按钮），纯代码生成世界空间 UI。
    ///   · 挂到任意空物体（建议命名为 StartScreen）。
    ///   · 玩法说明文字：改 Inspector 里的 Instructions 字段即可（默认内容为占位文案）。
    ///   · 点「开始演练」→ 火灾警报响起（作为背景音）+ 火焰点燃动画 + 进入演练流程。
    ///   · 自带入场/标题脉动/按钮呼吸等动画（UIFX）。
    /// </summary>
    public class StartScreen : MonoBehaviour
    {
        [Header("文案（玩法说明可在此替换）")]
        public string title = "办公室消防知识演练";

        [TextArea(6, 14)]
        public string instructions =
            "游戏玩法说明（这里是文本框，内容可在 Inspector 中修改补充）：\n" +
            "① 点击「开始演练」，火灾警报响起（背景音）；\n" +
            "② 先观察四周，找到红色灭火器并拾取它；\n" +
            "③ 拾取灭火器时会弹出 2~3 道灭火器常识问答，回答完毕后页面自动关闭；\n" +
            "④ 转身对准后方火源，拔出保险销、喷射灭火，火焰熄灭即演练成功。";

        public string buttonLabel = "开始演练";

        [Header("位置与比例")]
        public Vector3 worldPosition = new Vector3(0f, 1.3f, 1.8f);
        public float canvasScale = 0.001f;

        [Header("手柄锚点：开始界面显示在手柄正前方（留空则回退到相机前方）")]
        [Tooltip("绑定到右手控制器；非空时面板每帧跟随到其正前方")]
        public Transform anchorHand;
        [Tooltip("沿手柄前向伸出的距离（米），约一臂外的可读距离")]
        public float handForwardDistance = 1.0f;
        [Tooltip("相对手柄再向上抬的偏移（米），便于在视野下方居中阅读")]
        public float handUpOffset = 0.15f;

        [Header("开始流程")]
        [Tooltip("开始面板上的第二个按钮：打开分页操作教学")]
        public string onboardingLabel = "操作教学";
        [Tooltip("点击开始后拉响火灾警报")]
        public bool startAlarmOnBegin = true;
        [Tooltip("把背景音乐切换为警报音（BackgroundMusic.SwitchToAlarm）")]
        public bool switchBgmToAlarm = true;
        [Tooltip("警报响起时火焰从无到有点燃（缩放+灯光闪动动画）")]
        public bool igniteFiresOnBegin = true;

        [Header("动画参数")]
        public float panelFadeIn = 0.35f;
        public float panelScaleIn = 0.45f;
        public float titlePulseAmplitude = 0.035f;
        public float titlePulseFrequency = 1.1f;
        public float buttonPulseAmplitude = 0.03f;
        public float buttonPulseFrequency = 1.6f;

        public bool Started { get; private set; }

        public static StartScreen Instance { get; private set; }

        Canvas _canvas;
        CanvasGroup _panelGroup;
        RectTransform _panelRect;
        Button _startButton;
        Text _titleText;
        Text _instText;
        Text _startLabel;
        Transform _cam;
        AudioSource _alarmFallback;

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            BuildUI();
            PlayIntroAnimation();
        }

        void Update()
        {
            if (_cam == null) _cam = Camera.main ? Camera.main.transform : null;
            if (_canvas == null) return;

            // 优先跟随手柄锚点：面板显示在手柄正前方；锚点缺失时保持 worldPosition（桌面兜底）
            if (anchorHand != null)
            {
                Vector3 pos = anchorHand.position
                              + anchorHand.forward * handForwardDistance
                              + anchorHand.up * handUpOffset;
                _canvas.transform.position = pos;
            }

            // 让开始面板始终缓慢朝向玩家（仅在启动画面显示期间）
            if (!Started)
            {
                Transform look = _cam != null ? _cam : anchorHand;
                if (look != null)
                {
                    Quaternion target = Quaternion.LookRotation(_canvas.transform.position - look.position);
                    _canvas.transform.rotation = Quaternion.Slerp(_canvas.transform.rotation, target,
                        Time.deltaTime * 3f);
                }
            }
        }

        // ---------------- UI ----------------

        void BuildUI()
        {
            var go = new GameObject("Canvas_Start");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 300;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1000, 760);
            rt.localScale = Vector3.one * canvasScale;
            go.transform.position = worldPosition;
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
            // XR 手柄射线点按世界空间 UI 所需
            go.AddComponent<TrackedDeviceGraphicRaycaster>();

            // 背景面板
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image),
                typeof(VerticalLayoutGroup), typeof(CanvasGroup));
            panel.transform.SetParent(go.transform, false);
            var img = panel.GetComponent<Image>();
            img.color = new Color(0.05f, 0.07f, 0.12f, 0.94f);
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.padding = new RectOffset(28, 28, 28, 28);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            _panelRect = panel.GetComponent<RectTransform>();
            _panelRect.sizeDelta = new Vector2(940, 700);
            _panelRect.anchorMin = _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.anchoredPosition = Vector2.zero;
            _panelGroup = panel.GetComponent<CanvasGroup>();

            // 标题（带呼吸动画）
            _titleText = MakeText(panel.transform, title, 64, new Color(1f, 0.85f, 0.2f), 110f);
            UIFX.Pulse(this, _titleText.transform, 1f, titlePulseAmplitude, titlePulseFrequency);

            // 玩法说明文本框（可在 Inspector 的 Instructions 里修改）
            _instText = MakeText(panel.transform, instructions, 30, Color.white, 296f);
            _instText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _instText.verticalOverflow = VerticalWrapMode.Overflow;
            _instText.alignment = TextAnchor.UpperLeft;

            // 开始按钮（带呼吸动画）
            _startButton = MakeButton(panel.transform, buttonLabel, 40,
                new Color(0.12f, 0.55f, 0.30f, 1f), 100f);
            _startLabel = _startButton.GetComponentInChildren<Text>();
            UIFX.Pulse(this, _startButton.transform, 1f, buttonPulseAmplitude, buttonPulseFrequency);
            _startButton.onClick.AddListener(StartGame);

            // 引导页入口（온보딩）：分页教学
            var onboardBtn = MakeButton(panel.transform, onboardingLabel, 30,
                new Color(0.2f, 0.34f, 0.62f, 1f), 62f);
            onboardBtn.onClick.AddListener(OpenOnboarding);
        }

        Text MakeText(Transform parent, string content, int size, Color color, float height)
        {
            var t = new GameObject("Text", typeof(RectTransform), typeof(Text));
            t.transform.SetParent(parent, false);
            var text = t.GetComponent<Text>();
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.font = UIFont.Resolve();
            if (text.font != null) text.material = text.font.material;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            var le = t.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            var trt = t.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(trt.sizeDelta.x, height);
            return text;
        }

        Button MakeButton(Transform parent, string label, int size, Color bg, float height)
        {
            var b = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            b.transform.SetParent(parent, false);
            var img = b.GetComponent<Image>();
            img.color = bg;
            img.raycastTarget = true;
            var btn = b.GetComponent<Button>();
            btn.targetGraphic = img;
            var le = b.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            var brt = b.GetComponent<RectTransform>();
            brt.sizeDelta = new Vector2(brt.sizeDelta.x, height);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(b.transform, false);
            var t = labelGo.GetComponent<Text>();
            t.text = label;
            t.fontSize = size;
            t.color = Color.white;
            t.font = UIFont.Resolve();
            if (t.font != null) t.material = t.font.material;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            return btn;
        }

        void PlayIntroAnimation()
        {
            _panelGroup.alpha = 0f;
            UIFX.Fade(this, _panelGroup, 0f, 1f, panelFadeIn);
            UIFX.ScaleIn(this, _panelRect, 0.85f, 1f, panelScaleIn, 1.08f);
        }

        // ---------------- 开始 ----------------

        /// <summary>显示 / 隐藏开始画布（打开引导页时临时隐藏，避免按钮重叠误点）。</summary>
        public void SetVisible(bool visible)
        {
            if (_canvas == null) BuildUI();
            _canvas.gameObject.SetActive(visible);
        }

        /// <summary>「操作教学」按钮：打开分页引导页（온보딩 화면）。</summary>
        public void OpenOnboarding()
        {
            PlayClick();
            var onboarding = OnboardingScreen.Instance;
            if (onboarding == null) onboarding = FindObjectOfType<OnboardingScreen>();
            if (onboarding == null)
            {
                Debug.LogWarning("[StartScreen] 场景中没有 OnboardingScreen，操作教学不可用。");
                return;
            }
            onboarding.startScreen = this;
            onboarding.Show();
        }

        /// <summary>「开始演练」按钮回调。</summary>
        public void StartGame()
        {
            if (Started) return;
            Started = true;

            PlayClick();
            FireAlarm();
            SwitchBgm();
            if (igniteFiresOnBegin) IgniteFires();

            if (FireDrillManager.Instance != null)
                FireDrillManager.Instance.ForceCompleteUpTo(DrillStep.AlarmRinging);

            // 退出动画后隐藏
            StartCoroutine(ExitRoutine());
        }

        IEnumerator ExitRoutine()
        {
            UIFX.Fade(this, _panelGroup, 1f, 0f, 0.3f);
            yield return new WaitForSeconds(0.35f);
            _canvas.gameObject.SetActive(false);
        }

        // ---------------- 结算 ----------------

        /// <summary>阶段一「立刻结算」：重新显示开始面板，并改成结算 / 结束界面。</summary>
        public void ShowSettlement(string summary)
        {
            if (_canvas == null) BuildUI();
            Started = true;                       // 防止再走「开始演练」分支

            _canvas.gameObject.SetActive(true);
            PlaceInFrontOfCamera(1.25f);

            if (_titleText != null) _titleText.text = "演练结算";
            if (_instText != null)
            {
                _instText.text = summary;
                _instText.alignment = TextAnchor.UpperCenter;
            }
            if (_startLabel != null) _startLabel.text = "重新演练";

            _startButton.onClick.RemoveAllListeners();
            _startButton.onClick.AddListener(RestartDrill);

            PlayIntroAnimation();
            Debug.Log("[StartScreen] 已切换为结算面板。");
        }

        /// <summary>结算面板上的「重新演练」：重载当前场景，回到初始状态。</summary>
        public void RestartDrill()
        {
            PlayClick();
            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
            else SceneManager.LoadScene(scene.name);
        }

        void PlaceInFrontOfCamera(float distance)
        {
            Camera cam = Camera.main;
            if (cam == null || _canvas == null) return;
            var c = cam.transform;
            Vector3 pos = c.position + c.forward * distance + c.up * -0.05f;
            _canvas.transform.position = pos;
            _canvas.transform.rotation = Quaternion.LookRotation(pos - c.position, Vector3.up);
        }

        void FireAlarm()
        {
            if (!startAlarmOnBegin) return;
            var alarm = FindObjectOfType<FireAlarm>();
            if (alarm != null) { alarm.StartAlarm(); return; }

            // 兜底：场景里没有 FireAlarm 时，直接循环播放警报音（2D）
            var clip = Resources.Load<AudioClip>("Sounds/BGM/fire_alarm_siren");
            if (clip == null) return;
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.spatialBlend = 0f;
            src.volume = 0.55f;
            src.Play();
            _alarmFallback = src;
        }

        void SwitchBgm()
        {
            if (!switchBgmToAlarm) return;
            var bgm = FindObjectOfType<BackgroundMusic>();
            if (bgm != null) bgm.SwitchToAlarm();
        }

        /// <summary>火焰点燃动画：从无到有放大 + 灯光闪烁（“动画”功能的一部分）。</summary>
        void IgniteFires()
        {
            var fires = FindObjectsOfType<FireSource>();
            if (fires.Length == 0) return;
            foreach (var fire in fires)
                StartCoroutine(IgniteRoutine(fire));
        }

        IEnumerator IgniteRoutine(FireSource fire)
        {
            if (fire == null) yield break;
            var t = fire.transform;
            Vector3 target = t.localScale;
            t.localScale = target * 0.2f;

            var light = fire.GetComponent<Light>();
            if (light == null) light = fire.GetComponentInChildren<Light>();
            float homeIntensity = light != null ? light.intensity : 1f;
            if (light != null) light.intensity = 0f;

            float tm = 0f;
            const float dur = 0.9f;
            while (tm < dur)
            {
                tm += Time.deltaTime;
                float k = Mathf.Clamp01(tm / dur);
                float ease = 1f - Mathf.Pow(1f - k, 3f);
                t.localScale = Vector3.Lerp(target * 0.2f, target, ease);
                if (light != null) light.intensity = Mathf.Lerp(0f, homeIntensity, ease) * (0.6f + 0.4f * Mathf.Sin(k * 9f));
                yield return null;
            }
            t.localScale = target;
            if (light != null) light.intensity = homeIntensity;
        }

        void PlayClick()
        {
            var clip = Resources.Load<AudioClip>("Sounds/SFX/ui_click");
            if (clip != null) AudioSource.PlayClipAtPoint(clip, transform.position, 0.7f);
        }

        /// <summary>外部（编辑器/脚本）设置玩法说明文字。</summary>
        public void SetInstructions(string content)
        {
            instructions = content;
        }
    }
}
