using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FireSafety.EditorTools
{
    /// <summary>
    /// 第 44 轮侦查（只读，不改场景）：
    /// ① 打印所有墙/窗构件的包围盒（含 inactive），看西墙（纸张火那侧）哪一段没有实体；
    /// ② 对四面墙做垂直剖面射线扫描（4 个高度 × 每 0.25 米），直接列出「四层全没墙」的连续区间；
    /// ③ 打印北窗（电源总闸旁）全部子构件的包围盒与材质渲染队列，排查重叠面 / 共面 z-fighting。
    /// 结果写进 Logs/g44_recon_report.txt。
    /// </summary>
    public static class Round44Recon
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ReportPath = "Logs/g44_recon_report.txt";
        static readonly StringBuilder Sb = new StringBuilder();
        static readonly float[] Heights = { 0.60f, 1.40f, 2.20f, 2.80f };

        static void W(string s) { Sb.AppendLine(s); Debug.Log("[R44] " + s); }

        [MenuItem("Tools/FireSafety/第44轮侦查")]
        public static void Run()
        {
            if (EditorSceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath);

            Directory.CreateDirectory("Logs");
            Sb.Clear();

            W("===== A. 墙/窗构件包围盒（含 inactive） =====");
            var rends = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var seen = new HashSet<string>();
            foreach (var r in rends)
            {
                if (r == null) continue;
                string n = r.gameObject.name;
                if (!n.ToLowerInvariant().Contains("wall") && !n.Contains("Window")) continue;
                Bounds b = r.bounds;
                string key = n + "|" + b.min.ToString("F2");
                if (!seen.Add(key)) continue;
                W(string.Format("{0,-40} active={1,-5} mat={2,-22} q={3,-5} min=({4:F2},{5:F2},{6:F2}) max=({7:F2},{8:F2},{9:F2})",
                    n, r.gameObject.activeInHierarchy,
                    r.sharedMaterial != null ? r.sharedMaterial.name : "null",
                    r.sharedMaterial != null ? r.sharedMaterial.renderQueue : 0,
                    b.min.x, b.min.y, b.min.z, b.max.x, b.max.y, b.max.z));
            }

            W("");
            W("===== B. 北窗构件（电源总闸旁 Window_North） =====");
            var win = GameObject.Find("Window_North");
            if (win != null)
            {
                foreach (var t in win.GetComponentsInChildren<Transform>(true))
                {
                    var mr = t.GetComponent<MeshRenderer>();
                    var col = t.GetComponent<Collider>();
                    if (mr == null && col == null) continue;
                    Bounds b = mr != null ? mr.bounds : col.bounds;
                    W(string.Format("{0,-26} active={1,-5} mat={2,-20} q={3,-5} col={4,-5} min=({5:F2},{6:F2},{7:F2}) max=({8:F2},{9:F2},{10:F2})",
                        t.name, t.gameObject.activeInHierarchy,
                        mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.name : "-",
                        mr != null && mr.sharedMaterial != null ? mr.sharedMaterial.renderQueue : 0,
                        col != null,
                        b.min.x, b.min.y, b.min.z, b.max.x, b.max.y, b.max.z));
                }
            }
            else W("Window_North 未找到");

            W("");
            W("===== C. 四面墙剖面射线扫描 =====");
            ScanWall("西墙", new Vector3(-5.60f, 0f, 0f), Vector3.left, -8f, 4f, 0.25f, true);
            ScanWall("东墙", new Vector3(6.00f, 0f, 0f), Vector3.right, -8f, 4f, 0.25f, true);
            ScanWall("北墙", new Vector3(0f, 0f, 2.60f), Vector3.forward, -8f, 4f, 0.25f, false);
            ScanWall("南墙", new Vector3(0f, 0f, -6.80f), Vector3.back, -8f, 10f, 0.25f, false);

            W("");
            W("===== D. 纸张火周边 5 米内墙体 =====");
            Vector3 paperFire = new Vector3(-5.710f, 1.06f, -4.540f);
            foreach (var r in rends)
            {
                if (r == null) continue;
                if (!r.gameObject.name.ToLowerInvariant().Contains("wall")) continue;
                if (Vector3.Distance(r.bounds.center, paperFire) > 5f) continue;
                var b = r.bounds;
                W(string.Format("{0,-40} active={1,-5} min=({2:F2},{3:F2},{4:F2}) max=({5:F2},{6:F2},{7:F2})",
                    r.gameObject.name, r.gameObject.activeInHierarchy, b.min.x, b.min.y, b.min.z, b.max.x, b.max.y, b.max.z));
            }

            File.WriteAllText(ReportPath, Sb.ToString());
            Debug.Log("[R44] 报告写入 " + ReportPath);
            EditorApplication.Exit(0);
        }

        /// <summary>沿一条墙做剖面扫描：每个采样点测 4 个高度，四层全没命中即记为「缺口」。</summary>
        static void ScanWall(string label, Vector3 origin, Vector3 dir, float min, float max, float step, bool byZ)
        {
            W("--- " + label + "（起点 " + origin.ToString("F2") + "，方向 " + dir.ToString("F2") + "，打 2.4 米）---");
            float gapStart = float.NaN, lastGap = 0f; int gapCount = 0;
            for (float v = min; v <= max + 0.001f; v += step)
            {
                bool any = false;
                for (int i = 0; i < Heights.Length; i++)
                {
                    Vector3 from = byZ
                        ? new Vector3(origin.x, Heights[i], v)
                        : new Vector3(v, Heights[i], origin.z);
                    if (Physics.Raycast(from, dir, out RaycastHit hit, 2.4f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        any = true;
                        W(string.Format("  {0}={1:F2} y={2:F2} → {3}（{4:F2} 米）", byZ ? "z" : "x", v, Heights[i], hit.collider.name, hit.distance));
                    }
                }
                if (!any)
                {
                    if (float.IsNaN(gapStart)) gapStart = v;
                    lastGap = v; gapCount++;
                }
                else if (!float.IsNaN(gapStart))
                {
                    W(string.Format("  ✘ 缺口 {0} {1:F2}~{2:F2}（{3} 个采样点四层全空）", byZ ? "z" : "x", gapStart, lastGap, gapCount));
                    gapStart = float.NaN; gapCount = 0;
                }
            }
            if (!float.IsNaN(gapStart))
                W(string.Format("  ✘ 缺口 {0} {1:F2}~{2:F2}（{3} 个采样点四层全空）", byZ ? "z" : "x", gapStart, lastGap, gapCount));
        }
    }
}
