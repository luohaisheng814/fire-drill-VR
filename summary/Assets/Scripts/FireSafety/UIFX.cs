using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace FireSafety
{
    /// <summary>
    /// 轻量 UI 动画工具（“动画”功能）：
    /// 淡入淡出、缩放弹入、位移动画、呼吸脉动、闪烁——全部用协程实现，零第三方依赖。
    /// 所有方法返回 Coroutine，需要停止时可用 host.StopCoroutine(...)。
    /// </summary>
    public static class UIFX
    {
        // ---------------- 淡入淡出 ----------------

        public static Coroutine Fade(MonoBehaviour host, CanvasGroup g, float from, float to, float duration)
        {
            return host.StartCoroutine(FadeRoutine(g, from, to, duration));
        }

        static IEnumerator FadeRoutine(CanvasGroup g, float from, float to, float duration)
        {
            if (g == null) yield break;
            g.alpha = from;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                g.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(t / Mathf.Max(0.001f, duration)));
                yield return null;
            }
            g.alpha = to;
        }

        // ---------------- 缩放弹入 ----------------

        public static Coroutine ScaleIn(MonoBehaviour host, Transform t, float fromScale, float toScale,
            float duration, float overshoot = 1.12f)
        {
            return host.StartCoroutine(ScaleRoutine(t, fromScale, toScale, duration, overshoot));
        }

        static IEnumerator ScaleRoutine(Transform t, float fromScale, float toScale, float duration, float overshoot)
        {
            if (t == null) yield break;
            Vector3 start = t.localScale;
            t.localScale = Vector3.one * fromScale;
            float tm = 0f;
            while (tm < duration)
            {
                tm += Time.deltaTime;
                float k = Mathf.Clamp01(tm / Mathf.Max(0.001f, duration));
                float ease = 1f - Mathf.Pow(1f - k, 3f);                 // ease-out cubic
                float s = Mathf.Lerp(fromScale, toScale, ease)
                          + Mathf.Sin(k * Mathf.PI) * (toScale - fromScale) * (overshoot - 1f);
                t.localScale = start * s;
                yield return null;
            }
            t.localScale = start * toScale;
        }

        // ---------------- 位移动画 ----------------

        public static Coroutine SlideIn(MonoBehaviour host, RectTransform rt, Vector2 fromOffset, Vector2 toOffset,
            float duration)
        {
            return host.StartCoroutine(SlideRoutine(rt, fromOffset, toOffset, duration));
        }

        static IEnumerator SlideRoutine(RectTransform rt, Vector2 from, Vector2 to, float duration)
        {
            if (rt == null) yield break;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / Mathf.Max(0.001f, duration));
                float ease = 1f - Mathf.Pow(1f - k, 3f);
                rt.anchoredPosition = Vector2.Lerp(from, to, ease);
                yield return null;
            }
            rt.anchoredPosition = to;
        }

        // ---------------- 呼吸脉动（循环） ----------------

        public static Coroutine Pulse(MonoBehaviour host, Transform t, float baseScale, float amplitude,
            float frequency)
        {
            return host.StartCoroutine(PulseRoutine(t, baseScale, amplitude, frequency));
        }

        static IEnumerator PulseRoutine(Transform t, float baseScale, float amplitude, float frequency)
        {
            if (t == null) yield break;
            Vector3 start = t.localScale;
            while (true)
            {
                float s = baseScale + amplitude * (Mathf.Sin(Time.time * Mathf.PI * 2f * frequency) * 0.5f + 0.5f);
                t.localScale = start * s;
                yield return null;
            }
        }

        // ---------------- 透明度脉动（循环，警报红屏等） ----------------

        public static Coroutine PulseAlpha(MonoBehaviour host, Graphic g, float minAlpha, float maxAlpha,
            float frequency)
        {
            return host.StartCoroutine(PulseAlphaRoutine(g, minAlpha, maxAlpha, frequency));
        }

        static IEnumerator PulseAlphaRoutine(Graphic g, float minAlpha, float maxAlpha, float frequency)
        {
            if (g == null) yield break;
            Color c = g.color;
            while (true)
            {
                float a = Mathf.Lerp(minAlpha, maxAlpha, Mathf.Sin(Time.time * Mathf.PI * 2f * frequency) * 0.5f + 0.5f);
                c.a = a;
                g.color = c;
                yield return null;
            }
        }

        // ---------------- 一次性闪烁（灯光/图片） ----------------

        public static Coroutine Flash(MonoBehaviour host, Light l, float maxIntensity, float duration,
            int blinks = 3)
        {
            return host.StartCoroutine(FlashRoutine(l, maxIntensity, duration, blinks));
        }

        static IEnumerator FlashRoutine(Light l, float maxIntensity, float duration, int blinks)
        {
            if (l == null) yield break;
            float home = l.intensity;
            for (int i = 0; i < blinks; i++)
            {
                float t = 0f;
                while (t < duration / blinks)
                {
                    t += Time.deltaTime;
                    l.intensity = Mathf.Lerp(home, maxIntensity, Mathf.PingPong(t * 4f, 1f));
                    yield return null;
                }
            }
            l.intensity = home;
        }
    }
}
