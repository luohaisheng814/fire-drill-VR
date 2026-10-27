using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 中文字体解析：UI 文字都从这里取字体，避免中文显示成方块。
    /// 优先级：Resources/Fonts/ChineseFont（.ttf/.otf）→ 系统微软雅黑 → 黑体/宋体 → Arial。
    /// 打包到 Windows 时系统字体可用；如需跨平台，请把中文字体放进
    /// Assets/Resources/Fonts/ 并命名为 ChineseFont。
    /// </summary>
    public static class UIFont
    {
        static Font cached;

        public static Font Resolve()
        {
            if (cached != null) return cached;

            cached = Resources.Load<Font>("Fonts/ChineseFont");
            if (cached != null) return cached;

            foreach (var name in new[] { "Microsoft YaHei", "微软雅黑", "SimHei", "SimSun" })
            {
                var f = Font.CreateDynamicFontFromOSFont(name, 32);
                if (f != null)
                {
                    cached = f;
                    Debug.Log("[UIFont] 使用系统字体：" + name);
                    return cached;
                }
            }

            cached = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return cached;
        }
    }
}
