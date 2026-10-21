using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 临时占位音效生成器：不需要任何音频文件即可听到拿起/放下/火焰/背景音。
    /// 正式项目换成真实 wav 后，删除本组件即可。
    /// </summary>
    public class DemoSfxGenerator : MonoBehaviour
    {
        public bool generateOnAwake = true;

        [Header("赋值目标（可留空）")]
        public GrabSfx grabSfx;
        public BackgroundMusic bgm;
        public FireProximityAudio fireAudio;

        private const int Rate = 44100;

        private void Awake()
        {
            if (!generateOnAwake) return;
            if (grabSfx)
            {
                grabSfx.pickupClip = Beep("SFX_Pickup", 520f, 880f, 0.14f);
                grabSfx.dropClip = Beep("SFX_Drop", 700f, 300f, 0.16f);
            }
            if (bgm) bgm.musicClip = Drone("BGM_Ambient", 6f);
            if (fireAudio) fireAudio.fireClip = Noise("SFX_Fire", 3f);
        }

        /// <summary>频率扫掠音（上升=拿起，下降=放下）。</summary>
        private static AudioClip Beep(string name, float f0, float f1, float duration)
        {
            var clip = AudioClip.Create(name, (int)(Rate * duration), 1, Rate, false);
            var data = new float[(int)(Rate * duration)];
            float phase = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)data.Length;
                float f = Mathf.Lerp(f0, f1, t);
                phase += 2f * Mathf.PI * f / Rate;
                float env = Mathf.Sin(Mathf.PI * t);          // 平滑进出，避免爆音
                data[i] = Mathf.Sin(phase) * env * 0.5f;
            }
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>低通噪声（火焰燃烧声）。</summary>
        private static AudioClip Noise(string name, float duration)
        {
            var clip = AudioClip.Create(name, (int)(Rate * duration), 1, Rate, false);
            var data = new float[(int)(Rate * duration)];
            float last = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                last = (last + (Random.value * 2f - 1f)) * 0.35f;  // 一阶低通
                data[i] = last * 0.6f;
            }
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>低频环境垫（背景音乐占位）。</summary>
        private static AudioClip Drone(string name, float duration)
        {
            var clip = AudioClip.Create(name, (int)(Rate * duration), 1, Rate, false);
            var data = new float[(int)(Rate * duration)];
            for (int i = 0; i < data.Length; i++)
            {
                float t = i / (float)Rate;
                float v = Mathf.Sin(2 * Mathf.PI * 110f * t) * 0.25f
                        + Mathf.Sin(2 * Mathf.PI * 165f * t) * 0.15f
                        + Mathf.Sin(2 * Mathf.PI * 220f * t) * 0.10f;
                data[i] = v * (0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * 0.15f * t));
            }
            clip.SetData(data, 0);
            return clip;
        }
    }
}
