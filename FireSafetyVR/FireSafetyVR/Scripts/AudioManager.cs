using UnityEngine;

namespace FireSafetyVR
{

    /// <summary>
    /// 统一音效入口：拾取 / 放下 / 拔销 / 变形 / 喷射 / 熄灭 / 通关 / 失败 / 背景音。
    /// 场景里挂一个空物体 "AudioManager" 即可，所有脚本通过 AudioManager.Instance 调用。
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("交互音效")]
        public AudioClip pickupClip;     // 拿起灭火器
        public AudioClip dropClip;       // 放下灭火器
        public AudioClip pinClip;        // 拔出保险销
        public AudioClip transformClip;  // 形态转换（灭火口弹出）
        public AudioClip sprayClip;      // 喷射（循环）
        public AudioClip extinguishClip; // 火焰熄灭
        public AudioClip uiClickClip;    // UI 点击

        [Header("结果音效")]
        public AudioClip winClip;
        public AudioClip failClip;

        [Header("背景音（办公室消防知识练习）")]
        public AudioClip bgmClip;
        [Range(0f, 1f)] public float bgmVolume = 0.25f;

        AudioSource bgmSource;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            bgmSource = gameObject.AddComponent<AudioSource>();
            bgmSource.clip = bgmClip;
            bgmSource.loop = true;
            bgmSource.playOnAwake = false;
            bgmSource.spatialBlend = 0f;
            bgmSource.volume = bgmVolume;
        }

        void Start()
        {
            if (bgmClip != null) PlayBgm();
        }

        public void PlayBgm()
        {
            if (bgmSource == null) return;
            if (!bgmSource.isPlaying) bgmSource.Play();
        }

        public void StopBgm()
        {
            if (bgmSource != null && bgmSource.isPlaying) bgmSource.Stop();
        }

        /// <summary>在指定世界坐标播放一次性 3D 音效（自动销毁）。</summary>
        public void PlayAt(AudioClip clip, Vector3 pos, float volume = 1f, bool spatial = true)
        {
            if (clip == null) return;
            var go = new GameObject("SFX_" + clip.name);
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = spatial ? 1f : 0f;
            src.volume = volume;
            src.Play();
            Destroy(go, clip.length + 0.2f);
        }

        public void Play2D(AudioClip clip, float volume = 1f)
        {
            if (clip == null) return;
            var go = new GameObject("SFX2D_" + clip.name);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 0f;
            src.volume = volume;
            src.Play();
            DontDestroyOnLoad(go);
            Destroy(go, clip.length + 0.2f);
        }
    }
}
