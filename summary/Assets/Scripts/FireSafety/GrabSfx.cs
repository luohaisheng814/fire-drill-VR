using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 灭火器拾起 / 放下音效播放器。
    /// 无任何依赖：由 XR Grab 事件、ToggleGrab 事件或代码调用 PlayPickup()/PlayDrop()。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class GrabSfx : MonoBehaviour
    {
        [Header("音效")]
        public AudioClip pickupClip;
        public AudioClip dropClip;
        [Range(0f, 1f)] public float volume = 1f;

        [Header("调试")]
        public bool logOnPlay = false;

        private AudioSource _src;

        private void Awake()
        {
            if (pickupClip == null) pickupClip = Resources.Load<AudioClip>("Sounds/SFX/grab_pickup");
            if (dropClip == null) dropClip = Resources.Load<AudioClip>("Sounds/SFX/grab_drop");

            _src = GetComponent<AudioSource>();
            if (_src)
            {
                _src.playOnAwake = false;
                _src.loop = false;
            }
        }

        /// <summary>拿起时调用（Inspector 事件里选 GrabSfx.PlayPickup）。</summary>
        public void PlayPickup() => Play(pickupClip, "拿起");

        /// <summary>放下时调用（Inspector 事件里选 GrabSfx.PlayDrop）。</summary>
        public void PlayDrop() => Play(dropClip, "放下");

        private void Play(AudioClip clip, string tag)
        {
            if (!clip)
            {
                Debug.LogWarning($"[GrabSfx] {tag} 音效 Clip 为空，请在 Inspector 里拖入音频。");
                return;
            }
            if (_src) _src.PlayOneShot(clip, volume);
            else AudioSource.PlayClipAtPoint(clip, transform.position, volume);

            if (logOnPlay) Debug.Log($"[GrabSfx] 播放{tag}音效：{clip.name}");
        }
    }
}
