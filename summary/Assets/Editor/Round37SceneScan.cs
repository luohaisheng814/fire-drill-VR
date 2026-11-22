#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FireSafety.EditorTools
{
    /// <summary>第37轮布点前侦察：导出墙体/窗户/桌椅柜体包围盒，用于选纸张新址与窗户位置。</summary>
    public static class Round37SceneScan
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string OutPath = "Logs/round37_scan.txt";

        [MenuItem("FireSafety/第37轮 场景扫描")]
        public static void Run()
        {
            Directory.CreateDirectory("Logs");
            EditorSceneManager.OpenScene(ScenePath);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== 有 Renderer 的物体（名称 / 中心 / 尺寸 / 包围盒） ===");
            var roots = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
                .Where(t => t.parent == null)
                .OrderBy(t => t.name).ToList();
            var list = new List<Transform>();
            foreach (var r in roots) Collect(r, list);
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var t in list)
            {
                var rend = t.GetComponent<Renderer>();
                if (rend == null) continue;
                var b = rend.bounds;
                sb.AppendLine(string.Format("{0,-42} pos={1} size={2} min={3} max={4}",
                    t.name, V(t.position), V(b.size), V(b.min), V(b.max)));
            }

            sb.AppendLine();
            sb.AppendLine("=== 西南区 x[-7,-1] z[-4.2,0.2] 内所有 Renderer ===");
            foreach (var t in list)
            {
                var rend = t.GetComponent<Renderer>();
                if (rend == null) continue;
                var b = rend.bounds;
                if (b.max.x < -7f || b.min.x > -1f || b.max.z < -4.2f || b.min.z > 0.2f) continue;
                sb.AppendLine(string.Format("{0,-42} pos={1} size={2} min={3} max={4}",
                    t.name, V(t.position), V(b.size), V(b.min), V(b.max)));
            }

            sb.AppendLine();
            sb.AppendLine("=== 名称含墙/窗/门/桌/柜/沙发/台 ===");
            foreach (var t in list)
            {
                if (!(t.name.Contains("Wall") || t.name.Contains("wall") || t.name.Contains("Window")
                    || t.name.Contains("window") || t.name.Contains("Door") || t.name.Contains("door")
                    || t.name.Contains("Desk") || t.name.Contains("desk") || t.name.Contains("Table")
                    || t.name.Contains("table") || t.name.Contains("Sofa") || t.name.Contains("Cabinet")
                    || t.name.Contains("Shelf") || t.name.Contains("Rack") || t.name.Contains("Counter")
                    || t.name.Contains("printer") || t.name.Contains("Printer"))) continue;
                var b = t.GetComponent<Renderer>() != null ? t.GetComponent<Renderer>().bounds : new Bounds(t.position, Vector3.zero);
                sb.AppendLine(string.Format("{0,-42} pos={1} size={2} min={3} max={4}",
                    t.name, V(t.position), V(b.size), V(b.min), V(b.max)));
            }

            File.WriteAllText(OutPath, sb.ToString());
            Debug.Log("[R37] 场景扫描完成 → " + OutPath + "，共 " + list.Count + " 个 Transform");
            EditorApplication.Exit(0);
        }

        static void Collect(Transform t, List<Transform> list)
        {
            list.Add(t);
            for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i), list);
        }

        static string V(Vector3 v) => string.Format("({0:F2},{1:F2},{2:F2})", v.x, v.y, v.z);
    }
}
#endif
