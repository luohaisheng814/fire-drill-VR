using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 灭火剂类型（第 28 轮）：三种情景各配一瓶对路的灭火器，
    /// 喷射粒子形态 / 颜色 / 音效 / 熄灭表现都按类型区分。
    /// </summary>
    public enum ExtinguisherKind
    {
        /// <summary>ABC 干粉：纸张、木材等 A 类固体火，也兼容电器火；留在现场有粉末残留。</summary>
        PowderABC = 0,
        /// <summary>二氧化碳：电气设备、精密仪器；喷射是白色气雾，不留残渣。</summary>
        CO2 = 1,
        /// <summary>水基 / 湿化学：食用油、小家电等 F 类火；喷的是带水滴的雾，靠冷却 + 皂化覆盖。</summary>
        WetChemical = 2
    }

    /// <summary>每种灭火剂的展示名、适用火源、配色与喷射形态参数。</summary>
    public static class ExtinguisherKindInfo
    {
        /// <summary>瓶子标签上的短名。</summary>
        public static string Name(ExtinguisherKind kind)
        {
            switch (kind)
            {
                case ExtinguisherKind.CO2: return "二氧化碳 CO₂";
                case ExtinguisherKind.WetChemical: return "水基（湿化学）";
                default: return "ABC 干粉";
            }
        }

        /// <summary>适用火源说明（HUD 与瓶身标签共用）。</summary>
        public static string SuitableFor(ExtinguisherKind kind)
        {
            switch (kind)
            {
                case ExtinguisherKind.CO2: return "电气设备 / 精密仪器";
                case ExtinguisherKind.WetChemical: return "食用油 / 小家电（F 类）";
                default: return "纸张 / 木材（A 类）+ 电器";
            }
        }

        /// <summary>瓶身色带与粒子的主色。</summary>
        public static Color BandColor(ExtinguisherKind kind)
        {
            switch (kind)
            {
                case ExtinguisherKind.CO2: return new Color(0.16f, 0.42f, 0.78f);         // 蓝
                case ExtinguisherKind.WetChemical: return new Color(0.34f, 0.68f, 0.86f); // 淡蓝
                default: return new Color(0.86f, 0.78f, 0.42f);                           // 米黄
            }
        }

        /// <summary>喷射粒子颜色：CO₂ 偏蓝白半透明气雾，干粉暖白更浓，水基带淡蓝水雾。</summary>
        public static Color SprayColor(ExtinguisherKind kind)
        {
            switch (kind)
            {
                case ExtinguisherKind.CO2: return new Color(0.90f, 0.95f, 1.00f, 0.85f);
                case ExtinguisherKind.WetChemical: return new Color(0.82f, 0.93f, 1.00f, 0.90f);
                default: return new Color(0.97f, 0.95f, 0.89f, 0.95f);
            }
        }

        /// <summary>
        /// 喷射形态：CO₂ 是高速细气柱、几乎不下落；干粉是宽锥慢速浓粉云；
        /// 水基介于两者之间、水滴有明显下坠。
        /// </summary>
        public static SprayProfile Profile(ExtinguisherKind kind)
        {
            switch (kind)
            {
                case ExtinguisherKind.CO2:
                    return new SprayProfile
                    {
                        rate = 600f, startSpeed = 5.6f, coneAngle = 11f, lifetime = 0.90f,
                        startSize = 0.140f, sizeGrowth = 3.2f, gravity = 0.06f, noiseStrength = 0.12f
                    };
                case ExtinguisherKind.WetChemical:
                    return new SprayProfile
                    {
                        rate = 480f, startSpeed = 4.0f, coneAngle = 10f, lifetime = 1.2f,
                        startSize = 0.130f, sizeGrowth = 4.2f, gravity = 0.95f, noiseStrength = 0.28f
                    };
                default:
                    return new SprayProfile
                    {
                        rate = 520f, startSpeed = 3.2f, coneAngle = 16f, lifetime = 1.6f,
                        startSize = 0.165f, sizeGrowth = 7.0f, gravity = 0.50f, noiseStrength = 0.50f
                    };
            }
        }
    }

    /// <summary>一套喷射参数（由 ExtinguisherSpray 读取）。</summary>
    public struct SprayProfile
    {
        public float rate;
        public float startSpeed;
        public float coneAngle;
        public float lifetime;
        public float startSize;
        public float sizeGrowth;
        public float gravity;
        public float noiseStrength;
    }
}
