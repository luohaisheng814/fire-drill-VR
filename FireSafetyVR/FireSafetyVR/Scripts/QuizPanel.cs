using System.Collections.Generic;
using System.Linq;
using UnityEngine.UI;
using UnityEngine;

namespace FireSafetyVR
{

    /// <summary>
    /// 拾取灭火器时弹出的"消防安全步骤排序"页面（世界空间 UI）：
    ///   · 打乱 6 个消防步骤，让玩家按正确顺序点击排列
    ///   · 限时（默认 45 秒，可改 30 / 60），超时判定失败
    ///   · 排列正确 → 允许喷射灭火；超时 → 整个练习失败
    /// 挂在场景任意物体上（建议挂在 GameManager 同一物体或灭火器上）。
    /// </summary>
    public class QuizPanel : MonoBehaviour
    {
        [Header("UI（留空则自动创建 World Space Canvas）")]
        public Canvas uiCanvas;
        public Vector3 panelOffset = new Vector3(0f, 1.35f, 0f);  // 相对灭火器的位置

        [Header("限时（秒）")]
        public float timeLimit = 45f;

        [Header("音效")]
        public AudioClip clickClip;

        readonly string[] steps =
        {
            "按下火灾警报并大声呼救",
            "判断火势，确认安全出口方向",
            "找到最近的灭火器，站到上风处",
            "与着火点保持 2~3 米安全距离",
            "拔出保险销，喷口对准火焰根部",
            "压下压把左右扫射，确认不复燃"
        };

        GameObject panelRoot;
        Text timerText, orderText, feedbackText;
        List<Button> optionButtons = new List<Button>();
        List<string> shuffled = new List<string>();
        List<int> picked = new List<int>();
        float remain;
        bool active;
        Transform anchor;

        public bool IsActive => active;
        public bool Passed { get; private set; }

        void Awake() => BuildUI();

        void Update()
        {
            if (!active) return;
            remain -= Time.deltaTime;
            if (timerText != null)
                timerText.text = "剩余时间：" + Mathf.CeilToInt(Mathf.Max(0f, remain)) + " 秒";

            if (remain <= 0f) Fail("时间到！未在规定时间内完成步骤排序");
        }

        // ---------- 对外接口 ----------

        public void Open(Transform worldAnchor = null)
        {
            if (Passed) return;                       // 已通过就不再重复弹
            anchor = worldAnchor != null ? worldAnchor : transform;

            uiCanvas = UIBuilder.EnsureCanvas(uiCanvas, "Canvas_Quiz",
                anchor.position + panelOffset);

            panelRoot.SetActive(true);
            shuffled = steps.OrderBy(s => Random.value).ToList();
            picked.Clear();
            RebuildOptions();
            UpdateOrderText();
            feedbackText.text = "请按正确顺序点击 6 个消防步骤";

            remain = timeLimit;
            active = true;

            if (GameManager.Instance != null) GameManager.Instance.SetState(GameState.Quiz);
        }

