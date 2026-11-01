#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Unity.XR.CoreUtils;
using FireSafety;

namespace FireSafety.EditorTools
{
    /// <summary>
    /// 一键搭建「办公室消防知识演练」场景的编辑器脚本（幂等，可反复执行）：
    ///   1. 创建 XR 锚点（XRRig + 相机 + 左右手柄控制器/射线/位姿驱动）
    ///   2. 用户移动：XRDrillLocomotion + CharacterController（摇杆移动/转身 + 键盘调试）
    ///   3. 开始界面：StartScreen（标题 + 玩法说明文本框 + 开始按钮 + 动画）
    ///   4. 火灾警报：FireAlarm（循环警笛 + 红色爆闪灯，点击开始后拉响）
    ///   5. 背景音乐：BackgroundMusic 接上新 BGM（office_ambient）与警报音（fire_alarm_siren）
    ///   6. 灭火器装配：ToggleGrab(抓取+音效) + Extinguisher(步骤/喷射/问答门控)
    ///      + SafetyPin(保险销) + ExtinguisherQuizPanel(常识问答) + GrabPoseMatcher(手型匹配)
    ///   7. EventSystem + XRUIInputModule：手柄射线可点按 UI
    /// 用法：
    ///   · 编辑器菜单：FireSafety → 一键搭建并保存场景
    ///   · 命令行批处理：Unity.exe -batchmode -quit -projectPath "D:\43763\VR\summary"
    ///       -executeMethod FireSafety.EditorTools.FireSafetySetup.RunSetupFromMenu
    /// </summary>
    public static class FireSafetySetup
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string InputAssetPath =
            "Assets/Samples/XR Interaction Toolkit/3.6.1/Starter Assets/XRI Default Input Actions.inputactions";
        const string FbxPath =
            "Assets/AK STUDIO ART/Fire Extinguisher/Models/Fire Extinguisher/Fire Extinguisher.fbx";
        const string RefDir = "Assets/Scripts/FireSafety/InputRefs";

        const string RigName = "XR Origin (Drill)";
        const string OffsetName = "Camera Offset";
        const string CameraName = "Main Camera";
        const string LeftHandName = "LeftHand Controller";
        const string RightHandName = "RightHand Controller";

        static InputActionAsset _asset;

        // ================================================================
        // 入口
        // ================================================================

        [MenuItem("FireSafety/一键搭建并保存场景")]
        public static void RunSetupFromMenu()
        {
            RunSetup();
        }

        public static void RunSetup()
        {
            Debug.Log("[Setup] ==== 开始一键搭建 FireSafetyOffice ====");
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (!scene.IsValid()) { Debug.LogError("[Setup] 打开场景失败：" + ScenePath); return; }

            _asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            if (_asset == null)
            {
                Debug.LogError("[Setup] 找不到 XRI 默认输入资产：" + InputAssetPath + "，先导入 XRI Starter Assets 样例。");
                return;
            }

            // 1. XR 锚点
            var rig = FindOrCreateRig();
            var cam = rig.Camera.transform;
            var leftHand = BuildController(rig, HandSide.Left);
            var rightHand = BuildController(rig, HandSide.Right);

            // 2. 移动
            SetupLocomotion(rig, cam);

            // 3. 开始界面
            SetupStartScreen();

            // 4. 警报
            SetupFireAlarm();

            // 5. 背景音乐
            SetupBackgroundMusic();

            // 5.5 演练倒计时（3 分钟上限）+ 地面引导箭头
            SetupDrillCountdown();
            SetupGuidanceArrows();

            // 6. 灭火器
            SetupExtinguisher("Extinguisher01", rig, leftHand, rightHand);
            SetupExtinguisher("Extinguisher02", rig, leftHand, rightHand);

            // 7. UI 输入
            SetupEventSystem();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log("[Setup] ==== 搭建完成，场景已保存：" + ScenePath + " ====");
            Debug.Log("[Setup] 提示：开始界面玩法说明文字请在场景 StartScreen 的 Instructions 字段修改；"
                      + "灭火器抓握姿态在 ToggleGrab.heldLocalPosition/heldLocalEuler 微调；"
                      + "倒计时时长在 DrillCountdown.durationSeconds 调整（上限 180 秒）。");
        }

