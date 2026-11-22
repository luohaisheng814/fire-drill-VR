using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 第 37 轮：窗户排烟。起火时让烟粒子从窗洞向外漂，火灭后停止发射。
    /// 每个窗户绑定一个 FireSource：电气/纸张/茶水间各自只在自己起火时冒烟。
    /// </summary>
    public class WindowSmoke : MonoBehaviour
    {
        [Tooltip("这个窗户对应的火源：只有它燃烧时才向外排烟")]
        public FireSource source;
        [Tooltip("留空则取本物体上的 ParticleSystem")]
        public ParticleSystem smoke;

        bool _playing;

        void Awake()
        {
            if (smoke == null) smoke = GetComponent<ParticleSystem>();
        }

        void Start()
        {
            if (smoke != null) smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _playing = false;
        }

        void Update()
        {
            if (smoke == null || source == null) return;
            bool burning = source.IsBurning && !source.IsExtinguished && source.gameObject.activeInHierarchy;
            if (burning && !_playing)
            {
                smoke.Play();
                _playing = true;
            }
            else if (!burning && _playing)
            {
                smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                _playing = false;
            }
        }

        /// <summary>自检/探针用：这个窗户当前是否在排烟。</summary>
        public bool IsPlaying => _playing;
    }
}
