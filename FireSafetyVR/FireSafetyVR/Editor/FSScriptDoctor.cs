#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FireSafetyVR
{
    /// <summary>
    /// 诊断工具：Unity 菜单 FireSafetyVR → 诊断脚本 后看 Console。
    /// 用于排查「Can't add script ... script class cannot be found」。
    /// 放进 Assets/Editor/ 下（Editor 文件夹里的脚本不参与打包）。
    /// </summary>
    public static class FSScriptDoctor
    {
        [MenuItem("FireSafetyVR/诊断脚本（查编译失败与类名冲突）")]
        static void Diagnose()
        {
            Debug.Log("========== FireSafetyVR 脚本诊断开始 ==========");

            var guids = AssetDatabase.FindAssets("t:MonoScript");
            var paths = guids.Select(AssetDatabase.GUIDToAssetPath)
                             .Where(p => p.EndsWith(".cs"))
                             .OrderBy(p => p).ToArray();

            Debug.Log("Assets 下共找到 " + paths.Length + " 个 .cs 文件");

            var nameToPaths = new Dictionary<string, List<string>>();
            int broken = 0;

            foreach (var p in paths)
            {
                var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(p);

                // GetClass() 返回 null = 这个脚本没编译成功（有编译错误 / 不在有效 Assembly 里）
                if (ms == null || ms.GetClass() == null)
                {
                    // Editor 文件夹里的普通静态类没有 MonoBehaviour，也算正常
                    if (p.Contains("/Editor/")) continue;
                    broken++;
                    Debug.LogWarning("[未编译成功] " + p + "\n→ 打开 Console 看红色报错，或检查文件名与类名是否一致");
                    continue;
                }

                string cls = ms.GetClass().Name;
                if (!nameToPaths.ContainsKey(cls)) nameToPaths[cls] = new List<string>();
                nameToPaths[cls].Add(p);
            }

            // 类名冲突（这是「class cannot be found」最常见的原因）
            bool anyDup = false;
            foreach (var kv in nameToPaths)
            {
                if (kv.Value.Count <= 1) continue;
                anyDup = true;
                Debug.LogError("[类名重复] " + kv.Key + " 同时存在于以下文件：\n  " +
                               string.Join("\n  ", kv.Value.ToArray()) +
                               "\n→ 删掉或改名其中一个，只保留一份");
            }
            if (!anyDup) Debug.Log("✅ 没有发现重复的类名");

            // 检查本项目的 9 个脚本是否齐全
            string[] need =
            {
                "GameManager", "ExtinguisherGrab", "FireExtinguisher", "FireTarget",
                "QuizPanel", "UIBuilder", "Billboard", "AudioManager", "XRHandInput"
            };
            foreach (var n in need)
            {
                if (nameToPaths.ContainsKey(n))
                    Debug.Log("✅ 已找到 " + n + "  (" + nameToPaths[n][0] + ")");
                else
                    Debug.LogError("❌ 缺少脚本 " + n + " —— 确认 FireSafetyVR/Scripts 里的 .cs 都放进 Assets 了");
            }

            Debug.Log("========== 诊断结束：未编译 " + broken + " 个，重复类名 " +
                      nameToPaths.Count(kv => kv.Value.Count > 1) + " 组 ==========");
        }

        [MenuItem("FireSafetyVR/一键修复：为本场景挂好全部组件")]
        static void AutoSetup()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (!scene.IsValid()) { Debug.LogError("没有打开的场景"); return; }

            // GameManager
            var gm = Object.FindObjectOfType<GameManager>();
            if (gm == null)
            {
                var go = new GameObject("GameManager");
                gm = go.AddComponent<GameManager>();
                Debug.Log("已创建 GameManager");
            }

            // AudioManager
            if (Object.FindObjectOfType<AudioManager>() == null)
            {
                var go = new GameObject("AudioManager");
                go.AddComponent<AudioManager>();
                Debug.Log("已创建 AudioManager");
            }

            // QuizPanel
            var quiz = Object.FindObjectOfType<QuizPanel>();
            if (quiz == null)
            {
                var go = new GameObject("QuizPanel");
                quiz = go.AddComponent<QuizPanel>();
                Debug.Log("已创建 QuizPanel");
            }

            // 把 QuizPanel 自动连到 GameManager 上（其余引用需手动拖）
            var so = new SerializedObject(gm);
            var qp = so.FindProperty("quizPanel");
            if (qp != null) qp.objectReferenceValue = quiz;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(gm);
            Debug.Log("✅ 自动挂接完成，请在 Inspector 里手动补：灭火器、火焰、手柄、音频 clip");
        }
    }
}
#endif