        // ================================================================
        // 1. XR 锚点
        // ================================================================

        static XROrigin FindOrCreateRig()
        {
            var rootGO = GameObject.Find(RigName) ?? new GameObject(RigName);
            var rig = rootGO.GetComponent<XROrigin>() ?? rootGO.AddComponent<XROrigin>();
            rig.Origin = rootGO;                                // GameObject
            rig.CameraYOffset = 0f;

            // Camera Offset
            var offset = GameObject.Find(OffsetName) ?? new GameObject(OffsetName);
            offset.transform.SetParent(rootGO.transform, false);
            offset.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            rig.CameraFloorOffsetObject = offset;

            // Main Camera：优先复用已有相机，否则新建
            var camGO = GameObject.FindGameObjectWithTag("MainCamera");
            if (camGO == null) camGO = UnityEngine.Object.FindObjectOfType<Camera>()?.gameObject;
            if (camGO == null || camGO.transform.IsChildOf(rootGO.transform))
            {
                camGO = new GameObject(CameraName);
                var c = camGO.AddComponent<Camera>();
                c.fieldOfView = 60f;
                c.nearClipPlane = 0.05f;
                c.farClipPlane = 100f;
                c.clearFlags = CameraClearFlags.Skybox;
                camGO.AddComponent<AudioListener>();
                camGO.tag = "MainCamera";
            }
            camGO.transform.SetParent(offset.transform, false);
            camGO.transform.localPosition = Vector3.zero;
            camGO.transform.localRotation = Quaternion.identity;
            rig.Camera = camGO.GetComponent<Camera>();

            var pose = camGO.GetComponent<DrillTrackedPose>() ?? camGO.AddComponent<DrillTrackedPose>();
            pose.node = XRNode.CenterEye;

            // 交互管理器
            var mgrGO = GameObject.Find("XR Interaction Manager") ?? new GameObject("XR Interaction Manager");
            if (mgrGO.GetComponent<XRInteractionManager>() == null) mgrGO.AddComponent<XRInteractionManager>();

            try { rootGO.tag = "Player"; } catch (Exception) { Debug.LogWarning("[Setup] 项目缺少 Player 标签，已跳过（可在 Tag Manager 添加）。"); }

            Debug.Log("[Setup] ✔ XR 锚点就绪：" + RigName);
            return rig;
        }

        static Transform BuildController(XROrigin rig, HandSide side)
        {
            string name = side == HandSide.Left ? LeftHandName : RightHandName;
            string poseMap = side == HandSide.Left ? "XRI Left" : "XRI Right";                       // 位姿动作
            string interactMap = side == HandSide.Left ? "XRI Left Interaction" : "XRI Right Interaction"; // 交互动作
            XRNode node = side == HandSide.Left ? XRNode.LeftHand : XRNode.RightHand;

            var go = GameObject.Find(name) ?? new GameObject(name);
            go.transform.SetParent(rig.Origin.transform, false);
            go.transform.localPosition = side == HandSide.Left
                ? new Vector3(-0.35f, 1.3f, 0.3f)
                : new Vector3(0.35f, 1.3f, 0.3f);

            var pose = go.GetComponent<DrillTrackedPose>() ?? go.AddComponent<DrillTrackedPose>();
            pose.node = node;

            var ctrl = go.GetComponent<ActionBasedController>() ?? go.AddComponent<ActionBasedController>();
            ctrl.positionAction = new InputActionProperty(FindAction(poseMap, "Position"));
            ctrl.rotationAction = new InputActionProperty(FindAction(poseMap, "Rotation"));
            ctrl.selectAction = new InputActionProperty(FindAction(interactMap, "Select"));
            ctrl.activateAction = new InputActionProperty(FindAction(interactMap, "Activate"));
            ctrl.uiPressAction = new InputActionProperty(FindAction(interactMap, "UI Press"));

            var ray = go.GetComponent<XRRayInteractor>() ?? go.AddComponent<XRRayInteractor>();
            ray.enableUIInteraction = true;                     // 允许射线点按 UI
            ray.interactionManager = UnityEngine.Object.FindObjectOfType<XRInteractionManager>();

            if (go.GetComponent<XRInteractorLineVisual>() == null) go.AddComponent<XRInteractorLineVisual>();

            Debug.Log("[Setup] ✔ 手柄控制器：" + name);
            return go.transform;
        }

