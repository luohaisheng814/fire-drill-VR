using UnityEngine;
using UnityEngine.Events;

namespace FireSafety
{
    /// <summary>火灾警报：延时拉响、循环警报音、红色爆闪灯，并上报步骤 ②。</summary>
    [RequireComponent(typeof(AudioSource))]
    public class FireAlarm : MonoBehaviour
    {
        [Header("警报音")]
        public AudioSource sirenSource;
        public AudioClip sirenClip;
        [Range(0f, 1f)] public float sirenVolume = 0.8f;
        [Tooltip("进入场景后延迟多少秒拉响警报")]
        public float startDelay = 2f;

        [Header("爆闪灯")]
        public Light[] alarmLights;
        public Color alarmColor = Color.red;
        public float blinkHz = 2f;
        public float maxIntensity = 2f;

        [Header("流程")]
        public bool autoStart = true;
        public UnityEvent onAlarmStarted;
        public bool completeStep2 = true;

        public bool IsRinging { get; private set; }

        private Color[] _colors;

        private void Reset() { sirenSource = GetComponent<AudioSource>(); }

        private void Awake()
        {
            if (!sirenSource) sirenSource = GetComponent<AudioSource>();
            if (sirenClip == null) sirenClip = Resources.Load<AudioClip>("Sounds/BGM/fire_alarm_siren");
            if (sirenSource)
            {
                sirenSource.playOnAwake = false;
                sirenSource.loop = true;
                sirenSource.spatialBlend = 1f;
            }
            if (alarmLights != null)
            {
                _colors = new Color[alarmLights.Length];
                for (int i = 0; i < alarmLights.Length; i++)
                    if (alarmLights[i]) _colors[i] = alarmLights[i].color;
            }
        }

        private void Start()
        {
            if (autoStart) Invoke(nameof(StartAlarm), Mathf.Max(0f, startDelay));
        }

        public void StartAlarm()
        {
            if (IsRinging) return;
            IsRinging = true;

            if (sirenSource && sirenClip)
            {
                sirenSource.clip = sirenClip;
                sirenSource.volume = sirenVolume;
                sirenSource.Play();
            }

            onAlarmStarted?.Invoke();
            if (completeStep2 && FireDrillManager.Instance)
                FireDrillManager.Instance.ForceCompleteUpTo(DrillStep.AlarmRinging);

            Debug.Log("[Alarm] 火灾警报已拉响");
        }

        public void StopAlarm()
        {
            IsRinging = false;
            if (sirenSource) sirenSource.Stop();
            if (alarmLights != null && _colors != null)
                for (int i = 0; i < alarmLights.Length; i++)
                    if (alarmLights[i]) alarmLights[i].color = _colors[i];
        }

        private void Update()
        {
            if (!IsRinging || alarmLights == null) return;

            float t = (Mathf.Sin(Time.time * Mathf.PI * 2f * blinkHz) + 1f) * 0.5f;
            for (int i = 0; i < alarmLights.Length; i++)
            {
                var l = alarmLights[i];
                if (!l) continue;
                l.color = alarmColor;
                l.intensity = t * maxIntensity;
            }
        }
    }
}