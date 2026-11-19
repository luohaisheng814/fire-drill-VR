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

        [Header("English / 英文")]
        public string titleEnglish = "Office Fire Safety Drill";
        [TextArea(6, 14)]
        public string instructionsEnglish =
            "How to play:\n" +
            "1. Click Start Drill. The fire alarm will sound.\n" +
            "2. Find the extinguisher on the wall and pick it up.\n" +
            "3. Answer the extinguisher quiz (3 of 5 correct to pass).\n" +
            "4. Face the fire, pull the pin, squeeze the lever, and sweep at the base of the flame.";
        public string buttonLabelEnglish = "START DRILL";
        public string tutorialLabelEnglish = "Practice";
        public string onboardingLabelEnglish = "Tutorial";

        [Header("操作演练入口（原第一阶段：拿瓶→答题→拔销→压把→喷射）")]
        [Tooltip("开始面板上的第三个按钮：进入可实操的操作演练")]
        public string tutorialLabel = "操作演练";
        public bool showTutorialButton = true;

        [Header("位置与比例")]
        public Vector3 worldPosition = new Vector3(0f, 1.3f, 1.8f);
        public float canvasScale = 0.001f;

        [Header("手柄锚点：开始界面显示在手柄正前方（留空则回退到相机前方）")]
        [Tooltip("绑定到右手控制器；非空时面板每帧跟随到其正前方")]
        public Transform anchorHand;
        [Tooltip("沿手柄前向伸出的距离（米），约一臂内的可读距离")]
        public float handForwardDistance = 0.75f;
        [Tooltip("相对手柄再向上抬的偏移（米），便于在视野下方居中阅读")]
        public float handUpOffset = 0.15f;

        [Header("距离保护：面板离相机过远时改为相机正前方兜底")]
        [Tooltip("手柄锚点算出的面板超过该距离（米）时，改用相机正前方兜底，避免面板跑到房间另一头")]
        public float maxCameraDistance = 1.4f;
        [Tooltip("兜底时面板放在相机正前方多少米")]
        public float cameraFallbackDistance = 1.1f;

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
        Button _languageButton;
        Text _languageLabel;
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

            // 优先跟随手柄锚点：面板显示在手柄正前方；锚点缺失/过远时回退到相机正前方
            Vector3 desired;
            if (anchorHand != null)
            {
                desired = anchorHand.position
                          + anchorHand.forward * handForwardDistance
                          + anchorHand.up * handUpOffset;
            }
            else
            {
                desired = CameraFrontPosition();
            }

            if (_cam != null)
                desired = ClampToCamera(desired, _cam.position, _cam.forward,
                                        cameraFallbackDistance, maxCameraDistance);
            if (_cam != null)
                desired = HudPlacement.KeepInView(desired, _cam.position, _cam.rotation, 2.0f, 14f, -6f, 8f);

            _canvas.transform.position = desired;

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

        /// <summary>相机正前方的兜底位置（相机缺失时退回 worldPosition）。</summary>
        Vector3 CameraFrontPosition()
        {
            Transform c = _cam != null ? _cam : (Camera.main != null ? Camera.main.transform : null);
            if (c == null) return worldPosition;
            return c.position + c.forward * cameraFallbackDistance + c.up * -0.05f;
        }

        /// <summary>
        /// 距离保护：目标位置离相机超过 maxDistance 时，改为相机正前方 fallbackDistance 处。
        /// 纯函数，便于自检直接验证（不依赖场景中的相机）。
        /// </summary>
        public static Vector3 ClampToCamera(Vector3 desired, Vector3 camPos, Vector3 camForward,
                                            float fallbackDistance, float maxDistance)
        {
            if (Vector3.Distance(camPos, desired) <= maxDistance) return desired;
            return camPos + camForward.normalized * fallbackDistance;
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
            _panelRect.sizeDelta = new Vector2(940, 880);
            _panelRect.anchorMin = _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.anchoredPosition = Vector2.zero;
            _panelGroup = panel.GetComponent<CanvasGroup>();

            // 标题（带呼吸动画）
            _titleText = MakeText(panel.transform,
                DrillLocale.T(title, titleEnglish), 64, new Color(1f, 0.85f, 0.2f), 110f);
            UIFX.Pulse(this, _titleText.transform, 1f, titlePulseAmplitude, titlePulseFrequency);

            // 玩法说明文本框（可在 Inspector 的 Instructions 里修改）
            // 面板总高 880：标题 110 + 说明 320 + 开始 100 + 情景行 54 + 演练 62 + 教学 62 + 间距 60 + 内边距 56
            _instText = MakeText(panel.transform, DrillLocale.T(instructions, instructionsEnglish),
                20, Color.white, 320f);
            _instText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _instText.verticalOverflow = VerticalWrapMode.Truncate;
            _instText.lineSpacing = 1.15f;
            _instText.alignment = TextAnchor.UpperLeft;

            // 开始按钮（带呼吸动画）：随机挑一套起火情景（终点相同，只是起点与起火点不同）
            _startButton = MakeButton(panel.transform,
                DrillLocale.T(buttonLabel, buttonLabelEnglish), 40,
                new Color(0.12f, 0.55f, 0.30f, 1f), 100f);
            _startLabel = _startButton.GetComponentInChildren<Text>();
            UIFX.Pulse(this, _startButton.transform, 1f, buttonPulseAmplitude, buttonPulseFrequency);
            _startButton.onClick.AddListener(StartGame);

            // 三套起火情景的快选行：随机 / 电气 / 纸张 / 茶水间
            BuildScenarioRow(panel.transform);

            // 操作演练入口：原第一阶段的完整流程，玩家可先练熟再进正式演练
            if (showTutorialButton)
            {
                var tutorialBtn = MakeButton(panel.transform,
                    DrillLocale.T(tutorialLabel, tutorialLabelEnglish), 30,
                    new Color(0.78f, 0.42f, 0.12f, 1f), 62f);
                tutorialBtn.onClick.AddListener(OpenTutorial);
            }

            // 引导页入口（온보딩）：分页教学
            var onboardBtn = MakeButton(panel.transform,
                DrillLocale.T(onboardingLabel, onboardingLabelEnglish), 30,
                new Color(0.2f, 0.34f, 0.62f, 1f), 62f);
            onboardBtn.onClick.AddListener(OpenOnboarding);

            BuildLanguageButton(go.transform);
        }

        /// <summary>第 35 轮：开始界面右上角的中英文切换按钮（绝对定位，不参与竖排）。</summary>
        void BuildLanguageButton(Transform canvasRoot)
        {
            _languageButton = MakeAbsoluteButton(canvasRoot, DrillLocale.SwitchButtonLabel, 26,
                new Color(0.12f, 0.36f, 0.48f, 0.96f), new Vector2(148f, 54f));
            var rt = _languageButton.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-18f, -18f);
            _languageLabel = _languageButton.GetComponentInChildren<Text>();
            _languageButton.onClick.AddListener(ToggleLanguage);
            _languageButton.gameObject.SetActive(!Started);
        }

        Button MakeAbsoluteButton(Transform parent, string label, int size, Color bg, Vector2 sizeDelta)
        {
            var b = new GameObject("Btn_Language", typeof(RectTransform), typeof(Image), typeof(Button));
            b.transform.SetParent(parent, false);
            var img = b.GetComponent<Image>();
            img.color = bg;
            img.raycastTarget = true;
            var btn = b.GetComponent<Button>();
            btn.targetGraphic = img;
            var brt = b.GetComponent<RectTransform>();
            brt.sizeDelta = sizeDelta;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(b.transform, false);
            var t = labelGo.GetComponent<Text>();
            t.text = label;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
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

        public void ToggleLanguage()
        {
            PlayClick();
            DrillLocale.Toggle();
            RebuildUI();
        }

        void RebuildUI()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
                _canvas = null;
            }
            _panelGroup = null;
            _titleText = _instText = _startLabel = _languageLabel = null;
            _startButton = _languageButton = null;
            BuildUI();
            PlayIntroAnimation();
        }

        /// <summary>
        /// 三套起火情景的快选行。文案直接读 DrillScenario.scenarios，避免界面与场景配置写两份对不上。
        /// </summary>
        void BuildScenarioRow(Transform parent)
        {
            var row = new GameObject("ScenarioRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            var h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.padding = new RectOffset(0, 0, 0, 0);
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;
            var rle = row.GetComponent<LayoutElement>();
            rle.preferredHeight = 54f;
            var rrt = row.GetComponent<RectTransform>();
            rrt.sizeDelta = new Vector2(rrt.sizeDelta.x, 54f);

            var colors = new[]
            {
                new Color(0.72f, 0.26f, 0.20f, 1f),   // 电气
                new Color(0.66f, 0.48f, 0.16f, 1f),   // 纸张
                new Color(0.20f, 0.42f, 0.55f, 1f)    // 茶水间
            };
            var names = new[] { "电气", "纸张", "茶水间" };
            var namesEn = new[] { "Electrical", "Paper", "Pantry" };
            var dr = DrillScenario.Instance != null ? DrillScenario.Instance : FindObjectOfType<DrillScenario>();
            for (int i = 0; i < 3; i++)
            {
                string label = "情景" + (i + 1) + " · " + names[i];
                if (dr != null && dr.scenarios != null && i < dr.scenarios.Length && dr.scenarios[i] != null
                    && !string.IsNullOrEmpty(dr.scenarios[i].shortName))
                    label = "情景" + (i + 1) + " · " + dr.scenarios[i].shortName;
                int idx = i;
                string labelEn = "Scenario " + (i + 1) + " · " + namesEn[i];
                var b = MakeButton(row.transform, DrillLocale.T(label, labelEn), 24, colors[i], 54f);
                var le = b.GetComponent<LayoutElement>();
                if (le != null) { le.flexibleWidth = 1f; le.minWidth = 120f; }
                b.onClick.AddListener(() => StartScenario(idx));
            }
        }

        /// <summary>直接进入指定情景（起点与起火点由该情景决定，终点仍是同一扇安全出口）。</summary>
        public void StartScenario(int index)
        {
            PlayClick();
            var scenario = DrillScenario.Instance != null ? DrillScenario.Instance : FindObjectOfType<DrillScenario>();
            if (scenario == null)
            {
                Debug.LogWarning("[StartScreen] 场景中没有 DrillScenario，无法开始演练。");
                return;
            }

            // 关键：情景快选同样是「正式开始」。以前这里只调 StartFormal、没置 Started，
            // 导致 DesktopDrillInput / VRDrillInput 的「先把鼠标对准…请先开始演练」门禁一直生效，
            // 情景二/三的灭火器右键拿不起、问答面板也弹不出来（情景一因为走主按钮所以正常）。
            Started = true;
            if (FireDrillManager.Instance != null)
                FireDrillManager.Instance.ForceCompleteUpTo(DrillStep.AlarmRinging);

            if (scenario.ScenarioCount > 0)
                scenario.StartFormal(-1, Mathf.Clamp(index, 0, scenario.ScenarioCount - 1));
            else
                scenario.StartFormal(-1);
            StartCoroutine(ExitRoutine());
        }

        /// <summary>
        /// 演练是否真的已经开始：主按钮 / 操作演练 / 情景快选都算。
        /// 输入层的门禁统一读这个属性，避免再出现「界面已经进演练、输入层还以为没开始」。
        /// </summary>
        public bool DrillStarted
        {
            get
            {
                if (Started) return true;
                var sc = DrillScenario.Instance != null ? DrillScenario.Instance : FindObjectOfType<DrillScenario>();
                return sc != null && sc.CurrentMode == DrillScenario.Mode.Formal;
            }
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

        /// <summary>「操作演练」按钮：原第一阶段的完整实操流程（拿瓶 → 答题 → 拔销 → 压把 → 喷射）。</summary>
        public void OpenTutorial()
        {
            PlayClick();
            var scenario = DrillScenario.Instance != null ? DrillScenario.Instance : FindObjectOfType<DrillScenario>();
            if (scenario == null)
            {
                Debug.LogWarning("[StartScreen] 场景中没有 DrillScenario，操作演练退化为开始演练。");
                StartGame();
                return;
            }
            Started = true;
            scenario.BeginTutorial();
            StartCoroutine(ExitRoutine());
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

            // 新流程：正式开始由 DrillScenario 负责（随机出生点 + 烟雾 + 设备起火 + 断电 + 灭火 → 出口）
            var scenario = DrillScenario.Instance != null ? DrillScenario.Instance : FindObjectOfType<DrillScenario>();
            if (scenario != null) scenario.BeginFormal();

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

            if (_titleText != null) _titleText.text = DrillLocale.T("演练结算", "DRILL SUMMARY");
            if (_instText != null)
            {
                _instText.text = summary;
                _instText.alignment = TextAnchor.UpperCenter;
            }
            if (_startLabel != null) _startLabel.text = DrillLocale.T("重新演练", "RESTART DRILL");
            if (_languageButton != null) _languageButton.gameObject.SetActive(false);

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
            {
                // 设备火 / 出口火由 DrillScenario 在对应阶段显式点燃，不跟着开始按钮一起烧
                if (fire == null || !fire.igniteFromStartScreen) continue;
                StartCoroutine(IgniteRoutine(fire));
            }
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
