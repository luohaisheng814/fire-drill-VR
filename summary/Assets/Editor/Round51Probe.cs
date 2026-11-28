#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FireSafety;

namespace FireSafety.EditorTools
{
    /// <summary>
    /// 第 51 轮玩家视角探针：1.62 米眼高、1600×900。
    /// 验证韩国标准灭火器、情形2三个出生点、火焰/烟雾、双逃生路线、门右击直接打开。
    /// 批处理执行：-executeMethod FireSafety.EditorTools.Round51Probe.Run（不要 -nographics / -quit）
    /// </summary>
    public static class Round51Probe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = @"D:\43763\VR\summary\Logs\shots";
        const float HardTimeout = 150f;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();
        static bool CandidateMode;
        static float _t0, _phaseT;
        static int _phase;
        static bool _entered;
        static Camera _camera;
        static Transform _cam;
        static StartScreen _start;
        static DrillScenario _scenario;
        static StageTwoFlow _stageTwo;
        static GuidanceArrows _arrows;
        static ExitDoor _exitDoor;

        public static void Run()
        {
            CandidateMode = Environment.GetEnvironmentVariable("G51_CAND") == "1";
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear();
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[G51] " + s); Log.Add(s); }
        static void Fail(string s) { if (Fails.Contains(s)) return; Debug.LogError("[G51] × " + s); Log.Add("× " + s); Fails.Add(s); }
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
                    _stageTwo = UnityEngine.Object.FindFirstObjectByType<StageTwoFlow>();
                    _arrows = UnityEngine.Object.FindFirstObjectByType<GuidanceArrows>();
                    _exitDoor = UnityEngine.Object.FindFirstObjectByType<ExitDoor>();
                    Check("场景组件齐全（Start/Scenario/StageTwo/Arrows/ExitDoor）",
                        _start != null && _scenario != null && _stageTwo != null && _arrows != null && _exitDoor != null);
                    if (_scenario == null || _stageTwo == null) { Finish("缺组件"); return; }
                    if (_start != null) _start.SetVisible(false);
                    DumpOfficeGrid();
                    DumpSpawnCandidates();
                    _scenario.StartFormalWithCase(DrillScenario.OperationCase.Developed, 0);
                    if (CandidateMode) { Next(20); return; }
                    Next(1);
                    return;

                case 1:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 2.6f) return;
                    Check("进入独立情形 2（火势已发展）", _scenario.StandaloneCase &&
                        _scenario.operationCase == DrillScenario.OperationCase.Developed,
                        "case=" + _scenario.operationCase);
                    Check("情形2 出口门规则：不设门前火", !_stageTwo.exitFirePresent);
                    var fire = _scenario.caseFire;
                    Check("情形2 主火已点燃", fire != null && fire.IsBurning && !fire.IsExtinguished);
                    if (fire != null)
                    {
                        int flames = 0, smokes = 0;
                        foreach (var t in fire.GetComponentsInChildren<Transform>(true))
                        {
                            if (t.name.StartsWith("FlameDecor_Base")) flames++;
                            if (t.name.StartsWith("SmokeDecor_Base")) smokes++;
                        }
                        Check("情形2 火场至少 6 簇附加火 / 3 股烟", flames >= 6 && smokes >= 3,
                            "flames=" + flames + " smokes=" + smokes);
                    }
                    Check("情形2 局部烟更明显（速率≥40 / 粒径 0.8~1.1，避免大黑球）",
                        _scenario.caseSmoke != null &&
                        _scenario.caseSmoke.emission.rateOverTime.constant >= 40f &&
                        _scenario.caseSmoke.main.startSize.constant >= 0.8f &&
                        _scenario.caseSmoke.main.startSize.constant <= 1.1f);
                    var s0 = _scenario.caseSpawnPoints[0];
                    float d0 = Flat(s0.position, fire != null ? fire.transform.position : Vector3.zero);
                    Check("出生点 1 离火源≥4 m", d0 >= 4.0f, "d=" + d0.ToString("F2"));
                    CheckSpawnClear("出生点 1", s0);
                    PlaceAtSpawn(0);
                    ShotHere("g51_case2_spawn1.png");
                    Next(2);
                    return;
                }

                case 2:
                    if (Time.realtimeSinceStartup - _phaseT < 0.6f) return;
                    PlaceAtSpawn(1);
                    Check("出生点 2 离火源≥4 m",
                        Flat(_scenario.caseSpawnPoints[1].position, _scenario.caseFire.transform.position) >= 4.0f);
                    CheckSpawnClear("出生点 2", _scenario.caseSpawnPoints[1]);
                    ShotHere("g51_case2_spawn2.png");
                    Next(3);
                    return;

                case 3:
                    if (Time.realtimeSinceStartup - _phaseT < 0.6f) return;
                    PlaceAtSpawn(2);
                    Check("出生点 3 离火源≥4 m",
                        Flat(_scenario.caseSpawnPoints[2].position, _scenario.caseFire.transform.position) >= 4.0f);
                    CheckSpawnClear("出生点 3", _scenario.caseSpawnPoints[2]);
                    ShotHere("g51_case2_spawn3.png");
                    Next(4);
                    return;

                case 4:
                    if (Time.realtimeSinceStartup - _phaseT < 0.6f) return;
                    PlaceAtSpawn(0);
                    var fire2 = _scenario.caseFire;
                    var s0p = _scenario.caseSpawnPoints[0];
                    PlacePlayer(new Vector3(s0p.position.x, 0f, s0p.position.z),
                        new Vector3(fire2.transform.position.x, 1.15f, fire2.transform.position.z));
                    ShotHere("g51_case2_fire_spawn.png");
                    Next(5);
                    return;

                case 5:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.7f) return;
                    _scenario.BeginAlarmEvacuation();
                    Next(6);
                    return;
                }

                case 6:
                    if (Time.realtimeSinceStartup - _phaseT < 2.6f) return;
                    Check("报警后进入疏散（不要求灭火）", _stageTwo.CurrentStage == StageTwoFlow.Stage.Evacuating,
                        "stage=" + _stageTwo.CurrentStage);
                    Check("疏散提示不再提门前灭火", _arrows != null);
                    Check("黄色主路线已铺出", _arrows.VisibleArrowCount >= 1, "main=" + _arrows.VisibleArrowCount);
                    Check("青色备用路线已铺出", _arrows.VisibleAlternateArrowCount >= 1 && _arrows.AlternatePathPointCount >= 2,
                        "alt=" + _arrows.VisibleAlternateArrowCount + " points=" + _arrows.AlternatePathPointCount);
                    PlaceAtSpawn(0);
                    ShotHere("g51_case2_routes.png");
                    // 再拍一张「转身看向东侧出口」的玩家视角：验证黄/青路线在出口方向上确实可读。
                    var sp0 = _scenario.caseSpawnPoints[0];
                    PlacePlayer(new Vector3(sp0.position.x, 0f, sp0.position.z),
                        new Vector3(3.35f, 0.55f, 1.02f));
                    ShotHere("g51_case2_routes_exit.png");
                    Next(60);
                    return;

                case 60:
                    if (Time.realtimeSinceStartup - _phaseT < 0.9f) return;
                    PlaceAtSpawn(1);
                    Check("出生点 2 也能生成青色备用路线", _arrows.VisibleAlternateArrowCount >= 1 && _arrows.AlternatePathPointCount >= 2,
                        "alt=" + _arrows.VisibleAlternateArrowCount + " points=" + _arrows.AlternatePathPointCount);
                    ShotHere("g51_case2_spawn2_routes.png");
                    Next(61);
                    return;

                case 61:
                    if (Time.realtimeSinceStartup - _phaseT < 0.9f) return;
                    PlaceAtSpawn(2);
                    Check("出生点 3 也能生成青色备用路线", _arrows.VisibleAlternateArrowCount >= 1 && _arrows.AlternatePathPointCount >= 2,
                        "alt=" + _arrows.VisibleAlternateArrowCount + " points=" + _arrows.AlternatePathPointCount);
                    ShotHere("g51_case2_spawn3_routes.png");
                    Next(7);
                    return;

                case 7:
                    if (Time.realtimeSinceStartup - _phaseT < 1.4f) return;
                    PlacePlayer(new Vector3(3.30f, 0f, 1.02f), new Vector3(5.25f, 1.4f, 1.02f));
                    bool arrived = _stageTwo.TryArriveLobby(true);
                    Check("到达大厅后进入出口阶段", arrived || _stageTwo.CurrentStage == StageTwoFlow.Stage.ExitOpen,
                        "stage=" + _stageTwo.CurrentStage);
                    Check("门前无火：门已解锁但未自动打开", _exitDoor.IsUnlocked && !_exitDoor.IsOpen,
                        "unlocked=" + _exitDoor.IsUnlocked + " open=" + _exitDoor.IsOpen);
                    ShotHere("g51_case2_door_unlocked.png");
                    Next(8);
                    return;

                case 8:
                    if (Time.realtimeSinceStartup - _phaseT < 1.0f) return;
                    PlacePlayer(new Vector3(4.30f, 0f, 1.02f), new Vector3(5.25f, 1.4f, 1.02f));
                    bool opened = _exitDoor.TryOpenByPlayer(4.5f);
                    Check("门前无火时右击门扇直接打开（无其他条件）", opened);
                    Next(9);
                    return;

                case 9:
                    if (Time.realtimeSinceStartup - _phaseT < 1.4f) return;
                    Check("门扇已打开且可通过", _exitDoor.IsOpen && _exitDoor.IsPassable,
                        "open=" + _exitDoor.IsOpen + " pass=" + _exitDoor.IsPassable);
                    ShotHere("g51_case2_door_open.png");
                    Next(10);
                    return;

                case 10:
                    if (Time.realtimeSinceStartup - _phaseT < 0.5f) return;
                    ShotExtinguisher("Extinguisher01", new Vector3(-4.15f, 0f, 3.05f), "g51_ext01_standard.png");
                    Next(11);
                    return;

                case 11:
                    if (Time.realtimeSinceStartup - _phaseT < 0.5f) return;
                    ShotExtinguisher("Extinguisher02", new Vector3(-5.55f, 0f, -1.15f), "g51_ext02_standard.png");
                    Next(12);
                    return;

                case 12:
                    if (Time.realtimeSinceStartup - _phaseT < 0.5f) return;
                    ShotExtinguisher("Extinguisher03", new Vector3(5.95f, 0f, -3.35f), "g51_ext03_standard.png");
                    Next(13);
                    return;

                case 13:
                    if (Time.realtimeSinceStartup - _phaseT < 0.4f) return;
                    Finish("完成");
                    return;

                case 20:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 2.6f) return;
                    var fire = _scenario.caseFire;
                    Vector3 fp = fire.transform.position;
                    var pts = new[]
                    {
                        new Vector3(-6.20f, 0f, 1.50f),
                        new Vector3(-6.20f, 0f, 2.50f),
                        new Vector3(-5.70f, 0f, 2.50f),
                        new Vector3(0.80f, 0f, 3.50f),
                        new Vector3(1.50f, 0f, 3.50f),
                        new Vector3(-3.70f, 0f, -3.50f),
                        new Vector3(-2.20f, 0f, -4.00f),
                        new Vector3(-3.00f, 0f, -4.50f)
                    };
                    for (int i = 0; i < pts.Length; i++)
                    {
                        PlacePlayer(pts[i], new Vector3(fp.x, 1.10f, fp.z));
                        ShotHere("g51_cand_" + (i + 1) + ".png");
                    }
                    Finish("候选点截图");
                    return;
                }
            }
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static void DumpOfficeGrid()
        {
            var sb = new StringBuilder();
            sb.AppendLine("OFFICE_OPEN_GRID step=0.50  x=-6.8..6.8  z=-4.8..4.8  (#=blocked .=open F=fire)");
            for (float z = 4.8f; z >= -4.8f; z -= 0.5f)
            {
                sb.Append("z=").Append(z.ToString("F2").PadLeft(5)).Append("  ");
                for (float x = -6.8f; x <= 6.8f; x += 0.5f)
                {
                    bool fire = false;
                    foreach (var f in FireSource.Active)
                    {
                        if (f == null || !f.IsBurning || f.IsExtinguished) continue;
                        if (Mathf.Abs(f.transform.position.x - x) < 0.7f && Mathf.Abs(f.transform.position.z - z) < 0.7f) fire = true;
                    }
                    if (fire) { sb.Append('F'); continue; }
                    Vector3 a = new Vector3(x, 0.35f, z), b = new Vector3(x, 1.70f, z);
                    var hits = Physics.OverlapCapsule(a, b, 0.28f, ~0, QueryTriggerInteraction.Ignore);
                    bool blocked = false;
                    foreach (var c in hits)
                    {
                        if (c == null) continue;
                        if (c.GetComponentInParent<FireSource>() != null) continue;
                        if (c.GetComponentInParent<ExitDoor>() != null) continue;
                        if (c.name.StartsWith("Guide") || c.name.Contains("Arrow")) continue;
                        blocked = true; break;
                    }
                    sb.Append(blocked ? '#' : '.');
                }
                sb.AppendLine();
            }
            Debug.Log("[G51MAP]\n" + sb);
            File.WriteAllText(Path.Combine(ShotDir, "_round51_map.txt"), sb.ToString());
        }

        static void DumpSpawnCandidates()
        {
            var fire = _scenario != null ? _scenario.caseFire : null;
            if (fire == null) return;
            Vector3 fp = fire.transform.position;
            var sb = new StringBuilder();
            sb.AppendLine("SPAWN_CANDIDATES  open capsule r0.28, distance 4.0~7.0m, fire line-of-sight @1.20m");
            for (float z = 4.0f; z >= -4.3f; z -= 0.5f)
            {
                for (float x = -6.2f; x <= 6.2f; x += 0.5f)
                {
                    Vector3 a = new Vector3(x, 0.35f, z), b = new Vector3(x, 1.70f, z);
                    var hits = Physics.OverlapCapsule(a, b, 0.28f, ~0, QueryTriggerInteraction.Ignore);
                    foreach (var c in hits)
                    {
                        if (c == null || c.GetComponentInParent<FireSource>() != null ||
                            c.GetComponentInParent<ExitDoor>() != null) continue;
                        goto next;
                    }
                    float d = Flat(new Vector3(x, 0, z), fp);
                    if (d < 4.0f || d > 7.0f) continue;
                    Vector3 eye = new Vector3(x, 1.20f, z);
                    Vector3 target = new Vector3(fp.x, 1.20f, fp.z);
                    Vector3 dir = target - eye;
                    RaycastHit hit;
                    bool clear = !Physics.Raycast(eye, dir.normalized, out hit, dir.magnitude, ~0, QueryTriggerInteraction.Ignore)
                                 || hit.collider.GetComponentInParent<FireSource>() != null;
                    sb.AppendLine("  x=" + x.ToString("F2") + " z=" + z.ToString("F2") + " d=" + d.ToString("F2")
                                  + (clear ? "  FIRE_VISIBLE" : "  blocked:" + (hit.collider != null ? hit.collider.name : "?")
                                  + " @" + hit.distance.ToString("F1")));
                    next:;
                }
            }
            sb.AppendLine("END");
            File.WriteAllText(Path.Combine(ShotDir, "_round51_candidates.txt"), sb.ToString());
            Debug.Log("[G51CAND]\n" + sb);
        }

        static void PlaceAtSpawn(int i)
        {
            var s = _scenario.caseSpawnPoints[Mathf.Clamp(i, 0, _scenario.caseSpawnPoints.Length - 1)];
            Vector3 look = s.position + s.forward * 6f;
            look.y = 1.55f;
            PlacePlayer(new Vector3(s.position.x, 0f, s.position.z), look);
        }

        static void CheckSpawnClear(string label, Transform spawn)
        {
            if (spawn == null) { Check(label + " 存在", false); return; }
            Vector3 a = new Vector3(spawn.position.x, 0.35f, spawn.position.z);
            Vector3 b = new Vector3(spawn.position.x, 1.70f, spawn.position.z);
            var hits = Physics.OverlapCapsule(a, b, 0.28f, ~0, QueryTriggerInteraction.Ignore);
            int blocking = 0;
            string names = "";
            foreach (var c in hits)
            {
                if (c == null) continue;
                if (c.GetComponentInParent<FireSource>() != null) continue;
                if (c.GetComponentInParent<ExitDoor>() != null) continue;
                if (c.name.StartsWith("Guide") || c.name.Contains("Arrow")) continue;
                if (c.name.StartsWith("XR Origin")) continue;
                blocking++;
                if (names.Length < 60) names += c.name + ";";
            }
            Check(label + " 无家具/墙体卡位", blocking == 0, "blockers=" + blocking + (names.Length > 0 ? " " + names : ""));
        }

        static void ShotExtinguisher(string name, Vector3 feet, string shot)
        {
            var go = GameObject.Find(name);
            if (go == null) { Check(name + " 存在", false); return; }
            var sign = GameObject.Find(name + "_Sign");
            Check(name + " 有「소화기」标识", sign != null);
            Vector3 look = sign != null ? sign.transform.position : go.transform.position + Vector3.up * 1.2f;
            // 从牌面朝房间中心的一侧、退 1.8 m 拍；牌子贴在四面墙上，单靠 forward 可能正对墙内。
            Vector3 toRoom = new Vector3(-look.x, 0f, -look.z);
            if (toRoom.sqrMagnitude < 0.01f) toRoom = Vector3.forward;
            toRoom.Normalize();
            Vector3 stand = sign != null
                ? new Vector3(sign.transform.position.x, 0f, sign.transform.position.z) + toRoom * 1.8f
                : feet;
            PlacePlayer(stand, look);
            ShotHere(shot);
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
                File.WriteAllText(Path.Combine(ShotDir, "_round51.txt"),
                    string.Join("\n", Log.ToArray()) + "\n\nFAIL_COUNT=" + Fails.Count
                    + "\nPROBE_EXIT=" + (Fails.Count == 0 ? 0 : 1) + "\n");
            }
            catch (Exception e) { Debug.LogError("[G51] 报告写入失败：" + e.Message); }
            Debug.Log("[G51] " + tag + "，失败数=" + Fails.Count);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(Fails.Count == 0 ? 0 : 1);
        }
    }
}
#endif