        // ================================================================
        // 2. 用户移动
        // ================================================================

        static void SetupLocomotion(XROrigin rig, Transform cam)
        {
            var root = rig.Origin.transform;
            var cc = root.GetComponent<CharacterController>();
            if (cc == null) cc = root.gameObject.AddComponent<CharacterController>();
            cc.height = 1.6f;
            cc.center = new Vector3(0f, 0.8f, 0f);
            cc.radius = 0.3f;

            var loco = root.GetComponent<XRDrillLocomotion>();
            if (loco == null) loco = root.gameObject.AddComponent<XRDrillLocomotion>();
            loco.rigRoot = root;
            loco.head = cam;
            loco.moveSpeed = 2.2f;
            loco.sprintMultiplier = 1.6f;
            loco.turnSpeed = 120f;
            loco.snapAngle = 45f;
            loco.useSnapTurn = true;
            loco.allowSprint = true;
            loco.keyboardDebug = true;
            Debug.Log("[Setup] ✔ 用户移动：左摇杆移动 / 右摇杆转身 / WASD+Q E 桌面调试");
        }

        // ================================================================
        // 3. 开始界面
        // ================================================================

        static void SetupStartScreen()
        {
            var go = GameObject.Find("StartScreen") ?? new GameObject("StartScreen");
            var start = go.GetComponent<StartScreen>() ?? go.AddComponent<StartScreen>();
            start.worldPosition = new Vector3(0f, 1.3f, 1.8f);
            start.canvasScale = 0.004f;
            start.startAlarmOnBegin = true;
            start.switchBgmToAlarm = true;
            start.igniteFiresOnBegin = true;
            Debug.Log("[Setup] ✔ 开始界面：StartScreen（玩法说明文字可在 Inspector 修改）");
        }

        // ================================================================
        // 4. 火灾警报
        // ================================================================

        static void SetupFireAlarm()
        {
            var go = GameObject.Find("FireAlarm") ?? new GameObject("FireAlarm");
            var alarm = go.GetComponent<FireAlarm>() ?? go.AddComponent<FireAlarm>();   // RequireComponent 会自动加 AudioSource
            var src = go.GetComponent<AudioSource>();
            src.spatialBlend = 1f;
            src.loop = true;
            src.playOnAwake = false;

            alarm.sirenSource = src;
            alarm.sirenClip = Resources.Load<AudioClip>("Sounds/BGM/fire_alarm_siren");
            alarm.sirenVolume = 0.8f;
            alarm.startDelay = 0f;
            alarm.autoStart = false;          // 由「开始演练」按钮触发
            alarm.completeStep2 = true;

            // 红色爆闪灯子物体（始终新建干净的灯，避免复用旧对象导致的空组件）
            var oldLight = go.transform.Find("AlarmLight");
            if (oldLight != null) UnityEngine.Object.DestroyImmediate(oldLight.gameObject);
            var lightGO = new GameObject("AlarmLight");
            lightGO.transform.SetParent(go.transform, false);
            lightGO.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            var lt = lightGO.AddComponent<Light>();
            if (lt == null) { Debug.LogError("[Setup] 添加 Light 组件失败"); return; }
            lt.type = LightType.Point;
            lt.color = Color.red;
            lt.range = 12f;
            lt.intensity = 0f;
            alarm.alarmLights = new[] { lt };
            Debug.Log("[Setup] ✔ 火灾警报：FireAlarm（警笛 + 红闪灯，点击开始后拉响）");
        }