        public void Close()
        {
            active = false;
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        /// <summary>重开一局时把问答复位。</summary>
        public void ResetQuiz()
        {
            Passed = false;
            picked.Clear();
            remain = timeLimit;
            active = false;
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        // ---------- UI 构建 ----------

        void BuildUI()
        {
            uiCanvas = UIBuilder.EnsureCanvas(uiCanvas, "Canvas_Quiz", transform.position + panelOffset);
            uiCanvas.GetComponent<RectTransform>().sizeDelta = new Vector2(1000, 900);

            var bg = UIBuilder.Panel(uiCanvas.transform, "QuizPanel", new Vector2(820, 820),
                new Color(0.05f, 0.07f, 0.12f, 0.92f));
            panelRoot = bg.gameObject;
            bg.anchorMin = new Vector2(0.5f, 0.5f);
            bg.anchorMax = new Vector2(0.5f, 0.5f);
            bg.pivot = new Vector2(0.5f, 0.5f);
            bg.anchoredPosition = Vector2.zero;

            UIBuilder.Text(bg.transform, "消防安全步骤排序", 42, Color.yellow, 70);
            timerText = UIBuilder.Text(bg.transform, "剩余时间：--", 34, new Color(1f, 0.55f, 0.3f), 50);

            var optionsHost = UIBuilder.Panel(bg.transform, "Options", new Vector2(760, 470),
                new Color(0.12f, 0.15f, 0.22f, 0.6f));
            for (int i = 0; i < steps.Length; i++)
            {
                int index = i;
                var btn = UIBuilder.Button(optionsHost.transform, "", 28, () => OnOptionClicked(index),
                    new Color(0.16f, 0.38f, 0.62f, 1f), 62);
                optionButtons.Add(btn);
            }

            orderText = UIBuilder.Text(bg.transform, "已排顺序：（空）", 24, Color.white, 220);
            feedbackText = UIBuilder.Text(bg.transform, "", 28, Color.cyan, 50);

            var row = UIBuilder.Panel(bg.transform, "Row", new Vector2(700, 80), new Color(0f, 0f, 0f, 0f));
            var layout = row.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            UIBuilder.Button(row.transform, "撤销上一步", 28, Undo, new Color(0.45f, 0.30f, 0.10f, 1f), 60);
            UIBuilder.Button(row.transform, "提交答案", 30, Submit, new Color(0.10f, 0.50f, 0.25f, 1f), 66);

            panelRoot.SetActive(false);
        }

        void RebuildOptions()
        {
            for (int i = 0; i < optionButtons.Count; i++)
            {
                var label = optionButtons[i].GetComponentInChildren<Text>();
                if (label != null) label.text = (i + 1) + ". " + shuffled[i];
                optionButtons[i].interactable = true;
                optionButtons[i].image.color = new Color(0.16f, 0.38f, 0.62f, 1f);
            }
        }

        void OnOptionClicked(int index)
        {
            if (!active || Passed) return;
            if (picked.Contains(index)) return;

            picked.Add(index);
            optionButtons[index].interactable = false;
            optionButtons[index].image.color = new Color(0.20f, 0.55f, 0.30f, 1f);
            UpdateOrderText();
            Click();
        }

        void UpdateOrderText()
        {
            var lines = new List<string>();
            for (int i = 0; i < picked.Count; i++)
                lines.Add((i + 1) + " → " + shuffled[picked[i]]);
            orderText.text = lines.Count == 0 ? "已排顺序：（空）" : "已排顺序：\n" + string.Join("\n", lines);
        }

        void Undo()
        {
            if (!active || picked.Count == 0) return;
            int last = picked[picked.Count - 1];
            picked.RemoveAt(picked.Count - 1);
            optionButtons[last].interactable = true;
            optionButtons[last].image.color = new Color(0.16f, 0.38f, 0.62f, 1f);
            UpdateOrderText();
            Click();
        }

        void Submit()
        {
            if (!active || Passed) return;
            Click();

            if (picked.Count < steps.Length)
            {
                feedbackText.text = "还有步骤没有排完（" + picked.Count + "/" + steps.Length + "）";
                return;
            }

            bool ok = true;
            for (int i = 0; i < picked.Count; i++)
                if (shuffled[picked[i]] != steps[i]) { ok = false; break; }

            if (ok)
            {
                Passed = true;
                active = false;
                feedbackText.text = "回答正确！现在可以拔销灭火了";
                if (GameManager.Instance != null) GameManager.Instance.OnQuizPassed();
                Invoke(nameof(Close), 1.2f);
            }
            else
            {
                feedbackText.text = "顺序有误，请重新排列（时间继续计时）";
                picked.Clear();
                RebuildOptions();
                UpdateOrderText();
            }
        }

        void Fail(string reason)
        {
            active = false;
            feedbackText.text = reason;
            Debug.Log("[问答] " + reason);
            if (GameManager.Instance != null) GameManager.Instance.Fail(reason);
            Invoke(nameof(Close), 1.5f);
        }

        void Click()
        {
            if (AudioManager.Instance != null)
                AudioManager.Instance.Play2D(AudioManager.Instance.uiClickClip, 0.6f);
        }
    }
}
