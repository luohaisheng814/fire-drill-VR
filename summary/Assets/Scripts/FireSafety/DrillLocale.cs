using System;
using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 第 35 轮：全局中英文切换。
    /// 只存一个枚举 + PlayerPrefs，所有新 UI 通过 T(中文, English) 取文案；
    /// 旧的中文业务文案仍可原样调用 T("中文")（省略英文时回退中文）。
    /// </summary>
    public static class DrillLocale
    {
        public enum Lang { Chinese = 0, English = 1 }

        const string PrefKey = "fire_drill_language";

        static Lang _lang;
        static bool _loaded;

        /// <summary>语言变化事件（世界空间 UI 重建文案 / 开始面板刷新）。</summary>
        public static event Action Changed;

        public static Lang Language
        {
            get
            {
                if (!_loaded)
                {
                    _loaded = true;
                    _lang = (Lang)Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, 0), 0, 1);
                }
                return _lang;
            }
            set
            {
                _loaded = true;
                if (_lang == value) return;
                _lang = value;
                PlayerPrefs.SetInt(PrefKey, (int)_lang);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static bool English => Language == Lang.English;

        public static void Toggle() => Language = English ? Lang.Chinese : Lang.English;

        public static void SetEnglish(bool english) => Language = english ? Lang.English : Lang.Chinese;

        /// <summary>语言未变也强制广播一次，用于运行时首次显示、需要重建文本网格的 UI。</summary>
        public static void NotifyChanged() => Changed?.Invoke();

        /// <summary>按当前语言返回文案；英文缺失时回退中文。</summary>
        public static string T(string chinese, string english = null)
        {
            if (!English) return chinese;
            return string.IsNullOrEmpty(english) ? chinese : english;
        }

        public static string SwitchButtonLabel => English ? "中文" : "English";

        /// <summary>类型名（短）。</summary>
        public static string KindName(ExtinguisherKind kind)
        {
            switch (kind)
            {
                case ExtinguisherKind.CO2:
                    return T("二氧化碳 CO₂", "CO₂");
                case ExtinguisherKind.WetChemical:
                    return T("水基灭火器", "Wet chemical");
                default:
                    return T("ABC 干粉", "ABC powder");
            }
        }

        public static string KindSuitable(ExtinguisherKind kind)
        {
            switch (kind)
            {
                case ExtinguisherKind.CO2:
                    return T("电气设备 / 精密仪器", "Electrical / electronics");
                case ExtinguisherKind.WetChemical:
                    return T("食用油 / 小家电（F 类）", "Cooking oil / appliances");
                default:
                    return T("纸张 / 木材（A 类）", "Paper / wood (Class A)");
            }
        }
    }
}
