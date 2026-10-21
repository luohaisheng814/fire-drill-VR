using System.Collections;
using UnityEngine;

namespace FireSafety
{
    /// <summary>背景音乐：循环播放 + 开场淡入。挂到空物体 BackgroundMusic 上。</summary>
    [RequireComponent(typeof(AudioSource))]
    public class BackgroundMusic : MonoBehaviour
    {
        [Header("音乐")]
        public AudioClip musicClip;
        [Range(0f, 1f)] public float volume = 0.35f;
        public bool playOnStart = true;
        [Tooltip("淡入秒数，0 = 直接满音量")]
        public float fadeInSeconds = 2f;
        [Tooltip("勾选后切换场景不销毁")]
        public bool dontDestroyOnLoad = false;

        private AudioSource _src;

        private void Awake()
        {
            _src = GetComponent<AudioSource>();
            if (_src)
            {
                _src.playOnAwake = false;
                _src.loop = true;
                _src.spatialBlend = 0f;   // 2D 音乐，不随距离衰减
                _src.volume = 0f;
            }
            if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (playOnStart) Play();
        }

        public void Play()
        {
            if (!_src || !musicClip)
            {
                Debug.LogWarning("[BGM] 缺少 AudioSource 或 musicClip。");
                return;
            }
            _src.clip = musicClip;
            _src.Play();
            StopAllCoroutines();
            StartCoroutine(FadeIn());
        }

        public void Stop()
        {
            if (_src) _src.Stop();
        }

        private IEnumerator FadeIn()
        {
            if (fadeInSeconds <= 0f)
            {
                _src.volume = volume;
                yield break;
            }
            float t = 0f;
            while (t < fadeInSeconds)
            {
                t += Time.deltaTime;
                _src.volume = Mathf.Lerp(0f, volume, Mathf.Clamp01(t / fadeInSeconds));
                yield return null;
            }
            _src.volume = volume;
        }
    }
}
