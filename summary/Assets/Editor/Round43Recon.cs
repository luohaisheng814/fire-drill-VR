// 第 43 轮侦查工具（编辑模式，只读）：把「墙 / 窗 / 纸张火附近物体」的现场数据打到日志，
// 用来定位 ① 电源总闸旁北窗的闪烁来源 ② 纸张起火处缺失的墙面。
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FireSafety.EditorTools
{
    public static class Round43Recon
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";

        public static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath);
            var sb = new StringBuilder();

            // 1) 所有墙体（名字含 wall，忽略大小写）
            sb.AppendLine("=== 墙体对象（含包围盒） ===");
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null) continue;
                string n = t.name;
                if (!(n.IndexOf("wall", System.StringComparison.OrdinalIgnoreCase) >= 0) &&
                    !(n.StartsWith("WindowWall"))) continue;
                var r = t.GetComponent<Renderer>();
                string bounds = r != null
                    ? string.Format("bounds x[{0:F2},{1:F2}] y[{2:F2},{3:F2}] z[{4:F2},{5:F2}]",
                        r.bounds.min.x, r.bounds.max.x, r.bounds.min.y, r.bounds.max.y, r.bounds.min.z, r.bounds.max.z)
                    : "(no renderer)";
                sb.AppendLine(string.Format("{0}  pos={1}  scale={2}  active={3}  {4}",
                    n, t.position.ToString("F3"), t.lossyScale.ToString("F3"),
                    t.gameObject.activeSelf, bounds));
            }

            // 2) 窗与窗外构件
            sb.AppendLine();
            sb.AppendLine("=== Window_* / SkyBackdrop / Daylight / 排烟 ===");
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null) continue;
                string n = t.name;
                if (!(n.StartsWith("Window_") || n == "SkyBackdrop" || n.StartsWith("Daylight_") ||
                      n.StartsWith("WindowSmoke_") || n.StartsWith("WindowFeed_"))) continue;
                string parent = t.parent != null ? t.parent.name : "-";
                string extra = "";
                var r = t.GetComponent<Renderer>();
                if (r != null && r.sharedMaterial != null)
                {
                    var m = r.sharedMaterial;
                    extra += string.Format(" mat={0} queue={1} color={2}",
                        m.name, m.renderQueue, m.HasProperty("_Color") ? m.GetColor("_Color").ToString() : "n/a");
                    if (m.HasProperty("_Mode")) extra += " mode=" + m.GetFloat("_Mode");
                }
                var ps = t.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    var em = ps.emission;
                    extra += string.Format(" smoke rate={0:F1} alive={1} playing={2}",
                        em.rateOverTime.constant, ps.particleCount, ps.isPlaying);
                }
                var lt = t.GetComponent<Light>();
                if (lt != null) extra += string.Format(" light type={0} intensity={1:F2} range={2:F2} culling={3}",
                    lt.type, lt.intensity, lt.range, lt.cullingMask);
                sb.AppendLine(string.Format("{0}  parent={1}  pos={2}  scale={3}  active={4}{5}",
                    n, parent, t.position.ToString("F3"), t.lossyScale.ToString("F3"), t.gameObject.activeSelf, extra));
            }

            // 3) 纸张火周边（x[-6.3,-5.2], z[-5.2,-4.0]）所有带渲染的对象，看有没有墙板缺失/错位
            sb.AppendLine();
            sb.AppendLine("=== 纸张火周边物体（x -6.3~-5.2, z -5.2~-4.0） ===");
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null) continue;
                var p = t.position;
                if (p.x < -6.3f || p.x > -5.2f || p.z < -5.2f || p.z > -4.0f) continue;
                var r = t.GetComponent<Renderer>();
                if (r == null) continue;
                sb.AppendLine(string.Format("{0}  pos={1}  scale={2}  active={3}  bounds z[{4:F2},{5:F2}] y[{6:F2},{7:F2}]",
                    t.name, p.ToString("F3"), t.lossyScale.ToString("F3"), t.gameObject.activeSelf,
                    r.bounds.min.z, r.bounds.max.z, r.bounds.min.y, r.bounds.max.y));
            }

            // 4) 西墙 x≈-6.755 实体（看 z 覆盖是否连续）
            sb.AppendLine();
            sb.AppendLine("=== 西墙 x≈-6.75 物体 ===");
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t == null) continue;
                var r = t.GetComponent<Renderer>();
                if (r == null) continue;
                var b = r.bounds;
                if (Mathf.Abs(b.center.x - (-6.75f)) > 0.45f) continue;
                sb.AppendLine(string.Format("{0}  center={1}  z[{2:F2},{3:F2}] y[{4:F2},{5:F2}] mat={6} active={7}",
                    t.name, b.center.ToString("F2"), b.min.z, b.max.z, b.min.y, b.max.y,
                    r.sharedMaterial != null ? r.sharedMaterial.name : "-", t.gameObject.activeInHierarchy));
            }

            string text = sb.ToString();
            System.IO.File.WriteAllText("Logs/g43_recon_report.txt", text);
            Debug.Log("[G43RECON]\n" + text);
            Debug.Log("[G43RECON] DONE");
            EditorApplication.Exit(0);
        }
    }
}
