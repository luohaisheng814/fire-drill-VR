using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace FireSafety
{
    /// <summary>
    /// 拾取灭火器时弹出的「灭火器常识问答」面板（2~3 道选择题，可在 Inspector 增删题目）。
    /// 答完所有题目（每道题都会给出正确/错误反馈与讲解）后自动关闭并广播 onPassed。
    /// 纯代码生成世界空间 UI，中文字体自动解析。
    /// </summary>
    public class ExtinguisherQuizPanel : MonoBehaviour
    {
        [System.Serializable]
        public class QuizQuestion
        {
            [TextArea(2, 4)] public string question;
            [Tooltip("4 个选项，第 correctIndex 个为正确答案（0 起）")]
            public string[] options = new string[4];
            public int correctIndex;
            [TextArea(1, 3)] public string explanation;
        }

        [Header("题目（默认 3 题，可增删）")]
        public List<QuizQuestion> questions = new List<QuizQuestion>
        {
            new QuizQuestion
            {
                question = "使用灭火器灭火时，人应该站在火焰的哪个方向？",
                options = new[] { "下风方向", "上风方向", "侧面随意站", "任意方向都可以" },
                correctIndex = 1,
                explanation = "站在上风方向可以避免被火势与浓烟包围，是最安全的位置。"
            },
            new QuizQuestion
            {
                question = "喷射灭火剂时，喷口应对准火焰的哪个部位？",
                options = new[] { "火焰顶部", "火焰中部", "火焰根部", "对准空中" },
                correctIndex = 2,
                explanation = "对准火焰根部喷射，才能切断燃烧源，灭火效果最好。"
            },
            new QuizQuestion
            {
                question = "使用干粉灭火器前，应确认压力表指针位于哪个区域？",
                options = new[] { "红色区域", "黄色区域", "绿色区域", "黑色区域" },
                correctIndex = 2,
                explanation = "指针位于绿色区域表示压力正常，可以正常喷射。"
            }
        };

        [Header("UI 位置")]
        [Tooltip("相对灭火器/锚点的偏移")]
        public Vector3 panelOffset = new Vector3(0f, 1.35f, 0f);
        public float canvasScale = 0.004f;

        [Header("行为")]
        [Tooltip("答错时显示正确答案与讲解，随后进入下一题")]
        public bool teachOnWrong = true;
        [Tooltip("每道题作答后的停留秒数")]
        public float advanceDelay = 1.5f;

        [Header("事件")]
        public UnityEvent onPassed;

        public bool IsOpen { get; private set; }
        public bool Passed { get; private set; }
        public int CorrectCount { get; private set; }

        Canvas _canvas;
        GameObject _panelRoot;
        CanvasGroup _panelGroup;
        Text _progressText;
        Text _questionText;
        Text _feedbackText;
        List<Button> _optionButtons = new List<Button>();
        List<Text> _optionLabels = new List<Text>();
        int _index;
        Coroutine _advance;

        void Awake()
        {
            BuildUI();
        }

        // ---------------- 对外接口 ----------------

        /// <summary>在世界锚点处打开问答面板。</summary>
        public void Open(Transform worldAnchor = null)
        {
            if (Passed) return;
            if (questions == null || questions.Count == 0)
            {
                Debug.LogWarning("[Quiz] 没有配置题目，直接视为通过。");
                Passed = true;
                onPassed?.Invoke();
                return;
            }

            Transform anchor = worldAnchor != null ? worldAnchor : transform;
            if (_canvas == null) BuildUI();
            _canvas.transform.position = anchor.position + panelOffset;
            _canvas.gameObject.SetActive(true);
            _panelRoot.SetActive(true);
            _panelGroup.alpha = 0f;
            UIFX.Fade(this, _panelGroup, 0f, 1f, 0.25f);
            UIFX.ScaleIn(this, _panelRoot.transform, 0.88f, 1f, 0.3f, 1.06f);

            _index = 0;
            CorrectCount = 0;
            IsOpen = true;
            ShowQuestion(0);
        }

        public void Close()
        {
            IsOpen = false;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        public void ResetQuiz()
        {
            Passed = false;
            IsOpen = false;
            CorrectCount = 0;
            _index = 0;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        /// <summary>键盘/鼠标等外部输入作答（0 起）。仅当面板打开且该选项可点时生效。</summary>
        public bool AnswerOption(int optionIndex)
        {
            if (!IsOpen) return false;
            if (_optionButtons == null || optionIndex < 0 || optionIndex >= _optionButtons.Count) return false;

            var btn = _optionButtons[optionIndex];
            if (btn == null || !btn.gameObject.activeSelf || !btn.interactable) return false;

            OnOptionClicked(optionIndex);
            return true;
        }

        // ---------------- UI ----------------

        void BuildUI()
        {
            var go = new GameObject("Canvas_Quiz");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 250;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1000, 800);
            rt.localScale = Vector3.one * canvasScale;
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<TrackedDeviceGraphicRaycaster>();

            var panel = new GameObject("QuizPanel", typeof(RectTransform), typeof(Image),
                typeof(VerticalLayoutGroup), typeof(CanvasGroup));
            panel.transform.SetParent(go.transform, false);
            var img = panel.GetComponent<Image>();
            img.color = new Color(0.05f, 0.07f, 0.12f, 0.95f);
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.padding = new RectOffset(26, 26, 26, 26);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var prt = panel.GetComponent<RectTransform>();
            prt.sizeDelta = new Vector2(940, 720);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            _panelGroup = panel.GetComponent<CanvasGroup>();
            _panelRoot = panel;

            AddText(panel.transform, "灭火器常识问答", 46, new Color(1f, 0.85f, 0.2f), 80f);
            _progressText = AddText(panel.transform, "", 28, new Color(0.6f, 0.8f, 1f), 44f);
            _questionText = AddText(panel.transform, "", 34, Color.white, 120f);

            var optionsHost = new GameObject("Options", typeof(RectTransform), typeof(VerticalLayoutGroup));
            optionsHost.transform.SetParent(panel.transform, false);
            var ol = optionsHost.GetComponent<VerticalLayoutGroup>();
            ol.spacing = 8f;
            ol.padding = new RectOffset(10, 10, 4, 4);
            var ort = optionsHost.GetComponent<RectTransform>();
            ort.sizeDelta = new Vector2(880, 300);
            _optionButtons.Clear();
            _optionLabels.Clear();
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var b = AddButton(optionsHost.transform, "", 30, new Color(0.16f, 0.38f, 0.62f, 1f), 66f);
                _optionButtons.Add(b);
                _optionLabels.Add(b.GetComponentInChildren<Text>());
                b.onClick.AddListener(() => OnOptionClicked(idx));
            }

            _feedbackText = AddText(panel.transform, "", 28, Color.cyan, 80f);
            AddText(panel.transform, "提示：用手柄射线指向选项并扣动扳机选择", 22, new Color(0.75f, 0.75f, 0.75f), 40f);

            go.SetActive(false);
        }

        Text AddText(Transform parent, string content, int size, Color color, float height)
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

        Button AddButton(Transform parent, string label, int size, Color bg, float height)
        {
            var b = new GameObject("Btn", typeof(RectTransform), typeof(Image), typeof(Button));
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
            t.fontSize = size;
            t.color = Color.white;
            t.font = UIFont.Resolve();
            if (t.font != null) t.material = t.font.material;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.raycastTarget = false;
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(16f, 0f);
            lrt.offsetMax = new Vector2(-8f, 0f);
            return btn;
        }

        // ---------------- 答题流程 ----------------

        void ShowQuestion(int index)
        {
            if (index >= questions.Count) { Finish(); return; }

            var q = questions[index];
            _progressText.text = "第 " + (index + 1) + " / " + questions.Count + " 题";
            _questionText.text = q.question;
            _feedbackText.text = "";
            for (int i = 0; i < _optionButtons.Count; i++)
            {
                bool valid = i < q.options.Length && !string.IsNullOrEmpty(q.options[i]);
                _optionButtons[i].gameObject.SetActive(valid);
                if (valid) _optionLabels[i].text = (i + 1) + ". " + q.options[i];
                _optionButtons[i].interactable = true;
                _optionButtons[i].image.color = new Color(0.16f, 0.38f, 0.62f, 1f);
            }
        }

        void OnOptionClicked(int optionIndex)
        {
            if (!IsOpen || _index >= questions.Count) return;
            var q = questions[_index];

            for (int i = 0; i < _optionButtons.Count; i++) _optionButtons[i].interactable = false;

            if (optionIndex == q.correctIndex)
            {
                CorrectCount++;
                _optionButtons[optionIndex].image.color = new Color(0.15f, 0.65f, 0.30f, 1f);
                _feedbackText.text = "回答正确！" + (string.IsNullOrEmpty(q.explanation) ? "" : q.explanation);
                _feedbackText.color = Color.green;
                Play("Sounds/SFX/quiz_correct");
            }
            else
            {
                _optionButtons[optionIndex].image.color = new Color(0.75f, 0.2f, 0.2f, 1f);
                if (q.correctIndex >= 0 && q.correctIndex < _optionButtons.Count)
                    _optionButtons[q.correctIndex].image.color = new Color(0.15f, 0.65f, 0.30f, 1f);
                _feedbackText.text = "回答错误。" + (teachOnWrong && !string.IsNullOrEmpty(q.explanation) ? q.explanation : "");
                _feedbackText.color = teachOnWrong ? new Color(1f, 0.8f, 0.4f) : Color.red;
                Play("Sounds/SFX/quiz_wrong");
            }

            if (_advance != null) StopCoroutine(_advance);
            _advance = StartCoroutine(AdvanceRoutine());
        }

        IEnumerator AdvanceRoutine()
        {
            yield return new WaitForSeconds(advanceDelay);
            _index++;
            if (_index < questions.Count) ShowQuestion(_index);
            else Finish();
        }

        void Finish()
        {
            IsOpen = false;
            Passed = true;
            _feedbackText.text = "回答完毕！现在去对准火源灭火吧。";
            _feedbackText.color = Color.green;
            Play("Sounds/SFX/quiz_correct");
            onPassed?.Invoke();
            if (_advance != null) StopCoroutine(_advance);
            _advance = StartCoroutine(CloseRoutine());
        }

        IEnumerator CloseRoutine()
        {
            UIFX.Fade(this, _panelGroup, 1f, 0f, 0.3f);
            yield return new WaitForSeconds(0.4f);
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        void Play(string resourcePath)
        {
            var clip = Resources.Load<AudioClip>(resourcePath);
            if (clip != null) AudioSource.PlayClipAtPoint(clip, transform.position, 0.7f);
        }
    }
}