        // ================================================================
        // 5. 背景音乐
        // ================================================================

        static void SetupBackgroundMusic()
        {
            var go = GameObject.Find("BackgroundMusic") ?? new GameObject("BackgroundMusic");
            if (go.GetComponent<AudioSource>() == null) go.AddComponent<AudioSource>();
            var bgm = go.GetComponent<BackgroundMusic>() ?? go.AddComponent<BackgroundMusic>();
            bgm.musicClip = Resources.Load<AudioClip>("Sounds/BGM/office_ambient");
            bgm.volume = 0.35f;
            bgm.playOnStart = true;
            bgm.fadeInSeconds = 2f;
            bgm.alarmClip = Resources.Load<AudioClip>("Sounds/BGM/fire_alarm_siren");
            bgm.alarmVolume = 0.55f;
            Debug.Log("[Setup] ✔ 背景音乐：office_ambient（开始后切 fire_alarm_siren）");
        }

        // ================================================================
        // 5.5 演练倒计时 + 地面引导箭头
        // ================================================================

        static void SetupDrillCountdown()
        {
            var go = GameObject.Find("DrillCountdown") ?? new GameObject("DrillCountdown");
            var cd = go.GetComponent<DrillCountdown>() ?? go.AddComponent<DrillCountdown>();
            cd.durationSeconds = 180f;       // 上限 3 分钟（脚本内再做 180s 钳制）
            cd.warningSeconds = 30f;
            cd.dangerSeconds = 10f;
            cd.hudOffset = new Vector3(0f, 0.35f, 0.6f);
            cd.canvasScale = 0.0035f;
            Debug.Log("[Setup] ✔ 演练倒计时：DrillCountdown（点击开始后启动，上限 180 秒）");
        }

        static void SetupGuidanceArrows()
        {
            var go = GameObject.Find("GuidanceArrows") ?? new GameObject("GuidanceArrows");
            var arrows = go.GetComponent<GuidanceArrows>() ?? go.AddComponent<GuidanceArrows>();
            arrows.arrowCount = 6;
            arrows.spacing = 1.2f;
            arrows.floorY = 0.02f;
            arrows.autoStartOnAlarm = true;
            Debug.Log("[Setup] ✔ 地面引导箭头：GuidanceArrows（警报响起后指向最近灭火器）");
        }

        // ================================================================
        // 6. 灭火器装配
        // ================================================================

