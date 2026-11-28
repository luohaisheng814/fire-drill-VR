#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using FireSafety;

namespace FireSafety.EditorTools
{
    /// <summary>
    /// 第 50 轮玩家视角探针：1.62 米眼高、1600×900。
    /// 验证多火焰/烟、情景一/二火场、答题 5 秒停留、结果 5 秒停留、第 6 页教学、避火逃生路线。
    /// 批处理执行：-executeMethod FireSafety.EditorTools.Round50Probe.Run（不要 -nographics / -quit）
    /// </summary>
    public static class Round50Probe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = @"D:\43763\VR\summary\Logs\shots";
        const float HardTimeout = 120f;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();
        static float _t0, _phaseT;
        static int _phase, _quizIndexAt1, _quizIndexAt4;
        static bool _entered;
        static Camera _camera;
        static Transform _cam;
        static StartScreen _start;
        static DrillScenario _scenario;
        static OnboardingScreen _ob;
        static GuidanceArrows _arrows;
        static ExtinguisherQuizPanel _quiz;

        public static void Run()
        {
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear();
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[G50] " + s); Log.Add(s); }
        static void Fail(string s) { if (Fails.Contains(s)) return; Debug.LogError("[G50] × " + s); Log.Add("× " + s); Fails.Add(s); }
        static void Check(string name, bool ok, string detail = "")
        {
            if (ok) Say("√ " + name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
            else Fail(name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
        }
        static void Next(int p) { _phase = p; _phaseT = Time.realtimeSinceStartup; }

        static void Tick()
        {
            if (!Application.isPlaying)
            {
                if (_t0 > 0f && Time.realtimeSinceStartup - _t0 > HardTimeout) EditorApplication.Exit(2);
                return;
            }
            if (!_entered)
            {
                _entered = true; _t0 = Time.realtimeSinceStartup;
                Say("进入 Play（玩家眼高 1.62 米 / 1600×900）");
            }
            if (Time.realtimeSinceStartup - _t0 > HardTimeout) { Finish("TIMEOUT"); return; }

            var cam = Camera.main;
            if (cam == null) return;
            _camera = cam; _cam = cam.transform;

            switch (_phase)
            {
                case 0:
                    if (Time.realtimeSinceStartup - _t0 < 3f) return;
                    var lang = UnityEngine.Object.FindFirstObjectByType<LanguageSelectScreen>();
                    if (lang != null) lang.Choose(DrillLocale.Lang.Chinese);
                    _start = UnityEngine.Object.FindFirstObjectByType<StartScreen>();
                    _scenario = UnityEngine.Object.FindFirstObjectByType<DrillScenario>();
                    _ob = UnityEngine.Object.FindFirstObjectByType<OnboardingScreen>();
                    _arrows = UnityEngine.Object.FindFirstObjectByType<GuidanceArrows>();
                    _quiz = UnityEngine.Object.FindFirstObjectByType<ExtinguisherQuizPanel>();
                    Check("场景组件齐全（Start/Scenario/Onboarding/Arrows/Quiz）",
                        _start != null && _scenario != null && _ob != null && _arrows != null && _quiz != null);
                    if (_scenario == null || _quiz == null) { Finish("缺组件"); return; }
                    if (_start != null) _start.SetVisible(false);
                    _scenario.StartFormal(0, 0);
                    Next(1);
                    return;

                case 1:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 2.2f) return;
                    Check("正式演练已开始（电气情景 / 固定出生点）",
                        _scenario.CurrentMode == DrillScenario.Mode.Formal && _scenario.CurrentScenarioIndex == 0,
                        "mode=" + _scenario.CurrentMode + " index=" + _scenario.CurrentScenarioIndex);
                    var dev = GameObject.Find("DeviceFire");
                    var devFire = dev != null ? dev.GetComponent<FireSource>() : null;
                    Check("设备火已点燃", devFire != null && devFire.IsBurning && !devFire.IsExtinguished);
                    Check("设备火有 5 簇附加火焰 / 3 股附加烟",
                        CountChildren(dev, "FlameDecor_Device") >= 5 && CountChildren(dev, "SmokeDecor_Device") >= 3,
                        "flames=" + CountChildren(dev, "FlameDecor_Device") + " smokes=" + CountChildren(dev, "SmokeDecor_Device"));
                    PlacePlayer(new Vector3(-0.20f, 0f, 1.00f), new Vector3(-2.40f, 1.05f, 2.90f));
                    ShotHere("g50v_scene1_fire.png");
                    Next(2);
                    return;
                }

                case 2:
                    if (Time.realtimeSinceStartup - _phaseT < 1.2f) return;
                    PlacePlayer(new Vector3(2.60f, 0f, 0.60f), new Vector3(-2.40f, 1.05f, 2.90f));
                    ShotHere("g50v_scene1_from_spawn.png");
                    Next(3);
                    return;

                case 3:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;
                    // 情景二纸张火：直接点燃用于视觉验收（不改场景存档）
                    var paper = FindAny("PaperFire");
                    var paperFire = paper != null ? paper.GetComponent<FireSource>() : null;
                    if (paperFire != null)
                    {
                        paper.SetActive(true);
                        paperFire.IgniteNow();
                    }
                    Check("纸张火有 4 簇附加火焰 / 2 股附加烟",
                        paper != null && CountChildren(paper, "FlameDecor_Paper") >= 4 && CountChildren(paper, "SmokeDecor_Paper") >= 2,
                        "flames=" + CountChildren(paper, "FlameDecor_Paper") + " smokes=" + CountChildren(paper, "SmokeDecor_Paper"));
                    PlacePlayer(new Vector3(-5.95f, 0f, -1.95f), new Vector3(-5.71f, 1.05f, -4.54f));
                    ShotHere("g50v_scene2_fire.png");
                    Next(4);
                    return;
                }

                case 4:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;
                    // 答题的 6 个界面：5 题 + 结果页。先答第 1 题，观察 1 秒 / 4 秒时是否仍停在同一题。
                    _quiz.enabled = true;
                    _quiz.Open(null);
                    Check("答题面板已打开（第 1 / 5 题）", _quiz.IsOpen && _quiz.QuestionIndex == 0,
                        "open=" + _quiz.IsOpen + " index=" + _quiz.QuestionIndex);
                    _quiz.AnswerOption(_quiz.questions[0].correctIndex);
                    Next(5);
                    _phaseT = Time.realtimeSinceStartup;
                    return;
                }

                case 5:
                    // 作答后 1 秒：应仍停留在第 1 题的讲解
                    if (Time.realtimeSinceStartup - _phaseT < 1.1f) return;
                    _quizIndexAt1 = _quiz.QuestionIndex;
                    Check("1.1 秒时仍停在第 1 题（旧版 1.3 秒会跳走）", _quiz.QuestionIndex == 0 && _quiz.IsOpen,
                        "index=" + _quiz.QuestionIndex);
                    ShotHere("g50v_quiz_q1_1s.png");
                    Next(6);
                    return;

                case 6:
                    // 作答后 4.2 秒：仍应能看到讲解与选项
                    if (Time.realtimeSinceStartup - _phaseT < 3.1f) return;
                    _quizIndexAt4 = _quiz.QuestionIndex;
                    Check("4.2 秒时仍在第 1 题讲解页", _quiz.QuestionIndex == 0 && _quiz.IsOpen,
                        "index=" + _quiz.QuestionIndex);
                    ShotHere("g50v_quiz_q1_4s.png");
                    Next(7);
                    return;

                case 7:
                    // 5.2 秒：应自动进入第 2 题
                    if (Time.realtimeSinceStartup - _phaseT < 1.0f) return;
                    Check("满 5 秒后才进入第 2 题", _quiz.QuestionIndex == 1,
                        "index=" + _quiz.QuestionIndex);
                    Next(8);
                    return;

                case 8:
                {
                    // 剩余 4 题逐题作答，每题等 5.2 秒；答完最后一题立刻进入结果页观察
                    if (Time.realtimeSinceStartup - _phaseT < 5.2f) return;
                    if (_quiz.QuestionIndex < _quiz.QuestionCount - 1)
                    {
                        int i = _quiz.QuestionIndex;
                        _quiz.AnswerOption(_quiz.questions[i].correctIndex);
                        _phaseT = Time.realtimeSinceStartup;
                        return;
                    }
                    if (_quiz.QuestionIndex == _quiz.QuestionCount - 1)
                        _quiz.AnswerOption(_quiz.questions[_quiz.QuestionIndex].correctIndex);
                    Next(9);
                    return;
                }

                case 9:
                    // 最后一题作答后仍需 5 秒讲解，随后才进入结果页
                    if (Time.realtimeSinceStartup - _phaseT < 5.2f) return;
                    Check("5 题答完且通过", _quiz.IsOpen && _quiz.Passed && !_quiz.Failed,
                        "correct=" + _quiz.CorrectCount + "/" + _quiz.QuestionCount);
                    Check("结果页刚出现时可见", _quiz.IsOpen);
                    ShotHere("g50v_quiz_result_1s.png");
                    Next(10);
                    return;

                case 10:
                    if (Time.realtimeSinceStartup - _phaseT < 3.0f) return;
                    Check("结果页 3 秒后仍可见（旧版 1.2 秒自动收起）", _quiz.IsOpen);
                    ShotHere("g50v_quiz_result_4s.png");
                    Next(11);
                    return;

                case 11:
                    // 结果页满 5 秒后自动收起，随后打开操作教学第 6 页
                    if (Time.realtimeSinceStartup - _phaseT < 2.2f) return;
                    bool closed = !_quiz.IsOpen;
                    if (_quiz.IsOpen) _quiz.Close();
                    Check("结果页满 5 秒后自动收起", closed);
                    _start.SetVisible(false);
                    _ob.Show();
                    for (int i = 0; i < _ob.PageCount - 1; i++) _ob.NextPage();
                    Next(12);
                    return;

                case 12:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;
                    Check("教学面板在第 6 页", _ob.IsOpen && _ob.PageIndex == _ob.PageCount - 1,
                        "page=" + (_ob.PageIndex + 1) + "/" + _ob.PageCount);
                    Check("第 6 页含韩国官方五步与门把手注意事项",
                        _ob.pages[_ob.PageIndex].Contains("소방청") && _ob.pages[_ob.PageIndex].Contains("门把手"));
                    ShotHere("g50v_tutorial_6.png");
                    Next(13);
                    return;
                }

