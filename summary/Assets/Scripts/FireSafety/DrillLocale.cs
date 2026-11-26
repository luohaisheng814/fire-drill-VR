using System;
using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 全局语言切换（第 35 轮中英，第 41 轮加韩文）。
    /// 所有 UI 通过 T(中文, English, 한국어) 取文案；韩文缺失时回退英文，再回退中文。
    /// </summary>
    public static class DrillLocale
    {
        public enum Lang { Chinese = 0, English = 1, Korean = 2 }

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
                    _lang = (Lang)Mathf.Clamp(PlayerPrefs.GetInt(PrefKey, 0), 0, 2);
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

        public static void SetLanguage(Lang lang) => Language = lang;

        public static bool English => Language == Lang.English;
        public static bool Korean => Language == Lang.Korean;
        public static bool Chinese => Language == Lang.Chinese;

        /// <summary>三语切换按钮的下一站（中 → 英 → 韩 → 中）。</summary>
        public static void Cycle()
        {
            Language = Language == Lang.Chinese ? Lang.English
                     : Language == Lang.English ? Lang.Korean
                     : Lang.Chinese;
        }

        public static void Toggle() => Cycle();

        public static void SetEnglish(bool english) => Language = english ? Lang.English : Lang.Chinese;

        /// <summary>语言未变也强制广播一次，用于运行时首次显示、需要重建文本网格的 UI。</summary>
        public static void NotifyChanged() => Changed?.Invoke();

        /// <summary>按当前语言返回文案：韩文 → 韩文/英文/中文依次回退；英文 → 英文/中文。</summary>
        public static string T(string chinese, string english = null, string korean = null)
        {
            if (Language == Lang.Korean)
                return !string.IsNullOrEmpty(korean) ? korean
                     : !string.IsNullOrEmpty(english) ? english : chinese;
            if (Language == Lang.English)
                return string.IsNullOrEmpty(english) ? chinese : english;
            return chinese;
        }

        /// <summary>语言名称（用各自语言书写）。</summary>
        public static string LanguageName(Lang lang)
        {
            switch (lang)
            {
                case Lang.English: return "English";
                case Lang.Korean: return "한국어";
                default: return "中文";
            }
        }

        public static string SwitchButtonLabel => Language == Lang.Chinese ? "English / 한국어"
                                               : Language == Lang.English ? "한국어 / 中文"
                                               : "中文 / English";

        public static string KindName(ExtinguisherKind kind)
        {
            switch (kind)
            {
                case ExtinguisherKind.CO2:
                    return T("二氧化碳 CO₂", "CO₂", "이산화탄소 CO₂");
                case ExtinguisherKind.WetChemical:
                    return T("水基灭火器", "Wet chemical", "수계 소화기");
                default:
                    return T("ABC 干粉", "ABC powder", "ABC 분말");
            }
        }

        public static string KindSuitable(ExtinguisherKind kind)
        {
            switch (kind)
            {
                case ExtinguisherKind.CO2:
                    return T("电气设备 / 精密仪器", "Electrical / electronics", "전기 설비 / 정밀 기기");
                case ExtinguisherKind.WetChemical:
                    return T("食用油 / 小家电（F 类）", "Cooking oil / appliances", "식용유 / 소형 가전 (F급)");
                default:
                    return T("纸张 / 木材（A 类）", "Paper / wood (Class A)", "종이 / 목재 (A급)");
            }
        }
    }
}