        static void SetupExtinguisher(string objName, XROrigin rig, Transform leftHand, Transform rightHand)
        {
            var root = GameObject.Find(objName);
            if (root == null)
            {
                // 场景里可能以其他名字存在（如 Fire Extinguisher），按名字兜底
                root = FindObjectByNameContains("Extinguisher");
            }
            if (root == null)
            {
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
                if (fbx != null) root = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                else
                {
                    root = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    root.transform.localScale = new Vector3(0.16f, 0.35f, 0.16f);
                    var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    if (mat != null) { mat.color = new Color(0.85f, 0.15f, 0.15f); root.GetComponent<Renderer>().sharedMaterial = mat; }
                }
                root.name = objName;
                root.transform.position = objName.EndsWith("2")
                    ? new Vector3(3.2f, 0.5f, 0.5f)
                    : new Vector3(1.8f, 0.5f, 1.8f);
            }

            // 刚体 + 碰撞体
            var rb = root.GetComponent<Rigidbody>();
            if (rb == null) rb = root.AddComponent<Rigidbody>();
            rb.mass = 5f;
            rb.isKinematic = false;
            rb.useGravity = true;
            if (root.GetComponent<Collider>() == null)
            {
                var col = root.AddComponent<CapsuleCollider>();
                col.radius = 0.1f;
                col.height = 0.5f;
                col.center = new Vector3(0f, 0.25f, 0f);
            }

            // 抓取
            var grab = root.GetComponent<ToggleGrab>() ?? root.AddComponent<ToggleGrab>();
            grab.grabRange = 0.55f;
            grab.heldLocalPosition = new Vector3(0f, -0.05f, 0.1f);
            grab.heldLocalEuler = Vector3.zero;                 // 瓶身保持竖直握住，可在 Inspector 微调
            grab.hands = new[]
            {
                new HandBinding { side = HandSide.Left,  attachPoint = leftHand,  grabAction = GetOrCreateRef("XRI Left Interaction",  "Select") },
                new HandBinding { side = HandSide.Right, attachPoint = rightHand, grabAction = GetOrCreateRef("XRI Right Interaction", "Select") }
            };

            // 音源：一次性音效 + 喷射循环
            var sfx = root.GetComponent<AudioSource>();
            if (sfx == null) sfx = root.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            grab.audioSource = sfx;
            grab.pickupClip = Resources.Load<AudioClip>("Sounds/SFX/grab_pickup");
            grab.dropClip = Resources.Load<AudioClip>("Sounds/SFX/grab_drop");

            var spraySrc = SecondAudioSource(root, sfx);
            spraySrc.loop = true;
            spraySrc.playOnAwake = false;

            // 灭火器主体
            var ext = root.GetComponent<Extinguisher>() ?? root.AddComponent<Extinguisher>();
            ext.grab = grab;
            ext.audioSource = sfx;
            ext.sprayLoopSource = spraySrc;
            ext.pinPullClip = Resources.Load<AudioClip>("Sounds/SFX/pin_pull");
            ext.sprayLoopClip = Resources.Load<AudioClip>("Sounds/SFX/spray_loop");
            ext.leftActivateAction = GetOrCreateRef("XRI Left Interaction", "Activate");
            ext.rightActivateAction = GetOrCreateRef("XRI Right Interaction", "Activate");
            ext.quizRequired = true;
            ext.sprayRange = 3f;
            ext.sprayAngle = 20f;

            // 喷嘴（喷射方向 = Z 轴）
            var nozzle = root.transform.Find("Nozzle");
            if (nozzle == null)
            {
                var n = new GameObject("Nozzle");
                n.transform.SetParent(root.transform, false);
                n.transform.localPosition = new Vector3(0f, 0.42f, 0.12f);
                nozzle = n.transform;
            }
            ext.nozzle = nozzle;

            // 常识问答面板（挂在灭火器根上）
            var quiz = root.GetComponent<ExtinguisherQuizPanel>() ?? root.AddComponent<ExtinguisherQuizPanel>();
            quiz.panelOffset = new Vector3(0f, 1.4f, 0f);
            ext.quizPanel = quiz;

            // 手部姿态匹配（抓握时手指贴合瓶身）
            if (root.GetComponent<GrabPoseMatcher>() == null) root.AddComponent<GrabPoseMatcher>();

            // 视线高亮：灭火器进入玩家视野后发光 + 光环 + 光柱提示
            var highlight = root.GetComponent<ExtinguisherHighlight>() ?? root.AddComponent<ExtinguisherHighlight>();
            highlight.checkInterval = 0.15f;
            highlight.gazeAngle = 60f;
            highlight.maxDistance = 20f;
            highlight.checkOcclusion = true;

            // 保险销
            SetupSafetyPin(root, ext, leftHand, rightHand);

            Debug.Log("[Setup] ✔ 灭火器：" + objName + "（抓取/问答/拔销/喷射/手型匹配已装配）");
        }