                case 13:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.5f) return;
                    if (_quiz.IsOpen) _quiz.Close();
                    _ob.Close(false);
                    _start.SetVisible(false);
                    // 从正式演练的真实出生点走向东侧出口：这条路线本身就不穿火源危险区。
                    PlacePlayer(new Vector3(2.90f, 0f, 1.10f), new Vector3(5.25f, 1.00f, 1.02f));
                    _arrows.fireClearRadius = 1.8f;
                    _arrows.avoidBurningFires = true;
                    _arrows.BeginExitGuidance();
                    Next(14);
                    return;
                }

                case 14:
                    if (Time.realtimeSinceStartup - _phaseT < 1.0f) return;
                    Check("出口引导已启动并生成箭头", _arrows.Active && _arrows.VisibleArrowCount > 0,
                        "visible=" + _arrows.VisibleArrowCount);
                    Check("出口路线为从出生点到东侧出口的有效路径", _arrows.PathPointCount >= 2,
                        "points=" + _arrows.PathPointCount);
                    var player = GameObject.FindGameObjectWithTag("Player");
                    var deviceFireObj = FindAny("DeviceFire");
                    if (player != null && deviceFireObj != null && _arrows.exitTarget != null)
                    {
                        float d = PointSegmentDistance(deviceFireObj.transform.position,
                            player.transform.position, _arrows.exitTarget.position);
                        Check("出生点到出口的直线不穿过火源 1.8 米危险区", d >= 1.8f,
                            "distance=" + d.ToString("F2") + "m");
                    }
                    Check("出口标识显示安全出口与距离", _arrows.ExitSignVisible && _arrows.CurrentSignText.Contains("安全出口"),
                        _arrows.CurrentSignText);
                    ShotHere("g50v_exit_route.png");
                    Next(15);
                    return;

                case 15:
                    if (Time.realtimeSinceStartup - _phaseT < 0.5f) return;
                    Finish("DONE");
                    return;
            }
        }

        static int CountChildren(GameObject root, string prefix)
        {
            if (root == null) return 0;
            int n = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(prefix)) n++;
            return n;
        }

        static GameObject FindAny(string name)
        {
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t.name == name) return t.gameObject;
            return null;
        }

        static float PointSegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = new Vector3(b.x - a.x, 0f, b.z - a.z);
            Vector3 ap = new Vector3(p.x - a.x, 0f, p.z - a.z);
            float len2 = ab.sqrMagnitude;
            if (len2 < 0.0001f) return ap.magnitude;
            float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / len2);
            Vector3 closest = new Vector3(a.x, 0f, a.z) + ab * t;
            return (new Vector3(p.x, 0f, p.z) - closest).magnitude;
        }

        static void PlacePlayer(Vector3 feet, Vector3 lookAt)
        {
            var origin = GameObject.Find("XR Origin (Drill)");
            if (origin != null)
            {
                var t = origin.transform;
                t.position = new Vector3(feet.x, t.position.y, feet.z);
            }
            if (_cam != null)
            {
                var dir = (lookAt - new Vector3(feet.x, 1.62f, feet.z)).normalized;
                _cam.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }

        static void ShotHere(string name)
        {
            try
            {
                var cam = _camera;
                if (cam == null) return;
                const int w = 1600, h = 900;
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var oldTarget = cam.targetTexture; var oldEye = cam.stereoTargetEye;
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(ShotDir, name), tex.EncodeToPNG());
                Say("截图 " + name);
                UnityEngine.Object.Destroy(tex);
                cam.targetTexture = oldTarget; cam.stereoTargetEye = oldEye;
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }
            catch (Exception e) { Fail("截图失败 " + name + "：" + e.Message); }
        }

        static void Finish(string tag)
        {
            try
            {
                File.WriteAllText(Path.Combine(ShotDir, "_round50.txt"),
                    string.Join("\n", Log.ToArray()) + "\n\nFAIL_COUNT=" + Fails.Count
                    + "\nPROBE_EXIT=" + (Fails.Count == 0 ? 0 : 1) + "\n");
            }
            catch (Exception e) { Debug.LogError("[G50] 报告写入失败：" + e.Message); }
            Debug.Log("[G50] " + tag + "，失败数=" + Fails.Count);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(Fails.Count == 0 ? 0 : 1);
        }
    }
}
#endif
