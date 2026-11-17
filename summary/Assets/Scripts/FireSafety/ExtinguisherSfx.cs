using System.Collections.Generic;
using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 第 28 轮：三种灭火剂各自的喷射 / 熄灭音效（运行时合成，不依赖额外 wav）。
    /// 三种声音刻意做出区分：CO₂ 是高压「嘶——」的高频气声，干粉是「噗嗤」的低频粉雾声，
    /// 水基是带水滴颗粒的「呲呲」水雾声。
    /// </summary>
    public static class ExtinguisherSfx
    {
        const int Rate = 44100;
        const float LoopSeconds = 1.0f;

        static readonly Dictionary<ExtinguisherKind, AudioClip> _loops = new Dictionary<ExtinguisherKind, AudioClip>();
        static readonly Dictionary<ExtinguisherKind, AudioClip> _bursts = new Dictionary<ExtinguisherKind, AudioClip>();

        /// <summary>喷射循环音（循环播放，首尾做了 40 ms 交叉淡化，不会「咔」一下）。</summary>
        public static AudioClip SprayLoop(ExtinguisherKind kind)
        {
            if (_loops.TryGetValue(kind, out var cached) && cached != null) return cached;

            int n = Mathf.RoundToInt(Rate * LoopSeconds);
            var data = new float[n];
            var rng = new System.Random(kind.GetHashCode() * 7919 + 13);
            float lp = 0f, lp2 = 0f, hpPrev = 0f, hpPrevIn = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);

                switch (kind)
                {
                    case ExtinguisherKind.CO2:
                    {
                        // 一阶高通：把噪声的低频削掉，只留气流的「嘶」
                        lp += (white - lp) * 0.05f;
                        float hp = white - lp;
                        float jet = 1f + 0.06f * Mathf.Sin(2f * Mathf.PI * 34f * t);
                        data[i] = hp * 0.42f * jet;
                        break;
                    }
                    case ExtinguisherKind.WetChemical:
                    {
                        // 带通（快低通减慢低通）+ 水滴颗粒 + 8 Hz 抖动
                        lp += (white - lp) * 0.55f;
                        lp2 += (white - lp2) * 0.035f;
                        float band = lp - lp2;
                        float mod = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 8f * t);
                        float drop = 0f;
                        int cell = Mathf.FloorToInt(t * 90f);
                        float local = (t * 90f) - cell;
                        if (local < 0.05f)
                        {
                            float rndPhase = (float)(((uint)cell * 2654435761u) % 1000u) / 1000f;
                            drop = Mathf.Sin(2f * Mathf.PI * (1800f + 2600f * rndPhase) * t)
                                   * Mathf.Exp(-local * 40f) * 0.22f;
                        }
                        data[i] = band * 0.55f * mod + drop;
                        break;
                    }
                    default: // ABC 干粉
                    {
                        // 低通噪声 + 低频「呼」的粉雾推力
                        lp += (white - lp) * 0.22f;
                        float rumble = Mathf.Sin(2f * Mathf.PI * 110f * t) * 0.10f;
                        float wobble = 0.8f + 0.2f * Mathf.Sin(2f * Mathf.PI * 4.5f * t);
                        data[i] = (lp * 0.9f * wobble) + rumble;
                        break;
                    }
                }
            }

            CrossfadeLoop(data, Mathf.RoundToInt(Rate * 0.04f));
            return Cache(_loops, kind, "SFX_Spray_" + KindToken(kind), data);
        }

        /// <summary>火被扑灭那一下的「收尾声」：CO₂ 泄压、干粉落尘、水基蒸汽。</summary>
        public static AudioClip Burst(ExtinguisherKind kind)
        {
            if (_bursts.TryGetValue(kind, out var cached) && cached != null) return cached;

            float seconds = kind == ExtinguisherKind.PowderABC ? 1.2f : 0.9f;
            int n = Mathf.RoundToInt(Rate * seconds);
            var data = new float[n];
            var rng = new System.Random(kind.GetHashCode() * 104729 + 7);
            float lp = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float k = (float)i / n;
                float env = Mathf.Exp(-k * 4.5f) * (1f - k);
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);

                switch (kind)
                {
                    case ExtinguisherKind.CO2:
                        lp += (white - lp) * 0.04f;
                        data[i] = (white - lp) * env * 0.55f;
                        break;
                    case ExtinguisherKind.WetChemical:
                        lp += (white - lp) * 0.55f;
                        data[i] = (lp + Mathf.Sin(2f * Mathf.PI * 2600f * t) * 0.5f) * env * 0.45f;
                        break;
                    default:
                        lp += (white - lp) * 0.18f;
                        data[i] = (lp * 0.9f + Mathf.Sin(2f * Mathf.PI * 72f * t) * 0.35f) * env * 0.6f;
                        break;
                }
            }

            return Cache(_bursts, kind, "SFX_Burst_" + KindToken(kind), data);
        }

        /// <summary>给自检 / 探针看的短标识（与 clip 名一致）。</summary>
        public static string KindToken(ExtinguisherKind kind)
        {
            switch (kind)
            {
                case ExtinguisherKind.CO2: return "CO2";
                case ExtinguisherKind.WetChemical: return "Wet";
                default: return "Powder";
            }
        }

        static AudioClip Cache(Dictionary<ExtinguisherKind, AudioClip> map, ExtinguisherKind kind,
                               string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            map[kind] = clip;
            return clip;
        }

        /// <summary>把尾巴和开头交叉淡化，循环播放时听不出接缝。</summary>
        static void CrossfadeLoop(float[] data, int fade)
        {
            if (data.Length <= fade * 2) return;
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                int tail = data.Length - fade + i;
                float mixed = data[tail] * (1f - w) + data[i] * w;
                data[tail] = mixed;
            }
        }
    }
}