        static void SetupSafetyPin(GameObject root, Extinguisher ext, Transform leftHand, Transform rightHand)
        {
            var pinT = root.transform.Find("SafetyPin");
            if (pinT == null)
            {
                var pinGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pinGo.name = "SafetyPin";
                pinGo.transform.SetParent(root.transform, false);
                pinGo.transform.localPosition = new Vector3(0f, 0.55f, 0f);
                pinGo.transform.localScale = new Vector3(0.06f, 0.06f, 0.12f);
                UnityEngine.Object.DestroyImmediate(pinGo.GetComponent<Collider>());
                var col = pinGo.AddComponent<CapsuleCollider>();
                col.direction = 2;
                col.radius = 0.03f;
                col.height = 0.12f;
                pinT = pinGo.transform;
            }

            var rb = pinT.GetComponent<Rigidbody>();
            if (rb == null) rb = pinT.gameObject.AddComponent<Rigidbody>();
            rb.mass = 0.2f;

            var grab = pinT.GetComponent<ToggleGrab>() ?? pinT.gameObject.AddComponent<ToggleGrab>();
            grab.grabRange = 0.5f;
            grab.heldLocalPosition = new Vector3(0f, 0f, 0.06f);
            grab.heldLocalEuler = Vector3.zero;
            grab.lockAfterRelease = true;                       // 拔出后不能再抓回
            grab.hands = new[]
            {
                new HandBinding { side = HandSide.Left,  attachPoint = leftHand,  grabAction = GetOrCreateRef("XRI Left Interaction",  "Select") },
                new HandBinding { side = HandSide.Right, attachPoint = rightHand, grabAction = GetOrCreateRef("XRI Right Interaction", "Select") }
            };

            var pin = pinT.GetComponent<SafetyPin>() ?? pinT.gameObject.AddComponent<SafetyPin>();
            pin.owner = ext;
            pin.grab = grab;
            pin.pullDistance = 0.12f;
            pin.dropAfterPull = true;
            pin.hideDelay = 2f;
        }

        // ================================================================
        // 7. EventSystem + XR UI 输入
        // ================================================================

        static void SetupEventSystem()
        {
            var es = GameObject.Find("EventSystem");
            if (es == null) es = new GameObject("EventSystem");
            if (es.GetComponent<EventSystem>() == null) es.AddComponent<EventSystem>();
            if (es.GetComponent<XRUIInputModule>() == null) es.AddComponent<XRUIInputModule>();
            Debug.Log("[Setup] ✔ EventSystem + XRUIInputModule（手柄射线点按 UI）");
        }

        // ================================================================
        // 工具
        // ================================================================

        static InputAction FindAction(string map, string action)
        {
            if (_asset == null) return null;
            var m = _asset.FindActionMap(map, false);
            return m != null ? m.FindAction(action, false) : null;
        }

        /// <summary>创建可持久化的 InputActionReference 资源（场景序列化需要真实资源）。</summary>
        static InputActionReference GetOrCreateRef(string map, string action)
        {
            var a = FindAction(map, action);
            if (a == null)
            {
                Debug.LogError("[Setup] 找不到动作 " + map + "/" + action);
                return null;
            }
            if (!AssetDatabase.IsValidFolder(RefDir))
                AssetDatabase.CreateFolder("Assets/Scripts/FireSafety", "InputRefs");

            string path = RefDir + "/" + map.Replace(" ", "") + "_" + action.Replace(" ", "") + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<InputActionReference>(path);
            if (existing != null) return existing;

            var r = InputActionReference.Create(a);
            AssetDatabase.CreateAsset(r, path);
            AssetDatabase.SaveAssets();
            return r;
        }

        static GameObject FindObjectByNameContains(string token)
        {
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
                if (go.name.Contains(token, StringComparison.OrdinalIgnoreCase)) return go;
            return null;
        }

        static AudioSource SecondAudioSource(GameObject root, AudioSource first)
        {
            foreach (var s in root.GetComponents<AudioSource>())
                if (s != first) return s;
            return root.AddComponent<AudioSource>();
        }
    }
}
#endif
