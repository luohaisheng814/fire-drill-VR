using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 火源音效：靠近火源声音变大，远离逐渐减弱（超出范围可自动暂停省性能）。
    /// 挂到火源 BaseFire000 上，需要 AudioSource。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class FireProximityAudio : MonoBehaviour
    {
        public enum Mode
        {
            Auto3D,   // 交给 Unity 的 3D 空间衰减（推荐，最省事）
            Manual    // 脚本每帧按距离算音量（可控性更强）
        }

        [Header("火焰声")]
        public AudioClip fireClip;
        [Range(0f, 1f)] public float maxVolume = 1f;

        [Header("模式")]
        public Mode mode = Mode.Auto3D;

        [Header("距离")]
        [Tooltip("小于此距离音量最大")]
        public float minDistance = 0.5f;
        [Tooltip("大于此距离音量为 0（超出可自动暂停）")]
        public float maxDistance = 8f;
        [Tooltip("Auto3D 时的衰减曲线")]
        public AudioRolloffMode rolloff = AudioRolloffMode.Linear;
        [Tooltip("Manual 模式的衰减指数，越大衰减越快")]
        public float falloffExponent = 1.5f;

        [Header("性能")]
        [Tooltip("超出最大距离时暂停播放")]
        public bool pauseWhenFar = true;

        private AudioSource _src;
        private Transform _listener;

        private void Awake()
        {
            _src = GetComponent<AudioSource>();
            if (_src)
            {
                _src.playOnAwake = false;
                _src.loop = true;
            }
        }

        private void Start()
        {
            if (!_src || !fireClip)
            {
                Debug.LogWarning("[FireAudio] 缺少 AudioSource 或 fireClip。");
                return;
            }

            _src.clip = fireClip;

            if (mode == Mode.Auto3D)
            {
                _src.spatialBlend = 1f;          // 完全 3D
                _src.rolloffMode = rolloff;
                _src.minDistance = minDistance;
                _src.maxDistance = maxDistance;
                _src.volume = maxVolume;
                _src.Play();
            }
            else
            {
                _src.spatialBlend = 0f;          // 2D，音量自己算
                _src.volume = 0f;
                _src.Play();
            }

            _listener = FindListener();
        }

        private void Update()
        {
            if (mode != Mode.Manual || !_src || !fireClip) return;

            if (!_listener) _listener = FindListener();
            if (!_listener) return;

            float d = Vector3.Distance(_listener.position, transform.position);
            float t = 1f - Mathf.Clamp01((d - minDistance) / Mathf.Max(0.001f, maxDistance - minDistance));
            _src.volume = maxVolume * Mathf.Pow(t, falloffExponent);

            if (pauseWhenFar)
            {
                if (d > maxDistance && _src.isPlaying) _src.Pause();
                else if (d <= maxDistance && !_src.isPlaying) _src.UnPause();
            }
        }

        private Transform FindListener()
        {
            var cam = Camera.main;
            if (cam) return cam.transform;

            var listener = FindObjectOfType<AudioListener>();
            return listener ? listener.transform : null;
        }
    }
}
