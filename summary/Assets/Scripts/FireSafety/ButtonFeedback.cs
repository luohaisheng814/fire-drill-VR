using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FireSafety
{
    /// <summary>
    /// 第 36 轮：开始界面统一点击反馈。
    /// 鼠标悬停变亮、按下缩小并闪一下、松开回弹，VR 手柄射线同样走 Pointer 事件。
    /// 只改变视觉，不参与按钮点击逻辑。
    /// </summary>
    public class ButtonFeedback : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        [Header("缩放")]
        public float hoverScale = 1.035f;
        public float pressScale = 0.94f;
        public float bounceScale = 1.06f;
        public float pressDuration = 0.07f;
        public float releaseDuration = 0.14f;

        [Header("颜色")]
        [Range(0f, 1f)] public float hoverLighten = 0.22f;
        [Range(0f, 1f)] public float pressLighten = 0.30f;
        public Color outlineColor = new Color(0.35f, 1f, 0.92f, 1f);

        public bool IsPressed { get; private set; }
        public bool IsHovered { get; private set; }
        public float CurrentScale => transform.localScale.x;
        public float FlashAlpha => _flash != null ? _flash.color.a : 0f;

        Image _bg;
        Image _flash;
        Outline _outline;
        Text _label;
        Color _baseColor = Color.white;
        Coroutine _scaleRoutine;
        Coroutine _flashRoutine;
        bool _configured;

        void Awake()
        {
            EnsureVisuals();
        }

        public void Init(Color baseColor)
        {
            EnsureVisuals();
            _baseColor = baseColor;
            if (_bg != null) _bg.color = _baseColor;
            _configured = true;
            ApplyIdle();
        }

        void EnsureVisuals()
        {
            if (_bg == null) _bg = GetComponent<Image>();
            if (_bg == null) return;
            if (_baseColor == Color.white && _configured == false) _baseColor = _bg.color;

            if (_label == null) _label = GetComponentInChildren<Text>(true);

            if (_outline == null)
            {
                _outline = _bg.GetComponent<Outline>();
                if (_outline == null) _outline = _bg.gameObject.AddComponent<Outline>();
                _outline.effectColor = outlineColor;
                _outline.effectDistance = new Vector2(3f, -3f);
                _outline.useGraphicAlpha = true;
                _outline.enabled = false;
            }

            if (_flash == null)
            {
                var go = new GameObject("PressFlash", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(transform, false);
                go.transform.SetAsLastSibling();
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                _flash = go.GetComponent<Image>();
                _flash.color = new Color(1f, 1f, 1f, 0f);
                _flash.raycastTarget = false;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            IsHovered = true;
            if (_outline != null) _outline.enabled = true;
            if (_bg != null && !IsPressed) _bg.color = Lighten(_baseColor, hoverLighten);
            if (_label != null && !IsPressed) _label.color = new Color(1f, 0.97f, 0.82f);
            TweenScale(IsPressed ? pressScale : hoverScale, 0.1f);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            IsHovered = false;
            if (_outline != null && !IsPressed) _outline.enabled = false;
            if (!IsPressed) ApplyIdle();
            if (!IsPressed) TweenScale(1f, 0.12f);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            IsPressed = true;
            if (_bg != null) _bg.color = Lighten(_baseColor, pressLighten);
            if (_label != null) _label.color = new Color(1f, 0.96f, 0.62f);
            if (_outline != null) _outline.enabled = true;
            TweenScale(pressScale, pressDuration);
            Flash();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            IsPressed = false;
            if (_label != null) _label.color = Color.white;
            if (_bg != null)
            {
                // 先回弹过冲，再落到悬停/静止色
                _bg.color = IsHovered ? Lighten(_baseColor, hoverLighten) : _baseColor;
            }
            if (_outline != null) _outline.enabled = IsHovered;
            if (_scaleRoutine != null) StopCoroutine(_scaleRoutine);
            _scaleRoutine = StartCoroutine(BounceRoutine());
        }

        void ApplyIdle()
        {
            if (_bg != null) _bg.color = _baseColor;
            if (_label != null) _label.color = Color.white;
            if (_outline != null) _outline.enabled = false;
        }

        void TweenScale(float target, float duration)
        {
            if (_scaleRoutine != null) StopCoroutine(_scaleRoutine);
            _scaleRoutine = StartCoroutine(ScaleRoutine(transform.localScale.x, target, duration));
        }

        IEnumerator ScaleRoutine(float from, float to, float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / Mathf.Max(0.01f, duration));
                k = 1f - (1f - k) * (1f - k);
                float s = Mathf.Lerp(from, to, k);
                transform.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            transform.localScale = new Vector3(to, to, 1f);
            _scaleRoutine = null;
        }

        IEnumerator BounceRoutine()
        {
            float s = transform.localScale.x;
            float up = Mathf.Max(s, bounceScale);
            float t = 0f;
            while (t < releaseDuration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / releaseDuration);
                float x = Mathf.Lerp(s, up, Mathf.Sin(k * Mathf.PI * 0.5f));
                transform.localScale = new Vector3(x, x, 1f);
                yield return null;
            }
            float target = IsHovered ? hoverScale : 1f;
            t = 0f;
            while (t < releaseDuration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / releaseDuration);
                float x = Mathf.Lerp(up, target, k * k);
                transform.localScale = new Vector3(x, x, 1f);
                yield return null;
            }
            transform.localScale = new Vector3(target, target, 1f);
            _scaleRoutine = null;
        }

        void Flash()
        {
            if (_flash == null) return;
            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(FlashRoutine());
        }

        IEnumerator FlashRoutine()
        {
            float t = 0f;
            const float hold = 0.06f;
            const float duration = 0.30f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float alpha;
                if (t < hold) alpha = 0.55f;
                else alpha = 0.55f * (1f - Mathf.Clamp01((t - hold) / (duration - hold)));
                _flash.color = new Color(1f, 1f, 1f, alpha);
                yield return null;
            }
            _flash.color = new Color(1f, 1f, 1f, 0f);
            _flashRoutine = null;
        }

        static Color Lighten(Color c, float amount)
        {
            return new Color(
                Mathf.Lerp(c.r, 1f, amount),
                Mathf.Lerp(c.g, 1f, amount),
                Mathf.Lerp(c.b, 1f, amount),
                c.a);
        }

        static Color Darken(Color c, float amount)
        {
            float k = 1f - Mathf.Clamp01(amount);
            return new Color(c.r * k, c.g * k, c.b * k, c.a);
        }
    }
}
