using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 字体解析：UI 文字都从这里取字体，避免中文/韩文显示成方块。
    /// 优先级：Resources/Fonts/ChineseFont → 韩文语言时 Malgun Gothic → 系统微软雅黑 → 黑体/宋体 → Arial。
    /// 切换语言后调用 Invalidate() 让缓存重建。
    /// </summary>
    public static class UIFont
    {
        static Font cached;
        static string cachedKey;

        public static void Invalidate()
        {
            cached = null;
            cachedKey = null;
        }

        public static Font Resolve()
        {
            string key = DrillLocale.Language.ToString();
            if (cached != null && cachedKey == key) return cached;

            var resource = Resources.Load<Font>("Fonts/ChineseFont");
            if (resource != null) { cached = resource; cachedKey = key; return cached; }

            // 韩文：Malgun Gothic 同时带 Hangul / 拉丁 / 汉字，能混排中文标记
            string[] names = DrillLocale.Korean
                ? new[] { "Malgun Gothic", "맑은 고딕", "Gulim", "Microsoft YaHei", "SimHei" }
                : new[] { "Microsoft YaHei", "微软雅黑", "SimHei", "SimSun" };

            foreach (var name in names)
            {
                var f = Font.CreateDynamicFontFromOSFont(name, 32);
                if (f != null)
                {
                    cached = f;
                    cachedKey = key;
                    Debug.Log("[UIFont] 使用系统字体：" + name);
                    return cached;
                }
            }

            cached = Resources.GetBuiltinResource<Font>("Arial.ttf");
            cachedKey = key;
            return cached;
        }
    }
}
