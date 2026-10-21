using System.Collections;
using UnityEngine.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FireSafetyVR
{

    public enum GameState
    {
        Intro,          // 开场：办公室消防知识练习
        Practicing,     // 练习中（找灭火器 / 拾取）
        Quiz,           // 消防安全步骤排序
        Extinguishing,  // 灭火中
        Success,
        Failed
    }

    /// <summary>
    /// 全局流程：开场 → 拾取灭火器 → 排序小测 → 三步灭火 → 通关 / 失败
    ///   · 总时长（默认 120 秒）超时即失败
    ///   · 排序小测超时（QuizPanel.timeLimit，默认 45 秒）也算失败
    ///   · 火焰生命值归零 → 通关
    /// 挂在一个空物体 "GameManager" 上。
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("时间")]
        public float totalTime = 120f;

        [Header("UI 画布（留空自动生成）")]
        public Canvas uiCanvas;
        public Vector3 hudOffset = new Vector3(0f, 1.7f, 1.6f);

        [Header("引用")]
        public QuizPanel quizPanel;
        public FireTarget fire;
        public FireExtinguisher extinguisher;

        [Header("结果")]
        public bool reloadSceneOnRestart = true;

        public GameState State { get; private set; }
        public float RemainTime { get; private set; }
        public bool QuizPassed { get; private set; } = false;

        GameObject introPanel, hudPanel, resultPanel;
        Text timerText, hintText, resultTitle, resultDetail;
        bool running;
        bool finished;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
            BuildUI();
            EnterIntro();
        }

        void Update()
        {
            if (!running || finished) return;
            RemainTime -= Time.deltaTime;

            if (timerText != null)
            {
                int s = Mathf.CeilToInt(Mathf.Max(0f, RemainTime));
                timerText.text = "总倒计时 " + s + " 秒";
                timerText.color = RemainTime < 20f ? Color.red : Color.white;
            }

            if (RemainTime <= 0f) Fail("两分钟练习时间已到，任务失败");
        }

        // ---------------- 流程 ----------------

        void EnterIntro()
        {
            SetState(GameState.Intro);
            if (introPanel != null) introPanel.SetActive(true);
            if (hudPanel != null) hudPanel.SetActive(false);
            if (resultPanel != null) resultPanel.SetActive(false);
        }

        /// <summary>开场面板「开始练习」按钮调用。</summary>
        public void StartPractice()
        {
            if (State != GameState.Intro) return;
            if (introPanel != null) introPanel.SetActive(false);
            if (hudPanel != null) hudPanel.SetActive(true);

            RemainTime = totalTime;
            running = true;
            finished = false;
            SetState(GameState.Practicing);
            SetHint("火灾警报响起！先找到灭火器，用 Grip 键拿起它");

            if (AudioManager.Instance != null) AudioManager.Instance.PlayBgm();
            Click();
        }

        /// <summary>由 ExtinguisherGrab 在拿起灭火器时调用。</summary>
        public void OnExtinguisherPicked(ExtinguisherGrab grab)
        {
            if (!running || finished) return;
            if (QuizPassed) { SetHint("已通过小测，直接拔保险销开始灭火"); return; }

            SetState(GameState.Quiz);
            if (quizPanel == null) quizPanel = FindObjectOfType<QuizPanel>(true);
            if (quizPanel != null) quizPanel.Open(grab != null ? grab.transform : transform);
            else { QuizPassed = true; SetHint("未找到问答面板，直接进入灭火"); }
        }

        public void OnQuizPassed()
        {
            QuizPassed = true;
            SetState(GameState.Practicing);
            SetHint("小测通过！点击手柄拔出保险销（第 1 步）");
        }

        public void NotifySpraying()
        {
            if (running && !finished && State != GameState.Extinguishing)
                SetState(GameState.Extinguishing);
        }

        /// <summary>由 FireTarget.onExtinguished 调用（火焰生命值归零）。</summary>
        public void OnFireExtinguished()
        {
            if (finished) return;
            Win();
        }

        public void Win()
        {
            if (finished) return;
            finished = true;
            running = false;
            SetState(GameState.Success);
            ShowResult(true, "演练通关！\n用时 " + Mathf.RoundToInt(totalTime - RemainTime) + " 秒");
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.Play2D(AudioManager.Instance.winClip, 0.9f);
                AudioManager.Instance.StopBgm();
            }
        }

        public void Fail(string reason)
        {
            if (finished) return;
            finished = true;
            running = false;
            SetState(GameState.Failed);
            ShowResult(false, reason);
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.Play2D(AudioManager.Instance.failClip, 0.9f);
                AudioManager.Instance.StopBgm();
            }
        }

        public void Restart()
        {
            Click();
            finished = false;
            running = false;
            QuizPassed = false;

            if (reloadSceneOnRestart && !string.IsNullOrEmpty(SceneManager.GetActiveScene().name))
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
                return;
            }

            if (quizPanel != null) quizPanel.ResetQuiz();
            if (fire != null) { fire.health = fire.maxHealth; }
            EnterIntro();
        }

        // ---------------- 状态 / 提示 ----------------

        public void SetState(GameState s)
        {
            State = s;
            Debug.Log("[流程] " + s);
        }

        public void SetHint(string msg)
        {
            if (hintText != null) hintText.text = msg;
            Debug.Log("[提示] " + msg);
        }

        void Click()
        {
            if (AudioManager.Instance != null)
                AudioManager.Instance.Play2D(AudioManager.Instance.uiClickClip, 0.6f);
        }

        // ---------------- UI ----------------

        void BuildUI()
        {
            var cam = Camera.main;
            Vector3 basePos = cam != null
                ? cam.transform.position + cam.transform.forward * 1.6f
                : transform.position + hudOffset;

            uiCanvas = UIBuilder.EnsureCanvas(uiCanvas, "Canvas_Main", basePos);
            var root = uiCanvas.GetComponent<RectTransform>();

            // 开场面板
            introPanel = UIBuilder.Panel(root, "IntroPanel", new Vector2(900, 620),
                new Color(0.04f, 0.06f, 0.10f, 0.95f)).gameObject;
            AnchorCenter(introPanel.GetComponent<RectTransform>());
            UIBuilder.Text(introPanel.transform, "办公室消防知识练习", 52, Color.yellow, 90);
            UIBuilder.Text(introPanel.transform,
                "情景：办公室内火灾警报响起\n" +
                "任务（限时 2 分钟）：\n" +
                "1. 确认起火位置，移动到灭火器处\n" +
                "2. 用 Grip 键拿起灭火器（双击 Grip 才能放下）\n" +
                "3. 完成「消防安全步骤」排序小测（限时 45 秒）\n" +
                "4. 拔保险销 → 展开灭火口 → 对准火焰根部喷射\n" +
                "5. 火焰熄灭即通关",
                30, Color.white, 340);
            UIBuilder.Button(introPanel.transform, "开始练习", 36, StartPractice,
                new Color(0.10f, 0.50f, 0.25f, 1f), 80);

            // HUD
            hudPanel = new GameObject("HUD", typeof(RectTransform)).gameObject;
            hudPanel.transform.SetParent(root, false);
            var hudRect = hudPanel.GetComponent<RectTransform>();
            hudRect.anchorMin = new Vector2(0.5f, 1f);
            hudRect.anchorMax = new Vector2(0.5f, 1f);
            hudRect.pivot = new Vector2(0.5f, 1f);
            hudRect.anchoredPosition = new Vector2(0f, -20f);
            hudRect.sizeDelta = new Vector2(900, 200);

            timerText = UIBuilder.Text(hudPanel.transform, "总倒计时 --", 40, Color.white, 60);
            SetTopStretch(timerText.rectTransform, 10f, 60f);

            hintText = UIBuilder.Text(hudPanel.transform, "", 30, Color.cyan, 120);
            SetTopStretch(hintText.rectTransform, 80f, 120f);

            // 结算面板
            resultPanel = UIBuilder.Panel(root, "ResultPanel", new Vector2(760, 420),
                new Color(0.06f, 0.08f, 0.12f, 0.95f)).gameObject;
            AnchorCenter(resultPanel.GetComponent<RectTransform>());
            resultTitle = UIBuilder.Text(resultPanel.transform, "", 48, Color.white, 80);
            resultDetail = UIBuilder.Text(resultPanel.transform, "", 30, Color.white, 140);
            UIBuilder.Button(resultPanel.transform, "重新开始", 34, Restart,
                new Color(0.16f, 0.38f, 0.62f, 1f), 76);

            hudPanel.SetActive(false);
            resultPanel.SetActive(false);
        }

        static void AnchorCenter(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }

        void ShowResult(bool win, string detail)
        {
            if (resultPanel != null) resultPanel.SetActive(true);
            if (hudPanel != null) hudPanel.SetActive(false);
            if (resultTitle != null)
            {
                resultTitle.text = win ? "演练成功" : "演练失败";
                resultTitle.color = win ? Color.green : Color.red;
            }
            if (resultDetail != null) resultDetail.text = detail;
        }

        static void SetTopStretch(RectTransform rt, float topOffset, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(20f, -(topOffset + height));
            rt.offsetMax = new Vector2(-20f, -topOffset);
        }

        void OnEnable()
        {
            if (fire != null)
            {
                fire.onExtinguished.RemoveListener(OnFireExtinguished);
                fire.onExtinguished.AddListener(OnFireExtinguished);
            }
        }
    }
}
