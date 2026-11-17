#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
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
            SetupStartScreen(rightHand);
            SetupOnboarding(rightHand);

            // 4. 警报
            SetupFireAlarm();

            // 5. 背景音乐
            SetupBackgroundMusic();

            // 5.5 演练倒计时（3 分钟上限）+ 地面引导箭头
            SetupDrillCountdown(rightHand);
            SetupGuidanceArrows();

            // 5.6 阶段一完成面板（暂停倒计时 + 立刻结算 / 继续往下）
            SetupStageOnePanel();
            SetupEndingScreen();
            SetupStageTwoFlow();

            // 5.6b 情景二/三通往出口的房间门（第 27 轮）：第一阶段火灭后才能打开
            SetupRoomDoor();

            // 5.7 天花板（办公室原来只有地板，抬头能直接看到天空盒）
            SetupCeiling();

            // 5.8 补墙：封住南侧 / 南翼东侧 / 大厅南边 / 茶水间东侧四处敞开的天空盒缺口
            SetupWalls();

            // 5.9 门外开阔场地：安全出口门背后不再是一条封死的窄走廊
            SetupOutdoorPlaza();

            // 6. 灭火器（01 = 操作演练 / 02 = 正式第 1 阶段设备火 / 03 = 第 2 阶段出口火）
            SetupExtinguisher("Extinguisher01", rig, leftHand, rightHand);
            SetupExtinguisher("Extinguisher02", rig, leftHand, rightHand);
            SetupExtinguisher("Extinguisher03", rig, leftHand, rightHand);

            // 7. UI 输入
            SetupEventSystem();

            // 8. 桌面鼠标调试输入（无头显也能走完阶段一操作流程）
            SetupDesktopInput(rig);

            // 9. 头显输入层：Trigger 拔销/压把/喷射、A/B/X 问答与翻页、左摇杆按下蹲起、VR 提示条
            SetupVrInput(rig, cam);

            // 10. 新流程编排：操作演练 + 正式两阶段（随机出生点 / 设备火 / 电源 / 火焰血量 / 出口火）
            SetupDrillScenario(rig != null ? rig.gameObject : null);

            // 10. 清理历史遗留的「缺失脚本」组件
            RemoveMissingScripts();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log("[Setup] ==== 搭建完成，场景已保存：" + ScenePath + " ====");
            Debug.Log("[Setup] 提示：开始界面玩法说明文字请在场景 StartScreen 的 Instructions 字段修改；"
                      + "灭火器抓握姿态在 ToggleGrab.heldLocalPosition/heldLocalEuler 微调；"
                      + "倒计时时长在 DrillCountdown.durationSeconds 调整（上限 180 秒）。"
                      + "桌面鼠标操作：右键单击灭火器答题/拿起 / 左键单击拔销 / 左键双击压把 / 右键双击喷射 / P 放下。");
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

            // Camera Offset：只认本 rig 自己的子物体，避免复用到其它预制体 rig 的 offset
            GameObject offset = null;
            foreach (var ch in rootGO.GetComponentsInChildren<Transform>(true))
                if (ch != rootGO.transform && ch.name == OffsetName && ch.parent == rootGO.transform) { offset = ch.gameObject; break; }
            if (offset == null)
            {
                offset = new GameObject(OffsetName);
                offset.transform.SetParent(rootGO.transform, false);
            }
            offset.transform.localPosition = new Vector3(0f, 1.6f, 0f);   // 站立眼高 1.6 米
            rig.CameraFloorOffsetObject = offset;

            // Main Camera：本 rig 自带一台相机，保证「XR Origin 一动，视角就跟着动」。
            // 场景里 XRI Starter Assets 预制体的第二套 XR Origin 会随场景还原，抢同一个 Camera.main，
            // 因此这里不共用它的相机，而是把它的相机和整条旧 rig 一起停用。
            var camGO = offset.transform.Find(CameraName)?.gameObject;
            if (camGO == null)
            {
                camGO = new GameObject(CameraName);
                camGO.transform.SetParent(offset.transform, false);
                var c = camGO.AddComponent<Camera>();
                c.fieldOfView = 60f;
                c.nearClipPlane = 0.05f;
                c.farClipPlane = 100f;
                c.clearFlags = CameraClearFlags.Skybox;
                camGO.AddComponent<AudioListener>();
            }
            camGO.tag = "MainCamera";
            camGO.transform.localPosition = Vector3.zero;
            camGO.transform.localRotation = Quaternion.identity;
            rig.Camera = camGO.GetComponent<Camera>();

            // 旧的第二套 XR Origin（预制体自带相机/手柄）整体停用，避免双相机、双追踪
            foreach (var other in UnityEngine.Object.FindObjectsByType<XROrigin>(FindObjectsSortMode.None))
            {
                if (other == rig) continue;
                if (other.GetComponentInChildren<Camera>(true) != null)
                {
                    other.gameObject.SetActive(false);
                    Debug.Log("[Setup] 停用重复的 XR Origin：" + other.name + "（相机与手柄已由 " + RigName + " 承担）");
                }
            }

            // 头显近裁面：0.05 米（再近会切脸），远裁面 100 米够看清走廊尽头
            if (rig.Camera != null)
            {
                rig.Camera.nearClipPlane = 0.05f;
                rig.Camera.farClipPlane = 100f;
                rig.Camera.fieldOfView = 60f;
            }

            // 站立身高追踪：地板为原点，玩家真实身高直接映射
            rig.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

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
            FixRayLine(go);   // 射线不设材质/宽度 → 手边一条 1 米宽洋红色带子挡视线

            // 手柄可见模型：真机里看得见自己的手在哪、抓没抓住
            var vis = go.GetComponent<VRControllerVisual>() ?? go.AddComponent<VRControllerVisual>();
            vis.side = side;
            vis.showInDesktop = false;
            vis.accentColor = side == HandSide.Left ? new Color(0.28f, 0.82f, 1f) : new Color(1f, 0.55f, 0.2f);
            vis.emission = 0.55f;

            Debug.Log("[Setup] ✔ 手柄控制器：" + name);
            return go.transform;
        }

        /// <summary>
        /// 手柄射线：LineRenderer 若没有材质，Unity 用「洋红」当错误材质；而 XRILV 自动挂上的
        /// LineRenderer 默认宽度是 1 米，玩家第一人称视角里就被一条贴脸的洋红大板挡住。
        /// 这里把宽度调回细线、颜色改成青色，并补一份 URP 透明材质。
        /// </summary>
        static void FixRayLine(GameObject go)
        {
            var lr = go.GetComponent<LineRenderer>();
            if (lr != null)
            {
                for (int i = 0; i < lr.positionCount; i++) { }   // 保留已有点位
                var flat = new AnimationCurve();
                flat.AddKey(0f, 0.012f);
                flat.AddKey(1f, 0.012f);
                lr.widthCurve = flat;
                lr.startWidth = 0.012f;
                lr.endWidth = 0.012f;
                lr.widthMultiplier = 1f;
                lr.useWorldSpace = true;
                lr.numCapVertices = 4;
                lr.alignment = LineAlignment.View;
                var tint = new Color(0.38f, 0.85f, 1f, 0.55f);
                lr.startColor = tint;
                lr.endColor = tint;

                if (lr.sharedMaterial == null)
                {
                    var mat = SmokeZone.CreateParticleMaterial(null, true);
                    if (mat != null)
                    {
                        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
                        if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
                        mat.name = "HandRay_LineMat";
                        lr.sharedMaterial = mat;
                    }
                }
            }

            // XRI 的线视觉组件在运行期会用自己的 lineWidth 覆盖 LineRenderer，一并写掉
            var vis = go.GetComponent<XRInteractorLineVisual>();
            if (vis == null) return;
            foreach (var f in vis.GetType().GetFields(System.Reflection.BindingFlags.Instance |
                                                      System.Reflection.BindingFlags.Public |
                                                      System.Reflection.BindingFlags.NonPublic))
            {
                if (f.FieldType == typeof(float) && f.Name.ToLower().Contains("linewidth"))
                {
                    if (f.IsInitOnly) continue;
                    try { f.SetValue(vis, 0.012f); } catch (Exception) { }
                }
                else if (f.FieldType == typeof(Material) && f.Name.ToLower().Contains("material"))
                {
                    if (lr != null && lr.sharedMaterial != null)
                        try { f.SetValue(vis, lr.sharedMaterial); } catch (Exception) { }
                }
            }
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
            loco.moveAction = GetOrCreateRef("XRI Left Locomotion", "Move");
            loco.turnAction = GetOrCreateRef("XRI Right Locomotion", "Snap Turn");
            loco.moveSpeed = 2.2f;
            loco.crawlSpeed = 1.05f;
            loco.sprintMultiplier = 1.6f;
            loco.turnSpeed = 120f;
            loco.snapAngle = 45f;
            loco.snapCooldown = 0.35f;
            loco.deadzone = 0.15f;
            loco.crouchHeight = 1.15f;
            loco.useSnapTurn = true;
            loco.sprintOnFullStick = true;           // 推满摇杆自动小跑
            loco.fullStickThreshold = 0.92f;
            loco.allowSprint = false;                // Grip 是抓取键，不再兼任加速
            loco.moveDirection = XRDrillLocomotion.DirectionMode.Head;
            loco.leftHand = GameObject.Find(LeftHandName) != null ? GameObject.Find(LeftHandName).transform : null;
            loco.rightHand = GameObject.Find(RightHandName) != null ? GameObject.Find(RightHandName).transform : null;
            loco.keyboardDebug = true;
            Debug.Log("[Setup] ✔ 用户移动：左摇杆(XRI Move)移动 / 右摇杆(XRI Snap Turn)瞬转 45°"
                      + " / 推满摇杆小跑 / 蹲下自动降速 / WASD+Q E 桌面调试");
        }

        // ================================================================
        // 3. 开始界面
        // ================================================================

        static void SetupStartScreen(Transform anchorHand)
        {
            var go = GameObject.Find("StartScreen") ?? new GameObject("StartScreen");
            var start = go.GetComponent<StartScreen>() ?? go.AddComponent<StartScreen>();
            start.worldPosition = new Vector3(0f, 1.3f, 1.8f);
            start.canvasScale = 0.0009f;              // 约 0.9 米宽，一臂外可读且不糊视野
            start.anchorHand = anchorHand;           // 开始界面跟随到右手手柄正前方
            start.handForwardDistance = 0.75f;        // 手柄前向约 0.75 米（凑近一点，视野内正好读完）
            start.handUpOffset = 0.15f;               // 略向上抬，居中可读
            start.maxCameraDistance = 1.4f;           // 离相机超过 1.4 米就改用相机兜底，避免面板跑到房间另一头
            start.cameraFallbackDistance = 1.1f;      // 兜底：相机正前方 1.1 米（0.9 米宽面板约占屏高一半）
            start.startAlarmOnBegin = true;
            start.switchBgmToAlarm = true;
            start.igniteFiresOnBegin = true;
            start.instructions =
                "玩法说明：\n" +
                "① 点「开始演练」→ 警报拉响、倒计时 3 分钟；出生点会在 3 处中随机落到一处，旁边就是起火的电气设备；\n" +
                "② 设备起火会冒烟：先走到旁边的电源箱，右键单击（VR 扳机点射）切断电源——未断电时喷射无效；\n" +
                "③ 拿灭火器：进入视线会高亮；右键单击灭火器 → 5 道题、每题 10 秒、答对 60%（3 题）才能拿起；\n" +
                "④ 拔销（左键单击 / 扳机）→ 压把（左键双击 / 扳机）→ 喷射（右键双击 / 按住扳机）；\n" +
                "   火焰上方有血量条（HP 100 → 0），喷到归零即扑灭；\n" +
                "⑤ 灭掉设备火 → 第 2 阶段：沿「安全出口」标识与地面箭头撤离，出口前会再升起一团火；\n" +
                "⑥ 用出口旁的灭火器再答 5 题 → 灭掉出口火 →「安全出口 EXIT」开门，走进门内通关。\n" +
                "想先熟悉流程？点「操作演练」：原地练一遍 拿瓶 → 答题 → 拔销 → 压把 → 喷射。\n" +
                "键盘：W/A/S/D 移动、鼠标转头、C 蹲下、P 放下灭火器；回车 / 空格 开始演练。";
            start.tutorialLabel = "操作演练";
            start.showTutorialButton = true;
            start.onboardingLabel = "操作教学";
            Debug.Log("[Setup] ✔ 开始界面：StartScreen（锚定右手手柄正前方；玩法说明可在 Inspector 修改）");
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

        static void SetupDrillCountdown(Transform anchorHand)
        {
            var go = GameObject.Find("DrillCountdown") ?? new GameObject("DrillCountdown");
            var cd = go.GetComponent<DrillCountdown>() ?? go.AddComponent<DrillCountdown>();
            cd.durationSeconds = 180f;       // 上限 3 分钟（脚本内再做 180s 钳制）
            cd.warningSeconds = 30f;
            cd.dangerSeconds = 10f;
            cd.hudOffset = new Vector3(0f, 0.35f, 0.6f);
            cd.anchorHand = anchorHand;       // 倒计时牌跟随到右手手柄正上方
            cd.handAboveDistance = 0.42f;    // 正上方约 0.42 米，余光可读
            cd.handForwardOffset = 0.12f;    // 向前探出，避免被手臂遮挡
            cd.canvasScale = 0.0028f;
            Debug.Log("[Setup] ✔ 演练倒计时：DrillCountdown（绑定右手手柄正上方；点击开始后启动，上限 180 秒）");
        }

        static void SetupGuidanceArrows()
        {
            var go = GameObject.Find("GuidanceArrows") ?? new GameObject("GuidanceArrows");
            var arrows = go.GetComponent<GuidanceArrows>() ?? go.AddComponent<GuidanceArrows>();
            arrows.arrowCount = 6;
            arrows.spacing = 1.2f;
            arrows.floorY = 0.02f;
            arrows.autoStartOnAlarm = true;

            // 第 2 阶段：大厅撤离目标（地面标记 + 绿色圆柱视觉，可在场景里平移）
            // 第 16 轮：出口门换成 door_b_variant（x≈5.25 的墙上，门朝 -X 房间侧），
            // 大厅检查点就放在门正前方 1.9 米（x≈3.35），玩家从房间侧一路走来
            var lobby = GameObject.Find("Lobby") ?? new GameObject("Lobby");
            lobby.transform.localPosition = new Vector3(3.35f, 0.02f, 1.02f);
            if (lobby.transform.childCount == 0)
            {
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                pad.name = "LobbyPad";
                pad.transform.SetParent(lobby.transform, false);
                pad.transform.localPosition = Vector3.zero;
                pad.transform.localScale = new Vector3(0.6f, 0.02f, 0.6f);
                var rend = pad.GetComponent<Renderer>();
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                mat.color = new Color(0.2f, 0.85f, 0.45f, 0.85f);
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", new Color(0.1f, 0.6f, 0.25f, 1f));
                rend.sharedMaterial = mat;
                var col = pad.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.DestroyImmediate(col);
            }
            arrows.lobbyTarget = lobby.transform;
            arrows.lobbyArriveDistance = 0.6f;
            Debug.Log("[Setup] ✔ 地面引导箭头：GuidanceArrows（警报后指向灭火器；继续往下后指向大厅 Lobby）");
        }

        // ================================================================
        // 5.6b 情景二/三通往出口的房间门（第 27 轮）
        // ================================================================

        /// <summary>
        /// 场景里 z≈0 那道隔墙上的 door_b（门后是带玻璃窗的小房间）是情景二/三去安全出口的必经之门。
        /// 之前它是纯装饰：门扇带碰撞体、堵死整道隔墙（实测 x -0.8~7.3 全被挡），
        /// 玩家在第 1 阶段灭完火后根本走不到出口。这里给它挂 RoomDoor，
        /// 第一阶段的火灭掉之后即可用右键 / 扳机射线打开。
        /// </summary>
        public static void SetupRoomDoor()
        {
            var root = GameObject.Find("Floor 0/door_b");
            if (root == null) root = GameObject.Find("door_b");
            if (root == null)
            {
                Debug.LogWarning("[Setup] 找不到 door_b，房间门未装配。");
                return;
            }

            var door = root.GetComponent<RoomDoor>() ?? root.AddComponent<RoomDoor>();
            door.leaf = null;                 // 轴心就在 door_b 根节点（x=2.75 一侧）
            door.openAngle = 95f;             // 朝疏散方向（北侧走廊）开：真实疏散门也是朝逃生方向开，且不会把南侧走来的玩家顶住
            door.openDuration = 0.8f;
            door.maxInteractDistance = 4.0f;
            door.requireStageOneCleared = true;
            Debug.Log("[Setup] ✔ 房间门：door_b 挂 RoomDoor（情景二/三第 1 阶段火灭后可打开）");
        }


        // ================================================================
        // 5.6 阶段一完成面板（暂停倒计时 + 立刻结算 / 继续往下）
        // ================================================================

        static void SetupStageOnePanel()
        {
            var go = GameObject.Find("StageOnePanel") ?? new GameObject("StageOnePanel");
            var panel = go.GetComponent<StageOneCompletePanel>() ?? go.AddComponent<StageOneCompletePanel>();
            panel.countdown = UnityEngine.Object.FindObjectOfType<DrillCountdown>();
            panel.startScreen = UnityEngine.Object.FindObjectOfType<StartScreen>();
            panel.distanceFromCamera = 1.55f;   // 1.2 米时面板顶满视野、按钮被桌面 HUD 压住；1.55 米更稳
            panel.verticalOffset = 0.08f;
            panel.title = "阶段一完成";
            panel.buttonSettlement = "立刻结算";
            panel.buttonContinue = "继续往下";
            if (go.GetComponent<AudioSource>() == null) go.AddComponent<AudioSource>();
            Debug.Log("[Setup] ✔ 阶段一完成面板：StageOneCompletePanel（暂停倒计时 + 立刻结算/继续往下）");
        }

        /// <summary>引导页（온보딩）：分页操作教学，锚定右手手柄正前方。</summary>
        static void SetupOnboarding(Transform rightHand)
        {
            var go = GameObject.Find("OnboardingScreen") ?? new GameObject("OnboardingScreen");
            var ob = go.GetComponent<OnboardingScreen>() ?? go.AddComponent<OnboardingScreen>();
            ob.startScreen = UnityEngine.Object.FindObjectOfType<StartScreen>();
            ob.anchorHand = rightHand;
            ob.handForwardDistance = 0.6f;      // 引导页拉近：手柄前 0.6 米，一屏读完正文
            ob.handUpOffset = 0.12f;
            ob.canvasScale = 0.0009f;
            ob.fallbackDistance = 1.0f;
            ob.maxCameraDistance = 1.3f;        // 距离保护：锚点太远就拉回相机前方
            ob.cameraFallbackDistance = 1.0f;
            ob.title = "操作教学 · 开始前必读";
            ob.prevLabel = "上一步";
            ob.nextLabel = "下一步";
            ob.lastLabel = "开始演练";
            ob.skipLabel = "跳过教学";
            ob.pages = OnboardingScreen.BuildDefaultPages();   // 场景里同步最新 VR 移动/功能键位文案
            if (go.GetComponent<AudioSource>() == null) go.AddComponent<AudioSource>();
            Debug.Log("[Setup] ✔ 引导页：OnboardingScreen（4 页教学，锚定右手手柄前方 0.6 米 / 上抬 0.12 米，带 1.3 米距离保护）");
        }

        /// <summary>结束页（엔딩）：撤离到大厅成功 / 倒计时耗尽失败。</summary>
        static void SetupEndingScreen()
        {
            var go = GameObject.Find("EndingScreen") ?? new GameObject("EndingScreen");
            var ending = go.GetComponent<EndingScreen>() ?? go.AddComponent<EndingScreen>();
            ending.countdown = UnityEngine.Object.FindObjectOfType<DrillCountdown>();
            var ext = GameObject.Find("Extinguisher01");
            if (ext != null) ending.quiz = ext.GetComponentInChildren<ExtinguisherQuizPanel>(true);
            ending.distanceFromCamera = 1.35f;
            ending.verticalOffset = 0f;
            ending.exitDoor = UnityEngine.Object.FindObjectOfType<ExitDoor>(true);
            ending.titleSuccess = "演练完成 · 成功撤离";
            ending.titleFailure = "演练结束 · 时间到";
            ending.buttonRestart = "重新演练";
            ending.buttonClose = "关闭";
            if (go.GetComponent<AudioSource>() == null) go.AddComponent<AudioSource>();
            Debug.Log("[Setup] ✔ 结束页：EndingScreen（走进安全出口=成功 / 倒计时耗尽=失败，含成绩汇总）");
        }

        /// <summary>
        /// 第二 / 第三阶段：烟雾区 + 安全出口门 + 流程编排（StageTwoFlow）。
        /// 大厅 Lobby 是第二阶段检查点，出口门在它后方 1.1 米，玩家进门才通关。
        /// </summary>
        static void SetupStageTwoFlow()
        {
            // 烟雾区：灭火房间 → 新出口门（door_b_variant，x≈5.25）之间的通道
            var smokeGo = GameObject.Find("SmokeZone") ?? new GameObject("SmokeZone");
            var smoke = smokeGo.GetComponent<SmokeZone>() ?? smokeGo.AddComponent<SmokeZone>();
            smoke.emitterCount = 7;
            // 雾层抬高到 1.6 米：浓雾罩住视线高度，但雾底离地 1.15 米，
            // 地面上的「安全出口」箭头不会被粒子糊住
            smoke.start = new Vector3(-1.50f, 1.60f, 0.60f);
            smoke.end = new Vector3(4.30f, 1.60f, 1.10f);
            smoke.floorClearance = 1.15f;
            smoke.particleSize = 1.05f;
            smoke.ratePerSecond = 20f;

            // 安全出口门：复用场景里另一扇真实门 door_b_variant
            // （门板中心 x≈5.25 / z≈1.02，门在 x≈5.25 的墙面上朝 -X 房间侧开）
            var realDoor = GameObject.Find("door_b_variant");
            var doorGo = GameObject.Find("ExitDoor") ?? new GameObject("ExitDoor");
            doorGo.transform.position = new Vector3(5.25f, 0f, 1.02f);
            // 门朝 -X：让指示牌（本地 +Z 0.12 米）也落在房间这一侧，同时让门前火的偏移跟着门走
            doorGo.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
            var door = doorGo.GetComponent<ExitDoor>() ?? doorGo.AddComponent<ExitDoor>();
            door.existingDoorRoot = realDoor != null ? realDoor.transform : null;
            door.existingOpenAngle = -95f;
            door.signHeight = 2.42f;
            door.enterDistance = 0.9f;
            door.signText = "安全出口 EXIT";

            // 复用的前提下，清掉以前运行时生成、又被误存进场景的自建门体/指示牌子物体
            if (realDoor != null && doorGo.transform.childCount > 0)
            {
                for (int i = doorGo.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.DestroyImmediate(doorGo.transform.GetChild(i).gameObject);
            }

            // 出口门应急照明：这面墙附近本来没有灯，浓雾里 + 火灭之后门前是一片黑，
            // 玩家找不到门。门上方加一盏壁灯，照亮门口一小片（不改变整间屋子的亮度）
            var lampGo = doorGo.transform.Find("ExitDoorLamp");
            if (lampGo == null)
            {
                lampGo = new GameObject("ExitDoorLamp").transform;
                lampGo.SetParent(doorGo.transform, false);
            }
            lampGo.localPosition = new Vector3(0f, 2.30f, 0.30f);
            var lamp = lampGo.GetComponent<Light>();
            if (lamp == null) lamp = lampGo.gameObject.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.90f, 0.72f);
            lamp.intensity = 1.8f;
            lamp.range = 4.2f;
            lamp.shadows = LightShadows.None;

            // 场景自带的火（BaseFire000）带了一个把整间屋子都包住的 BoxCollider（约 8.7 米）。
            // 灭火器是带 Rigidbody 的刚体，一进运行就被这个盒子「顶」出去 —— 表现为“运行后直接弹到他处”。
            // 火是纯视觉，不需要碰撞体：全部移除（也顺带不再挡住右键射线 / 视线遮挡判定）。
            foreach (var fireSource in UnityEngine.Object.FindObjectsByType<FireSource>(FindObjectsSortMode.None))
            {
                if (fireSource == null) continue;
                int removed = 0;
                foreach (var c in fireSource.GetComponentsInChildren<Collider>(true))
                {
                    UnityEngine.Object.DestroyImmediate(c);
                    removed++;
                }
                if (removed > 0)
                    Debug.Log("[Setup] 已移除火源 " + fireSource.name + " 的 " + removed + " 个碰撞体（避免把刚体顶飞）");
            }

            var go = GameObject.Find("StageTwoFlow") ?? new GameObject("StageTwoFlow");
            var flow = go.GetComponent<StageTwoFlow>() ?? go.AddComponent<StageTwoFlow>();
            flow.smoke = smoke;
            flow.exitDoor = door;
            flow.arrows = UnityEngine.Object.FindObjectOfType<GuidanceArrows>();
            flow.countdown = UnityEngine.Object.FindObjectOfType<DrillCountdown>();
            flow.endingScreen = UnityEngine.Object.FindObjectOfType<EndingScreen>();
            flow.crouchHeight = 1.15f;
            flow.lobbyEnterDistance = 1.2f;
            flow.exitEnterDistance = 0.9f;
            // 新流程第 2 阶段：只保留安全标识 + 出口火情，不开浓烟、不强制蹲姿
            flow.useSmoke = false;
            flow.requireCrouch = false;
            // 复燃火源放在安全出口门正前方 0.75 米（按门的本地坐标解释，门朝哪边就落在哪边）
            flow.stageTwoFireOffset = new Vector3(0f, 0.25f, 0.75f);

            // 第 2 阶段固定用 Extinguisher03（D 组：安全出口 / 疏散情景），摆在出口门旁
            var spare = GameObject.Find("Extinguisher03");
            if (spare != null) flow.spareExtinguisher = spare.GetComponent<Extinguisher>();
            flow.autoMoveSpareExtinguisher = false;
            flow.spareExtinguisherSpot = new Vector3(4.95f, 0f, 3.20f);   // 第 25 轮：出口门西北侧贴墙（x=5.25 墙内侧），不再占门前的过道
            flow.spareQuizSetIndex = 3;

            // 箭头能指到出口
            var arrows = UnityEngine.Object.FindObjectOfType<GuidanceArrows>();
            if (arrows != null)
            {
                arrows.exitTarget = doorGo.transform;
                arrows.exitArriveDistance = 0.9f;
                arrows.stage2ArrowScale = 2.2f;         // 浓烟里箭头放大
                arrows.floorSignText = "安全出口 →";    // 悬浮标识，烟挡不住
                arrows.floorSignHeight = 0.62f;         // 贴在雾底（1.15 米）以下，浓烟里也读得到
                arrows.floorSignBackOffset = 0.9f;
                arrows.floorSignSideOffset = 1.3f;      // 挪到门侧：正对门口会被门前火挡住
            }

            var panel = UnityEngine.Object.FindObjectOfType<StageOneCompletePanel>();
            if (panel != null) panel.stageTwo = flow;

            var ending = UnityEngine.Object.FindObjectOfType<EndingScreen>();
            if (ending != null) ending.exitDoor = door;

            Debug.Log("[Setup] ✔ 第二阶段流程：StageTwoFlow + SmokeZone（7 个烟雾发射点）+ ExitDoor（复用 door_b_variant 真实门，解锁后门扇转开）");
        }

        // ================================================================
        // 5.7 天花板 + 顶灯
        // ================================================================
        /// <summary>
        /// 场景原本只有 Floor 0，抬起头能直接看到天空盒 —— 火灾房间里出现"天花板开了个洞"的违和感。
        /// 用一块 14×12 米的 Cube 封住 3.0 米高的墙顶。关键点：必须关闭阴影投射，
        /// 否则方向光被整块板挡住，室内会骤然变暗。
        /// </summary>
        static void SetupCeiling()
        {
            var go = GameObject.Find("Ceiling");
            if (go == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Ceiling";
            }
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.DestroyImmediate(col);   // 不参与碰撞，纯视觉

            go.transform.position = new Vector3(0.25f, 3.05f, -2.0f);   // 内表面 y=3.0，与墙顶齐
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = new Vector3(14f, 0.1f, 12f);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = EnsureSetupMaterial("Assets/Materials/CeilingMat.mat",
                    new Color(0.72f, 0.73f, 0.76f), 0.15f);
                mr.shadowCastingMode = ShadowCastingMode.Off;   // 不挡方向光，保持室内亮度
                mr.receiveShadows = false;
            }

            // 顶灯：四盏吸顶灯盘 + 低强度点光，补天花板下方的照度（不改变原有氛围）
            var lampDefs = new[]
            {
                new Vector3(-3.80f, 2.92f, 1.60f),
                new Vector3(1.00f, 2.92f, 1.60f),
                new Vector3(4.60f, 2.92f, 1.60f),
                new Vector3(-1.40f, 2.92f, -3.20f)
            };
            for (int i = 0; i < lampDefs.Length; i++)
            {
                string lampName = "CeilingLamp_" + (i + 1);
                var lampGo = GameObject.Find(lampName);
                Transform t = lampGo != null ? lampGo.transform : null;
                if (t == null)
                {
                    var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    disc.name = lampName;
                    var dc = disc.GetComponent<Collider>();
                    if (dc != null) UnityEngine.Object.DestroyImmediate(dc);
                    t = disc.transform;
                }
                t.position = lampDefs[i];
                t.localScale = new Vector3(0.30f, 0.015f, 0.30f);
                var dmr = t.GetComponent<MeshRenderer>();
                if (dmr != null)
                    dmr.sharedMaterial = EnsureSetupMaterial("Assets/Materials/CeilingLampMat.mat",
                        new Color(1f, 0.96f, 0.88f), 0.6f, emission: new Color(1f, 0.92f, 0.78f) * 1.2f);

                Transform lightT = t.Find("PointLight");
                if (lightT == null)
                {
                    var lgo = new GameObject("PointLight");
                    lgo.transform.SetParent(t, false);
                    lightT = lgo.transform;
                }
                lightT.localPosition = new Vector3(0f, -1.0f / Mathf.Max(0.01f, t.localScale.y) * 0.015f, 0f);
                var lamp = lightT.GetComponent<Light>();
                if (lamp == null) lamp = lightT.gameObject.AddComponent<Light>();
                lamp.type = LightType.Point;
                lamp.color = new Color(1f, 0.94f, 0.84f);
                lamp.intensity = 1.1f;
                lamp.range = 6.0f;
                lamp.shadows = LightShadows.None;
            }

            Debug.Log("[Setup] ✔ 天花板：14×12 米封顶（内表面 y=3.0，阴影投射已关闭）+ 4 盏吸顶灯");
        }

        /// <summary>
        /// 补墙：办公室的围合原本全靠美术拼件（wall_standard 3 米 / wall_medium 2.5 米 /
        /// wall_small 1.75 米），实际有四处边界是敞开的，玩家走过去就是空的天空盒（地板也到边为止）。
        /// 这里按实测包围盒补四段 3 米高的墙板，凑成完整房间：
        ///   ① 南翼南墙 z=-8.0（x -6.75~-0.75）
        ///   ② 南翼东墙 x=-0.75（z -8~-4，地板在这一段以东并不存在）
        ///   ③ 大厅 + 走廊南墙 z=-4.0（x -0.85~7.25）
        ///   ④ 东端墙 x=7.25（z -4~4）
        /// 墙板带 BoxCollider，玩家不能穿出去；外侧再压一条踢脚线，避免"凭空一块灰片"的观感。
        /// </summary>
        static void SetupWalls()
        {
            var root = GameObject.Find("OfficeWalls");
            if (root == null) root = new GameObject("OfficeWalls");
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var wallMat = EnsureSetupMaterial("Assets/Materials/OfficeWallMat.mat",
                new Color(0.855f, 0.845f, 0.805f), 0.10f);
            var skirtMat = EnsureSetupMaterial("Assets/Materials/OfficeWallSkirtingMat.mat",
                new Color(0.40f, 0.40f, 0.39f), 0.22f);

            // 名称 / 中心点 / 尺寸（单位：米）
            // 第 25 轮：Wall_CorridorEast 只留「茶水间东侧」那段（z -4~0.1），
            // 走廊东端（z 0.1~3.9）留空 —— 安全出口门从这里出去，接门外的开阔场地。
            var defs = new[]
            {
                new { name = "Wall_SouthWing",   pos = new Vector3(-3.75f, 1.50f, -8.10f), size = new Vector3(6.00f, 3.00f, 0.20f) },
                new { name = "Wall_WingEast",    pos = new Vector3(-0.85f, 1.50f, -6.00f), size = new Vector3(0.20f, 3.00f, 4.00f) },
                new { name = "Wall_LobbySouth",  pos = new Vector3(3.15f, 1.50f, -4.10f), size = new Vector3(8.20f, 3.00f, 0.20f) },
                new { name = "Wall_CorridorEast",pos = new Vector3(7.15f, 1.50f, -1.95f), size = new Vector3(0.20f, 3.00f, 4.10f) },
            };

            foreach (var d in defs)
            {
                Transform t = root.transform.Find(d.name);
                if (t == null)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = d.name;
                    t = go.transform;
                    t.SetParent(root.transform, false);
                }
                t.localPosition = d.pos;
                t.localRotation = Quaternion.identity;
                t.localScale = d.size;

                var mr = t.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.sharedMaterial = wallMat;
                    mr.shadowCastingMode = ShadowCastingMode.On;
                    mr.receiveShadows = true;
                }
                var col = t.GetComponent<Collider>();
                if (col == null) t.gameObject.AddComponent<BoxCollider>();
                col = t.GetComponent<Collider>();
                if (col != null) col.enabled = true;

                // 踢脚线：底边 0.12 米高、比墙厚 4 厘米
                Transform skirt = root.transform.Find(d.name + "_Skirt");
                if (skirt == null)
                {
                    var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    s.name = d.name + "_Skirt";
                    var sc = s.GetComponent<Collider>();
                    if (sc != null) UnityEngine.Object.DestroyImmediate(sc);
                    skirt = s.transform;
                    skirt.SetParent(root.transform, false);
                }
                skirt.localPosition = new Vector3(d.pos.x, 0.06f, d.pos.z);
                skirt.localRotation = Quaternion.identity;
                skirt.localScale = new Vector3(d.size.x + 0.04f, 0.12f, d.size.z + 0.04f);
                var smr = skirt.GetComponent<MeshRenderer>();
                if (smr != null) smr.sharedMaterial = skirtMat;
            }

            Debug.Log("[Setup] ✔ 补墙：" + defs.Length + " 段 3 米高墙板（南翼南墙 z=-8 / 南翼东墙 x=-0.75 / "
                      + "大厅南墙 z=-4 / 茶水间东侧 x=7.25(仅 z -4~0.1，走廊东端留开口接门外广场)），"
                      + "带 BoxCollider + 踢脚线");
        }

        /// <summary>
        /// 灭火器墙面挂钩板：背板 + 挂钩横杆两块装饰，让「灭火器贴墙」在画面上真实成立。
        /// wallNormal：从瓶心指向墙面的方向（+Z / -Z / -X 等）。
        /// </summary>
        static void SetupExtinguisherWallBracket(Vector3 bottlePos, Vector3 wallNormal, string name)
        {
            var root = GameObject.Find("ExtinguisherBrackets");
            if (root == null) root = new GameObject("ExtinguisherBrackets");
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            var plateMat = EnsureSetupMaterial("Assets/Materials/ExtinguisherBracketMat.mat",
                new Color(0.60f, 0.10f, 0.10f), 0.30f, new Color(0.26f, 0.02f, 0.02f));

            bool alongZ = Mathf.Abs(wallNormal.z) > 0.5f;

            // 背板（贴墙）
            Transform plate = root.transform.Find(name);
            if (plate == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                plate = go.transform;
                plate.SetParent(root.transform, false);
                var c = go.GetComponent<Collider>();
                if (c != null) UnityEngine.Object.DestroyImmediate(c);
            }
            plate.position = new Vector3(bottlePos.x, 0.55f, bottlePos.z) + wallNormal * 0.30f;
            plate.rotation = Quaternion.identity;
            plate.localScale = alongZ ? new Vector3(0.50f, 0.90f, 0.03f) : new Vector3(0.03f, 0.90f, 0.50f);
            var mr = plate.GetComponent<MeshRenderer>();
            if (mr != null) { mr.sharedMaterial = plateMat; mr.shadowCastingMode = ShadowCastingMode.On; }

            // 挂钩横杆（托住瓶身提把）
            Transform hook = root.transform.Find(name + "_Hook");
            if (hook == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name + "_Hook";
                hook = go.transform;
                hook.SetParent(root.transform, false);
                var c = go.GetComponent<Collider>();
                if (c != null) UnityEngine.Object.DestroyImmediate(c);
            }
            hook.position = new Vector3(bottlePos.x, 0.70f, bottlePos.z) + wallNormal * 0.20f;
            hook.rotation = Quaternion.identity;
            hook.localScale = alongZ ? new Vector3(0.36f, 0.05f, 0.05f) : new Vector3(0.05f, 0.05f, 0.36f);
            var hmr = hook.GetComponent<MeshRenderer>();
            if (hmr != null) { hmr.sharedMaterial = plateMat; hmr.shadowCastingMode = ShadowCastingMode.On; }
        }

        /// <summary>
        /// 第 25 轮：安全出口门外 = 开阔场地（原「东端墙」把门后封成 2×3.8 米的死角）。
        /// 做法：把 Wall_CorridorEast 只留茶水间那段（z -4~0.1），走廊东端（z 0.1~3.9）开口，
        /// 门口往东铺一块 6.5×8 米的室外广场：地面 + 三面 1.1 米矮墙 + 集合点标识 + 两盏庭院灯 + 绿化。
        /// </summary>
        static void SetupOutdoorPlaza()
        {
            var root = GameObject.Find("OutdoorPlaza");
            if (root == null) root = new GameObject("OutdoorPlaza");
            root.transform.position = Vector3.zero;
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            // 广场范围：x 7.25~13.75（宽 6.5），z -4~4（深 8），地面顶面 y=0 与室内地砖齐平
            const float cx = 10.50f, cz = 0.00f;
            const float w = 6.50f, d = 8.00f;

            var groundMat = EnsureSetupMaterial("Assets/Materials/PlazaGroundMat.mat",
                new Color(0.46f, 0.47f, 0.45f), 0.06f);
            var lowWallMat = EnsureSetupMaterial("Assets/Materials/PlazaFenceMat.mat",
                new Color(0.70f, 0.69f, 0.66f), 0.08f);
            var yellowMat = EnsureSetupMaterial("Assets/Materials/PlazaMarkMat.mat",
                new Color(0.92f, 0.78f, 0.16f), 0.20f, new Color(0.30f, 0.24f, 0.02f));
            var greenMat = EnsureSetupMaterial("Assets/Materials/PlazaSignMat.mat",
                new Color(0.10f, 0.48f, 0.24f), 0.20f, new Color(0.12f, 0.42f, 0.18f));

            Boxing(root.transform, "PlazaGround", new Vector3(cx, -0.05f, cz), new Vector3(w, 0.10f, d), groundMat, true);

            // 三面 1.1 米矮围墙（南 / 北 / 东）；西侧不砌墙 —— 敞开接办公室走廊开口
            Boxing(root.transform, "PlazaFence_South", new Vector3(cx, 0.55f, cz - d * 0.5f + 0.05f), new Vector3(w, 1.10f, 0.10f), lowWallMat, true);
            Boxing(root.transform, "PlazaFence_North", new Vector3(cx, 0.55f, cz + d * 0.5f - 0.05f), new Vector3(w, 1.10f, 0.10f), lowWallMat, true);
            Boxing(root.transform, "PlazaFence_East", new Vector3(cx + w * 0.5f - 0.05f, 0.55f, cz), new Vector3(0.10f, 1.10f, d), lowWallMat, true);

            // 集合点：黄色地面标线（圆形盘，不是方板——从门里看一块黄方块不像集合点）+ 绿色「集合点」立牌
            Transform ring = root.transform.Find("PlazaAssembleRing");
            if (ring != null && ring.GetComponent<MeshFilter>() != null
                && ring.GetComponent<MeshFilter>().sharedMesh != null
                && ring.GetComponent<MeshFilter>().sharedMesh.name.ToLower().Contains("cube"))
            {
                // 旧版是 2.6×2.6 的方块地毯，重建为圆盘
                UnityEngine.Object.DestroyImmediate(ring.gameObject);
                ring = null;
            }
            if (ring == null)
            {
                var ringGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ringGo.name = "PlazaAssembleRing";
                ring = ringGo.transform;
                ring.SetParent(root.transform, false);
                var rc = ringGo.GetComponent<Collider>();
                if (rc != null) UnityEngine.Object.DestroyImmediate(rc);
            }
            ring.localPosition = new Vector3(9.20f, 0.005f, 0.00f);
            ring.localRotation = Quaternion.identity;
            ring.localScale = new Vector3(2.60f, 0.005f, 2.60f);   // 直径 2.6 米、厚 1 厘米
            {
                var rmr = ring.GetComponent<MeshRenderer>();
                if (rmr != null) { rmr.sharedMaterial = yellowMat; rmr.shadowCastingMode = ShadowCastingMode.Off; }
            }

            // 绿色立牌：正对出口门方向（玩家一出门就能看到），带自发光，避免背光时整块发黑
            Boxing(root.transform, "PlazaAssembleSign", new Vector3(10.90f, 1.05f, -1.60f), new Vector3(0.06f, 1.80f, 1.20f), greenMat, true);
            Boxing(root.transform, "PlazaAssembleSign_POST", new Vector3(10.90f, 0.45f, -1.60f), new Vector3(0.10f, 0.90f, 0.10f), lowWallMat, false);

            // 两盏庭院灯（灯柱 + 灯头 + 点光），让门外不像黑走廊
            for (int i = 0; i < 2; i++)
            {
                float lx = i == 0 ? 8.40f : 12.20f;
                float lz = i == 0 ? -2.60f : 2.90f;
                Boxing(root.transform, "PlazaLampPost_" + (i + 1), new Vector3(lx, 1.60f, lz), new Vector3(0.12f, 3.20f, 0.12f), lowWallMat, false);
                Boxing(root.transform, "PlazaLampHead_" + (i + 1), new Vector3(lx, 3.28f, lz), new Vector3(0.34f, 0.20f, 0.34f), yellowMat, false);

                var lightGo = root.transform.Find("PlazaLamp_" + (i + 1));
                if (lightGo == null)
                {
                    var lg = new GameObject("PlazaLamp_" + (i + 1));
                    lightGo = lg.transform;
                    lightGo.SetParent(root.transform, false);
                    var l = lg.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.color = new Color(1.0f, 0.95f, 0.82f);
                    l.intensity = 1.6f;
                    l.range = 7.0f;
                    l.shadows = LightShadows.None;
                }
                lightGo.position = new Vector3(lx, 3.05f, lz);
                var lc = lightGo.GetComponent<Light>();
                if (lc != null) { lc.type = LightType.Point; lc.intensity = 1.6f; lc.range = 7.0f; lc.enabled = true; }
            }

            // 绿化：两个花箱，给广场一点尺度参照
            Boxing(root.transform, "PlazaPlanter_1", new Vector3(8.10f, 0.28f, 3.10f), new Vector3(0.90f, 0.55f, 0.55f), lowWallMat, true);
            Boxing(root.transform, "PlazaPlanter_2", new Vector3(12.60f, 0.28f, -3.10f), new Vector3(0.90f, 0.55f, 0.55f), lowWallMat, true);

            // 第 28 轮：门外广场知识墙（3 块立式展板 + 顶部标语 + 地面喷涂字），展板竖在东侧矮墙前朝西
            var board = root.GetComponent<PlazaKnowledgeBoard>() ?? root.AddComponent<PlazaKnowledgeBoard>();
            board.boardCenter = new Vector3(13.40f, 0f, 0f);
            board.facingYaw = 90f;
            board.reverseOrder = false;
            board.panelBottom = 0.62f;
            board.floorTextCenter = new Vector3(10.90f, 0.02f, 1.55f);
            board.floorTextYaw = 90f;

            Debug.Log("[Setup] ✔ 门外开阔场地：广场 6.5×8 米（x 7.25~13.75 / z -4~4）、三面 1.1 米矮墙、"
                      + "集合点标识 2 处、庭院灯 2 盏、花箱 2 个；走廊东端 z 0.1~3.9 开口通向广场；"
                      + "另加知识墙 3 块展板（中心 x 13.40 朝西）+ 地面标语 1 条");
        }

        /// <summary>建/复用一个立方体装饰件（可选带 BoxCollider）。</summary>
        static void Boxing(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, bool collider)
        {
            Transform t = parent.Find(name);
            if (t == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                t = go.transform;
                t.SetParent(parent, false);
            }
            t.localPosition = pos;
            t.localRotation = Quaternion.identity;
            t.localScale = size;

            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
            }

            var col = t.GetComponent<Collider>();
            if (collider)
            {
                if (col == null) col = t.gameObject.AddComponent<BoxCollider>();
                col.enabled = true;
            }
            else if (col != null)
            {
                UnityEngine.Object.DestroyImmediate(col);
            }
        }

        /// <summary>取/建一个 URP Lit 材质资产（场景保存后引用不会丢）。</summary>
        static Material EnsureSetupMaterial(string assetPath, Color color, float smoothness, Color? emission = null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                mat = new Material(shader);
                mat.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                AssetDatabase.CreateAsset(mat, assetPath);
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emission.Value);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ================================================================
        // 10. 新流程编排：操作演练 + 正式第 1 阶段（设备起火 / 电源 / 随机出生）+ 第 2 阶段
        // ================================================================
        static void SetupDrillScenario(GameObject rig)
        {
            Vector3 devicePos = new Vector3(-2.40f, 0f, 2.90f);   // desk_5 西侧、北墙前 0.9 米的开阔地

            // ============================================================
            // 三套起火情景：起火点不同 / 灭火器不同 / 题库不同 / 出生点不同，
            // 但终点相同（都沿「安全出口」标识撤到同一扇出口门）。
            // ============================================================
            // 灭火器一律「贴墙」摆放（第 25 轮）：靠近墙面、固定在自己的挂钩板上，
            // 不占走道中线 —— 玩家按就近原则取最近的那台。
            Vector3 ext01Pos = new Vector3(0.85f, 0.16f, 3.45f);      // 情景一 电气：北墙，配电柜东边 3.3 米（兼操作演练取瓶）
            Vector3 ext02Pos = new Vector3(3.45f, 0.16f, -3.55f);     // 情景二 纸张：大厅南墙，纸张火东南 2.0 米
            Vector3 ext03Pos = new Vector3(5.95f, 0.16f, -3.80f);     // 情景三 茶水间：南墙贴边（原东南墙角被 stool_2 占住，瓶体会被顶飞），到小家电火 3.1 米

            // ---------------- 出生点：每套情景 3 个候选（随机取一个，尽量避开上一次） ----------------
            var spawnRoot = GameObject.Find("DrillSpawnPoints");
            if (spawnRoot == null) spawnRoot = new GameObject("DrillSpawnPoints");
            var deviceLook = new Vector3(devicePos.x, 1.0f, devicePos.z);

            var g1 = EnsureGroup(spawnRoot.transform, "SpawnGroup01");
            var g2 = EnsureGroup(spawnRoot.transform, "SpawnGroup02");
            var g3 = EnsureGroup(spawnRoot.transform, "SpawnGroup03");

            // 情景一（电气）：北侧办公区，站在设备柜东南方向看设备
            var spawn0 = EnsureSpawn(g1, "Spawn01", new Vector3(2.90f, 0f, 1.10f), deviceLook);
            var spawn1 = EnsureSpawn(g1, "Spawn02", new Vector3(1.00f, 0f, 1.60f), deviceLook);
            var spawn2 = EnsureSpawn(g1, "Spawn03", new Vector3(4.30f, 0f, 1.20f), deviceLook);

            // 情景二（纸张 / 档案）：中部房间，站在南侧工位北面看向燃烧的办公桌
            // 起火点必须避开这台打印机（printer 包围盒 x[0.20,0.96] z[-3.85,-2.89]）：
            // 玩家视角实测放在桌子正中会被打印机整个挡住，只看得见一张浮在机器上方的血量条。
            var paperFirePos = new Vector3(1.42f, 1.06f, -3.45f);
            var paperLook = new Vector3(paperFirePos.x, 1.0f, paperFirePos.z);
            var spawnP0 = EnsureSpawn(g2, "Spawn01", new Vector3(3.30f, 0f, -1.20f), paperLook);
            var spawnP1 = EnsureSpawn(g2, "Spawn02", new Vector3(4.20f, 0f, -1.40f), paperLook);
            var spawnP2 = EnsureSpawn(g2, "Spawn03", new Vector3(2.60f, 0f, -1.60f), paperLook);

            // 情景三（茶水间）：东南侧，站在茶水台西/南面看向台面火
            var pantryFirePos = new Vector3(6.30f, 1.06f, -0.74f);
            var pantryLook = new Vector3(pantryFirePos.x, 1.0f, pantryFirePos.z);
            var spawnK0 = EnsureSpawn(g3, "Spawn01", new Vector3(5.00f, 0f, -2.60f), pantryLook);
            var spawnK1 = EnsureSpawn(g3, "Spawn02", new Vector3(4.60f, 0f, -2.20f), pantryLook);
            var spawnK2 = EnsureSpawn(g3, "Spawn03", new Vector3(3.60f, 0f, -2.60f), pantryLook);

            // ---------------- 起火的电气设备（配电柜） ----------------
            var device = GameObject.Find("DeviceCabinet");
            if (device == null)
            {
                device = GameObject.CreatePrimitive(PrimitiveType.Cube);
                device.name = "DeviceCabinet";
            }
            device.transform.position = new Vector3(devicePos.x, 0.525f, devicePos.z);
            device.transform.rotation = Quaternion.identity;
            device.transform.localScale = new Vector3(0.70f, 1.05f, 0.55f);
            var deviceRenderer = device.GetComponent<MeshRenderer>();
            if (deviceRenderer != null)
            {
                deviceRenderer.sharedMaterial = EnsureSetupMaterial("Assets/Materials/DeviceCabinetMat.mat",
                    new Color(0.30f, 0.31f, 0.34f), 0.35f);
                deviceRenderer.shadowCastingMode = ShadowCastingMode.On;
            }
            // 面板散热条与运行指示灯（让「设备」一眼可辨认，不是一块灰砖）
            var panel = EnsureChildCube(device.transform, "Panel", new Vector3(0f, 0.02f, -0.505f),
                new Vector3(0.56f, 0.34f, 0.02f), "Assets/Materials/DevicePanelMat.mat", new Color(0.14f, 0.15f, 0.17f), 0.25f);
            EnsureChildCube(device.transform, "LedRed", new Vector3(0.24f, 0.30f, -0.505f),
                new Vector3(0.055f, 0.055f, 0.02f), "Assets/Materials/DeviceLedMat.mat", new Color(1f, 0.25f, 0.18f), 0.6f,
                emission: new Color(1f, 0.18f, 0.10f) * 3f);

            // ---------------- 设备顶部的火焰（克隆场景自带 BaseFire000） ----------------
            var fireGo = GameObject.Find("DeviceFire");
            var template = GameObject.Find("BaseFire000");
            if (fireGo == null)
            {
                if (template != null)
                {
                    fireGo = UnityEngine.Object.Instantiate(template);
                    fireGo.name = "DeviceFire";
                }
                else
                {
                    fireGo = new GameObject("DeviceFire");
                }
            }
            // 不能挂到设备下面：设备 scale(0.7,1.05,0.55) 会把火焰压变形
            fireGo.transform.SetParent(null, true);
            fireGo.transform.position = new Vector3(devicePos.x, 1.06f, devicePos.z - 0.02f);
            float templateScale = template != null ? template.transform.localScale.x : 1f;
            fireGo.transform.localScale = Vector3.one * (templateScale * 0.55f);

            var fireSource = PrepareFireClone(fireGo, locked: true, startUnlit: true);
            // 火焰顶部再挂一个瞄准点 + 血量条（FireHealthBar 自己建世界空间 UI）
            var aim = fireGo.transform.Find("Aim");
            if (aim == null)
            {
                var aimGo = new GameObject("Aim").transform;
                aimGo.SetParent(fireGo.transform, false);
                aimGo.localPosition = new Vector3(0f, 0.30f, 0f);
                aim = aimGo;
            }
            if (fireSource != null) fireSource.aimTarget = aim;
            if (fireGo.GetComponent<FireHealthBar>() == null) fireGo.AddComponent<FireHealthBar>();
            Debug.Log("[Setup] ✔ 设备火焰 DeviceFire：断电锁定 + 火焰血量条（HP 100 起）");

            // ---------------- 设备冒烟（开始演练即播放） ----------------
            var smokeGo = GameObject.Find("DeviceSmoke");
            if (smokeGo == null) smokeGo = new GameObject("DeviceSmoke");
            smokeGo.transform.SetParent(null, true);
            smokeGo.transform.position = new Vector3(devicePos.x, 1.40f, devicePos.z);
            var smokePs = smokeGo.GetComponent<ParticleSystem>();
            if (smokePs == null) smokePs = smokeGo.AddComponent<ParticleSystem>();
            ConfigureSmoke(smokePs, new Color(0.32f, 0.32f, 0.34f, 0.62f),
                rate: 26f, speed: 0.55f, size: 1.15f, life: 3.4f, additive: false, radius: 0.18f);
            smokePs.name = "DeviceSmoke";

            // ---------------- 电源接口（配电箱总闸，贴着北墙） ----------------
            var boxRoot = GameObject.Find("PowerBox");
            if (boxRoot == null) boxRoot = new GameObject("PowerBox");
            boxRoot.transform.position = new Vector3(devicePos.x, 1.35f, 3.68f);
            boxRoot.transform.rotation = Quaternion.identity;   // 正面（本地 -Z）朝房间内
            var powerSwitch = boxRoot.GetComponent<PowerSwitch>();
            if (powerSwitch == null) powerSwitch = boxRoot.AddComponent<PowerSwitch>();
            BuildPowerBox(boxRoot, powerSwitch);

            // ---------------- 正式第 1 阶段独立烟雾区 ----------------
            var s1Go = GameObject.Find("Stage1Smoke");
            if (s1Go == null) s1Go = new GameObject("Stage1Smoke");
            var s1 = s1Go.GetComponent<SmokeZone>();
            if (s1 == null) s1 = s1Go.AddComponent<SmokeZone>();
            s1.emitterCount = 5;
            s1.start = new Vector3(-2.90f, 1.55f, 1.20f);
            s1.end = new Vector3(-1.60f, 1.55f, 3.30f);
            s1.floorClearance = 1.00f;
            s1.particleSize = 1.00f;
            s1.ratePerSecond = 18f;

            // ---------------- 情景二 / 三的起火点（克隆 BaseFire000，不继承模板的进度与事件） ----------------
            var paperFire = EnsureScenarioFire("PaperFire", paperFirePos, template, 0.50f,
                "桌面纸张、档案正在燃烧！这属于 A 类固体火灾");
            var paperSmoke = EnsureScenarioSmoke("PaperSmoke",
                new Vector3(paperFirePos.x, 1.55f, paperFirePos.z), new Color(0.30f, 0.30f, 0.32f, 0.55f), 18f);
            var paperZone = EnsureRoomSmoke("Stage1Smoke_Paper",
                new Vector3(2.40f, 1.55f, -2.10f), new Vector3(1.40f, 1.55f, -3.40f), 4, 15f);

            var pantryFire = EnsureScenarioFire("PantryFire", pantryFirePos, template, 0.50f,
                "茶水间台面起火！先断电、再灭火，油类火灾不能用水");
            var pantrySmoke = EnsureScenarioSmoke("PantrySmoke",
                new Vector3(pantryFirePos.x, 1.50f, pantryFirePos.z), new Color(0.34f, 0.33f, 0.31f, 0.55f), 18f);
            var pantryZone = EnsureRoomSmoke("Stage1Smoke_Pantry",
                new Vector3(5.60f, 1.55f, 0.40f), new Vector3(6.35f, 1.55f, -1.60f), 4, 15f);

            // ---------------- 三台灭火器等间距落位 ----------------
            // 原来 01 在西墙 (-6.26,0.83)、02 在 (-3.10,1.15)，两台只隔 3.2 米；03 在 (3.70,1.60)，
            // 与 02 隔 6.9 米 —— 一台挤在角落、一台孤零零。现在改成沿房间对角均匀铺开：
            // 01 ↔ 02 = 5.65 米，02 ↔ 03 = 4.68 米，且每台都在自己情景的起火点 2~2.6 米内。
            var ext01 = GameObject.Find("Extinguisher01");
            var ext02 = GameObject.Find("Extinguisher02");
            var ext03 = GameObject.Find("Extinguisher03");
            if (ext01 != null) PlaceExtinguisher(ext01.transform, ext01Pos);
            if (ext02 != null) PlaceExtinguisher(ext02.transform, ext02Pos);
            if (ext03 != null) PlaceExtinguisher(ext03.transform, ext03Pos);

            // 墙面挂钩板：让「贴墙」不只是坐标贴近，而是看起来就挂在墙上
            SetupExtinguisherWallBracket(ext01Pos, Vector3.forward, "Extinguisher01_Bracket");
            SetupExtinguisherWallBracket(ext02Pos, Vector3.back, "Extinguisher02_Bracket");
            SetupExtinguisherWallBracket(ext03Pos, Vector3.back, "Extinguisher03_Bracket");

            float d12 = Vector3.Distance(new Vector3(ext01Pos.x, 0f, ext01Pos.z), new Vector3(ext02Pos.x, 0f, ext02Pos.z));
            float d23 = Vector3.Distance(new Vector3(ext02Pos.x, 0f, ext02Pos.z), new Vector3(ext03Pos.x, 0f, ext03Pos.z));
            Debug.Log("[Setup] ✔ 灭火器贴墙落位：01" + ext01Pos.ToString("F2") + "（北墙）/ 02" + ext02Pos.ToString("F2")
                      + "（大厅南墙）/ 03" + ext03Pos.ToString("F2") + "（茶水间南墙）"
                      + "；01↔02=" + d12.ToString("F2") + " 米，02↔03=" + d23.ToString("F2") + " 米");

            // ---------------- 操作演练用的原地板火 ----------------
            if (template != null)
            {
                var baseFire = template.GetComponent<FireSource>();
                if (baseFire != null)
                {
                    baseFire.startUnlit = true;                 // 未开始演练时不燃烧
                    baseFire.igniteFromStartScreen = false;     // 「开始演练」只点燃设备火
                    baseFire.PrepareAsUnlit();
                }
            }

            // ---------------- 总控 DrillScenario ----------------
            var scGo = GameObject.Find("DrillScenario");
            if (scGo == null) scGo = new GameObject("DrillScenario");
            var sc = scGo.GetComponent<DrillScenario>();
            if (sc == null) sc = scGo.AddComponent<DrillScenario>();
            sc.startScreen = UnityEngine.Object.FindObjectOfType<StartScreen>();
            sc.stageTwo = UnityEngine.Object.FindObjectOfType<StageTwoFlow>();
            sc.stagePanel = UnityEngine.Object.FindObjectOfType<StageOneCompletePanel>();
            sc.arrows = UnityEngine.Object.FindObjectOfType<GuidanceArrows>();
            sc.countdown = UnityEngine.Object.FindObjectOfType<DrillCountdown>();
            sc.endingScreen = UnityEngine.Object.FindObjectOfType<EndingScreen>();
            sc.hud = UnityEngine.Object.FindObjectOfType<VRDrillHud>();
            sc.tutorialFire = template != null ? template.GetComponent<FireSource>() : null;
            sc.tutorialExtinguisher = ext01 != null ? ext01.GetComponent<Extinguisher>() : null;
            sc.tutorialExtinguisherHome = ext01Pos;
            sc.deviceFire = fireSource;
            sc.deviceSmoke = smokePs;
            sc.powerSwitch = powerSwitch;
            sc.deviceExtinguisher = ext01 != null ? ext01.GetComponent<Extinguisher>() : null;
            sc.stage1Smoke = s1;
            sc.rigRoot = rig != null ? rig.transform : null;

            var e1 = ext01 != null ? ext01.GetComponent<Extinguisher>() : null;
            var e2 = ext02 != null ? ext02.GetComponent<Extinguisher>() : null;
            var e3 = ext03 != null ? ext03.GetComponent<Extinguisher>() : null;
            sc.allExtinguishers = new[] { e1, e2, e3 };

            // ---------------- 三套情景：起火点 / 灭火器 / 题库组 / 出生点 ----------------
            sc.scenarios = new[]
            {
                new DrillScenario.FireScenario
                {
                    title = "情景一 · 电气设备起火（配电柜）",
                    shortName = "电气",
                    intro = "配电柜短路冒烟起火，火焰带电——必须先切断总闸，再用旁边的二氧化碳灭火器扑灭（不留残渣，不导电）。",
                    fire = fireSource,
                    smoke = smokePs,
                    roomSmoke = s1,
                    extinguisher = e1,
                    extinguisherHome = ext01Pos,
                    quizSetIndex = 2,                 // C 组：电气设备起火 · 先断电
                    extinguisherKind = ExtinguisherKind.CO2,   // 第 28 轮：电气火配二氧化碳
                    powerSwitch = powerSwitch,
                    requirePowerCut = true,
                    spawnPoints = new[] { spawn0, spawn1, spawn2 }
                },
                new DrillScenario.FireScenario
                {
                    title = "情景二 · 办公桌纸张起火",
                    shortName = "纸张",
                    intro = "南侧工位上一摞打印纸和档案袋被引燃，属于 A 类固体火灾——用干粉灭火器对准火焰根部，灭后翻开纸堆防复燃。",
                    fire = paperFire,
                    smoke = paperSmoke,
                    roomSmoke = paperZone,
                    extinguisher = e2,
                    extinguisherHome = ext02Pos,
                    quizSetIndex = 4,                 // E 组：纸张 / 固体可燃物
                    extinguisherKind = ExtinguisherKind.PowderABC,   // 第 28 轮：A 类固体火配 ABC 干粉
                    powerSwitch = null,
                    requirePowerCut = false,
                    skipEvacuation = true,            // 第 25 轮：就近原则，灭火后直奔安全出口门
                    spawnPoints = new[] { spawnP0, spawnP1, spawnP2 }
                },
                new DrillScenario.FireScenario
                {
                    title = "情景三 · 茶水间小家电起火",
                    shortName = "茶水间",
                    intro = "茶水间的电水壶/微波炉插排过载起火，台面油污被引燃——用湿化学（水基）灭火器从侧面近距离覆盖，靠冷却和皂化压住油火，不能直接泼水。",
                    fire = pantryFire,
                    smoke = pantrySmoke,
                    roomSmoke = pantryZone,
                    extinguisher = e3,
                    extinguisherHome = ext03Pos,
                    quizSetIndex = 5,                 // F 组：茶水间小家电 / 油类
                    extinguisherKind = ExtinguisherKind.WetChemical,  // 第 28 轮：F 类油火配水基/湿化学
                    powerSwitch = null,
                    requirePowerCut = false,
                    skipEvacuation = true,            // 第 25 轮：就近原则，灭火后直奔安全出口门
                    spawnPoints = new[] { spawnK0, spawnK1, spawnK2 }
                }
            };
            sc.randomScenario = true;

            // 兼容字段：默认情景 = 第 1 套
            sc.spawnPoints = new[] { spawn0, spawn1, spawn2 };
            sc.exitExtinguisher = e3;                 // 离安全出口最近的一台（第 2 阶段备用瓶）
            sc.randomSpawn = true;
            sc.requirePowerCutBeforeExtinguish = true;

            // 操作演练站位：站在地板火东侧 3 米，面向西（先看火，再取西墙的火火器）
            var tutGo = GameObject.Find("TutorialSpawn");
            if (tutGo == null) tutGo = new GameObject("TutorialSpawn");
            tutGo.transform.position = new Vector3(0.20f, 0f, 1.40f);
            var lookAtFire = new Vector3(-1.79f, 0.6f, 0.29f);
            Vector3 dir = lookAtFire - tutGo.transform.position; dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f) tutGo.transform.rotation = Quaternion.LookRotation(dir);
            sc.tutorialSpawn = tutGo.transform;

            // 第 2 阶段把头显提示条也接上
            var flow = UnityEngine.Object.FindObjectOfType<StageTwoFlow>();
            if (flow != null)
            {
                flow.hud = sc.hud;
                flow.useSmoke = false;
                flow.requireCrouch = false;
                flow.spareQuizSetIndex = 3;
                if (ext03 != null) flow.spareExtinguisher = ext03.GetComponent<Extinguisher>();
                // 三台灭火器现在铺开到房间各处，最近的一台离出口门也有 4.3 米；
                // 第 2 阶段开始时把备用瓶搬到门旁空位，玩家到大厅就能顺手拿到。
                flow.autoMoveSpareExtinguisher = true;
                flow.spareExtinguisherSpot = new Vector3(4.95f, 0f, 3.20f);
            }

            Debug.Log("[Setup] ✔ 新流程：操作演练 + 正式第 1 阶段（3 个随机出生点 / 设备火 / 电源箱 / 独立烟雾 / 火焰血量条）"
                      + " + 第 2 阶段（安全标识 → 出口火 → Extinguisher03 疏散题 → 开门）");
        }

        /// <summary>出生点分组（每套情景一个父物体）。</summary>
        static Transform EnsureGroup(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t == null)
            {
                var go = new GameObject(name);
                t = go.transform;
                t.SetParent(parent, false);
            }
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            return t;
        }

        /// <summary>情景火源：克隆 BaseFire000，清进度与事件表、去碰撞体、补瞄准点与血量条。</summary>
        static FireSource EnsureScenarioFire(string name, Vector3 pos, GameObject template, float scale, string lockHint)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                if (template != null)
                {
                    go = UnityEngine.Object.Instantiate(template);
                    go.name = name;
                }
                else go = new GameObject(name);
            }
            go.transform.SetParent(null, true);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.identity;
            float ts = template != null ? template.transform.localScale.x : 1f;
            go.transform.localScale = Vector3.one * (ts * scale);

            var fire = PrepareFireClone(go, locked: false, startUnlit: true);
            if (fire != null) fire.lockHint = lockHint;

            var aim = go.transform.Find("Aim");
            if (aim == null)
            {
                var aimGo = new GameObject("Aim").transform;
                aimGo.SetParent(go.transform, false);
                aimGo.localPosition = new Vector3(0f, 0.30f, 0f);
                aim = aimGo;
            }
            if (fire != null) fire.aimTarget = aim;
            if (go.GetComponent<FireHealthBar>() == null) go.AddComponent<FireHealthBar>();
            Debug.Log("[Setup] ✔ 情景火源 " + name + " @ " + pos.ToString("F2") + "（scale×" + scale + "，含血量条）");
            return fire;
        }

        /// <summary>情景起火点的冒烟粒子（开始演练即播放）。</summary>
        static ParticleSystem EnsureScenarioSmoke(string name, Vector3 pos, Color color, float rate)
        {
            var go = GameObject.Find(name);
            if (go == null) go = new GameObject(name);
            go.transform.SetParent(null, true);
            go.transform.position = pos;
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null) ps = go.AddComponent<ParticleSystem>();
            ConfigureSmoke(ps, color, rate: rate, speed: 0.55f, size: 1.05f, life: 3.2f, additive: false, radius: 0.16f);
            ps.name = name;
            return ps;
        }

        /// <summary>每套情景一条独立的房间烟雾区（SmokeZone 一旦 Build 过就复用旧发射点，不能共用）。</summary>
        static SmokeZone EnsureRoomSmoke(string name, Vector3 start, Vector3 end, int emitters, float rate)
        {
            var go = GameObject.Find(name);
            if (go == null) go = new GameObject(name);
            var zone = go.GetComponent<SmokeZone>();
            if (zone == null) zone = go.AddComponent<SmokeZone>();
            zone.emitterCount = emitters;
            zone.start = start;
            zone.end = end;
            zone.floorClearance = 1.00f;
            zone.particleSize = 0.90f;
            zone.ratePerSecond = rate;
            return zone;
        }

        /// <summary>出生点：位置 + 朝向设备（yaw 取水平朝向）。</summary>
        static Transform EnsureSpawn(Transform parent, string name, Vector3 pos, Vector3 lookAt)
        {
            var t = parent.Find(name);
            if (t == null)
            {
                var go = new GameObject(name);
                t = go.transform;
                t.SetParent(parent, false);
            }
            Vector3 dir = lookAt - pos;
            dir.y = 0f;
            t.position = pos;
            t.rotation = dir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(dir) : Quaternion.identity;
            return t;
        }

        /// <summary>子物体方块（设备面板、指示灯这类纯装饰件）。</summary>
        static Transform EnsureChildCube(Transform parent, string name, Vector3 localPos, Vector3 localScale,
                                         string matPath, Color color, float smoothness, Color? emission = null)
        {
            var t = parent.Find(name);
            if (t == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                var col = go.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.DestroyImmediate(col);
                t = go.transform;
                t.SetParent(parent, false);
            }
            t.localPosition = localPos;
            t.localRotation = Quaternion.identity;
            t.localScale = localScale;
            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.sharedMaterial = EnsureSetupMaterial(matPath, color, smoothness, emission);
            return t;
        }

        /// <summary>把克隆出来的火焰整理成「可点燃 / 可锁定 / 血量归零」的新火源。</summary>
        static FireSource PrepareFireClone(GameObject go, bool locked, bool startUnlit)
        {
            // 先取组件再删碰撞体：DestroyImmediate 后同帧拿到的是待销毁对象
            var fires = go.GetComponentsInChildren<FireSource>(true);
            var rootFire = go.GetComponent<FireSource>();
            foreach (var extra in fires)
                if (extra != rootFire) UnityEngine.Object.DestroyImmediate(extra);

            int removed = 0;
            foreach (var c in go.GetComponentsInChildren<Collider>(true))
            {
                UnityEngine.Object.DestroyImmediate(c);
                removed++;
            }
            if (removed > 0) Debug.Log("[Setup] 火焰克隆体移除 " + removed + " 个碰撞体（模板的 8.7 米盒子会把刚体顶飞）");

            if (rootFire == null) rootFire = go.AddComponent<FireSource>();
            rootFire.onExtinguished.RemoveAllListeners();       // 不能继承模板的事件表
            rootFire.progress01 = 0f;
            rootFire.extinguishThreshold = 2.2f;
            rootFire.startUnlit = startUnlit;
            rootFire.igniteFromStartScreen = false;
            rootFire.extinguishLocked = locked;
            rootFire.lockHint = "火焰仍带电，先切断电源！";
            if (rootFire.particles == null || rootFire.particles.Length == 0)
                rootFire.particles = go.GetComponentsInChildren<ParticleSystem>(true);

            // 火光：克隆可能没带 Light，补一盏（否则室内火焰只亮一点粒子）
            if (rootFire.fireLight == null)
            {
                var lgo = go.transform.Find("FireLight");
                if (lgo == null)
                {
                    var t = new GameObject("FireLight").transform;
                    t.SetParent(go.transform, false);
                    t.localPosition = new Vector3(0f, 0.18f, 0f);
                    lgo = t;
                }
                var light = lgo.GetComponent<Light>();
                if (light == null) light = lgo.gameObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.45f, 0.15f);
                light.intensity = 1.6f;
                light.range = 3.2f;
                light.shadows = LightShadows.None;
                rootFire.fireLight = light;
            }

            rootFire.PrepareAsUnlit();
            return rootFire;
        }

        /// <summary>配电箱：箱体 + 闸柄 + 指示灯 + 电弧火花 + 世界空间标签。</summary>
        static void BuildPowerBox(GameObject root, PowerSwitch powerSwitch)
        {
            // 箱体
            var body = root.transform.Find("Body");
            if (body == null)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = "Body";
                var bc = b.GetComponent<Collider>();
                if (bc != null) { bc.isTrigger = false; }
                body = b.transform;
                body.SetParent(root.transform, false);
            }
            body.localPosition = Vector3.zero;
            body.localRotation = Quaternion.identity;
            body.localScale = new Vector3(0.50f, 0.70f, 0.20f);
            var bmr = body.GetComponent<MeshRenderer>();
            if (bmr != null)
                bmr.sharedMaterial = EnsureSetupMaterial("Assets/Materials/PowerBoxMat.mat",
                    new Color(0.55f, 0.57f, 0.60f), 0.45f);

            // 闸柄：pivot 在箱体正面顶部，切断时向下扳
            var leverPivot = root.transform.Find("LeverPivot");
            if (leverPivot == null)
            {
                var lp = new GameObject("LeverPivot").transform;
                lp.SetParent(root.transform, false);
                leverPivot = lp;
            }
            leverPivot.localPosition = new Vector3(0f, 0.16f, -0.115f);
            leverPivot.localRotation = Quaternion.identity;
            var lever = leverPivot.Find("Lever");
            if (lever == null)
            {
                var l = GameObject.CreatePrimitive(PrimitiveType.Cube);
                l.name = "Lever";
                var lc = l.GetComponent<Collider>();
                if (lc != null) UnityEngine.Object.DestroyImmediate(lc);
                lever = l.transform;
                lever.SetParent(leverPivot, false);
            }
            lever.localPosition = new Vector3(0f, -0.13f, 0f);
            lever.localScale = new Vector3(0.075f, 0.26f, 0.075f);
            var lmr = lever.GetComponent<MeshRenderer>();
            if (lmr != null)
                lmr.sharedMaterial = EnsureSetupMaterial("Assets/Materials/PowerLeverMat.mat",
                    new Color(0.92f, 0.55f, 0.16f), 0.5f);

            // 指示灯
            var lampT = root.transform.Find("IndicatorLight");
            if (lampT == null)
            {
                var t = new GameObject("IndicatorLight").transform;
                t.SetParent(root.transform, false);
                lampT = t;
            }
            lampT.localPosition = new Vector3(0.17f, 0.24f, -0.115f);
            var lamp = lampT.GetComponent<Light>();
            if (lamp == null) lamp = lampT.gameObject.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, 0.28f, 0.18f);
            lamp.intensity = 1.4f;
            lamp.range = 1.0f;
            lamp.shadows = LightShadows.None;

            // 电弧火花
            var sparkT = root.transform.Find("Sparks");
            if (sparkT == null)
            {
                var t = new GameObject("Sparks").transform;
                t.SetParent(root.transform, false);
                sparkT = t;
            }
            sparkT.localPosition = new Vector3(0.19f, 0.05f, -0.14f);
            var sparkPs = sparkT.GetComponent<ParticleSystem>();
            if (sparkPs == null) sparkPs = sparkT.gameObject.AddComponent<ParticleSystem>();
            ConfigureSmoke(sparkPs, new Color(1f, 0.72f, 0.22f, 0.95f),
                rate: 14f, speed: 0.35f, size: 0.10f, life: 0.55f, additive: true, radius: 0.02f);

            // 世界空间标签（始终朝相机）
            // 注意：给普通 GameObject 加 Canvas 会把 Transform 替换成 RectTransform，
            // 缓存的 Transform 引用当场失效（MissingReferenceException），所以必须一开始就用 RectTransform 创建。
            var canvasGo = root.transform.Find("LabelCanvas");
            GameObject labelGo = canvasGo != null ? canvasGo.gameObject : null;
            if (labelGo == null)
            {
                labelGo = new GameObject("LabelCanvas", typeof(RectTransform));
                labelGo.transform.SetParent(root.transform, false);
            }
            var canvasRt = (RectTransform)labelGo.transform;
            canvasRt.localPosition = new Vector3(0f, 0.52f, -0.16f);
            canvasRt.localScale = Vector3.one * 0.0014f;
            var canvas = labelGo.GetComponent<Canvas>();
            if (canvas == null) canvas = labelGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 12;
            canvasRt.sizeDelta = new Vector2(360f, 110f);

            var bgGo = labelGo.transform.Find("Bg") != null ? labelGo.transform.Find("Bg").gameObject : null;
            if (bgGo == null)
            {
                bgGo = new GameObject("Bg", typeof(RectTransform));
                bgGo.transform.SetParent(labelGo.transform, false);
            }
            var bg = bgGo.GetComponent<Image>();
            if (bg == null) bg = bgGo.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.08f, 0.10f, 0.86f);
            var bgRt = (RectTransform)bgGo.transform;
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;

            var textGo = labelGo.transform.Find("Label") != null ? labelGo.transform.Find("Label").gameObject : null;
            if (textGo == null)
            {
                textGo = new GameObject("Label", typeof(RectTransform));
                textGo.transform.SetParent(labelGo.transform, false);
            }
            var text = textGo.GetComponent<Text>();
            if (text == null) text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null) text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.text = powerSwitch.promptText;
            text.fontSize = 34;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(1f, 0.85f, 0.35f);
            var textRt = (RectTransform)textGo.transform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(8f, 4f);
            textRt.offsetMax = new Vector2(-8f, -4f);

            powerSwitch.lever = leverPivot;
            powerSwitch.indicator = lamp;
            powerSwitch.sparks = sparkPs;
            powerSwitch.labelCanvas = canvas;
            powerSwitch.labelText = text;
            powerSwitch.maxInteractDistance = 4f;
            var boxAudio = root.GetComponent<AudioSource>();
            if (boxAudio == null) boxAudio = root.AddComponent<AudioSource>();
            boxAudio.playOnAwake = false;
            powerSwitch.audioSource = boxAudio;
            powerSwitch.cutClip = Resources.Load<AudioClip>("Sounds/SFX/ui_click");
        }

        /// <summary>统一配置一团烟 / 火花（循环、世界空间、柔边贴图资产）。</summary>
        static void ConfigureSmoke(ParticleSystem ps, Color color, float rate, float speed,
                                   float size, float life, bool additive, float radius)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startColor = color;
            main.maxParticles = 220;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;

            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = rate;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = radius;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(color.a, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.7f, 1f, 1.35f));

            string assetName = additive ? "FxSpark" : "FxSmoke";
            var mat = EnsureParticleMaterialAsset(assetName, additive);
            var pr = ps.GetComponent<ParticleSystemRenderer>();
            if (pr == null) pr = ps.gameObject.AddComponent<ParticleSystemRenderer>();
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.sharedMaterial = mat;
            pr.shadowCastingMode = ShadowCastingMode.Off;
            pr.receiveShadows = false;
        }

        /// <summary>柔边粒子材质 + 贴图存成资产（否则保存场景后引用丢失变成方块/黑块）。</summary>
        static Material EnsureParticleMaterialAsset(string name, bool additive)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");

            string texPath = "Assets/Materials/" + name + "_Tex.asset";
            string matPath = "Assets/Materials/" + name + ".mat";

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            if (tex == null)
            {
                tex = SmokeZone.CreateSoftTexture();
                tex.name = name + "_Tex";
                AssetDatabase.CreateAsset(tex, texPath);
            }
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = SmokeZone.CreateParticleMaterial(tex, additive);
                mat.name = name;
                AssetDatabase.CreateAsset(mat, matPath);
            }
            return mat;
        }

        /// <summary>把灭火器摆到指定地面位置（保持直立、贴地）。</summary>
        static void PlaceExtinguisher(Transform ext, Vector3 pos)
        {
            if (ext == null) return;
            ext.position = pos;
            ext.rotation = Quaternion.identity;
            var eb = ext.GetComponent<Extinguisher>();
            if (eb != null) eb.EnsureUprightStanding(true);
            EditorUtility.SetDirty(ext.gameObject);
        }

        /// <summary>清理场景中脚本丢失（Missing Mono Script）的组件。</summary>
        static void RemoveMissingScripts()
        {
            int removed = 0;
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    int n = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                    if (n > 0)
                    {
                        removed += n;
                        Debug.LogWarning("[Setup] 清理缺失脚本组件：" + n + " 个 @ " + t.name);
                    }
                }
            }
            if (removed > 0) Debug.Log("[Setup] ✔ 已清理缺失脚本组件共 " + removed + " 个");
        }

        // ================================================================
        // 6. 灭火器装配
        // ================================================================

        static void SetupExtinguisher(string objName, XROrigin rig, Transform leftHand, Transform rightHand)
        {
            var root = GameObject.Find(objName);
            if (root == null && !objName.EndsWith("03"))
            {
                // 场景里可能以其他名字存在（如 Fire Extinguisher），按名字兜底
                // 03 是新增的第三台，必须精确命中，不能让兜底逻辑错抓 Extinguisher01
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
                root.transform.position = objName.EndsWith("3")
                    ? new Vector3(3.70f, 0.5f, 1.60f)
                    : objName.EndsWith("2")
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
            grab.heldLocalPosition = new Vector3(0f, -0.14f, 0.10f);   // 瓶身略微下沉，灭火时别把火焰和血量条糊住
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
            ext.blockedClip = Resources.Load<AudioClip>("Sounds/SFX/quiz_wrong");   // 未拔销就喷射的提示音
            ext.leftActivateAction = GetOrCreateRef("XRI Left Interaction", "Activate");
            ext.rightActivateAction = GetOrCreateRef("XRI Right Interaction", "Activate");
            ext.quizRequired = true;
            ext.sprayRange = 3f;
            ext.sprayAngle = 20f;
            ext.extinguishRate = 1f;                 // 火焰 extinguishThreshold=2.5 → 连续喷射约 2.5 秒扑灭

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
            // 三台灭火器各 5 题，共 30 道（六组）且互不重复，按「情景」绑定题库组：
            // Extinguisher01 → C 组（2）电气设备起火·先断电（同时兼任操作演练的取瓶，运行时切 A 组）
            // Extinguisher02 → E 组（4）纸张 / 固体可燃物起火
            // Extinguisher03 → F 组（5）茶水间小家电 / 油类起火（第 2 阶段再切 D 组疏散）
            quiz.questionSetIndex = root.name.Contains("03") ? 5 : (root.name.Contains("02") ? 4 : 2);
            quiz.ResetToDefaultQuestions();                 // 只刷本组 5 题，A/B/C 三选项
            quiz.timePerQuestion = 10f;                     // 每题 10 秒
            quiz.passRatio = 0.6f;                          // 答对 60%（3/5）才可通过
            quiz.distanceFromCamera = 1.15f;                // 一臂外阅读距离：字够大、按钮不挡手
            quiz.verticalOffset = -0.05f;                   // 略低于视线，不用仰头
            quiz.followCamera = true;                       // 玩家站起走动后自动回到正前方
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

            // 压把（按下压把 → 喷射）+ 粒子喷射效果
            SetupHandleAndSpray(root, ext, nozzle, sfx, leftHand, rightHand);

            // 第 28 轮：灭火剂类型（CO₂ / ABC 干粉 / 水基）+ 瓶身上方的类型标签
            SetupExtinguisherKind(root, ext);

            Debug.Log("[Setup] ✔ 灭火器：" + objName + "（抓取/问答/拔销/压把/粒子喷射/手型匹配已装配）");
        }

        /// <summary>
        /// 第 28 轮：给这一瓶定灭火剂类型，并在瓶身上方挂一块类型标签。
        /// 01 = 二氧化碳（情景一电气）、02 = ABC 干粉（情景二纸张）、03 = 水基/湿化学（情景三小家电油火）。
        /// </summary>
        static void SetupExtinguisherKind(GameObject root, Extinguisher ext)
        {
            ExtinguisherKind kind = root.name.Contains("03")
                ? ExtinguisherKind.WetChemical
                : root.name.Contains("02") ? ExtinguisherKind.PowderABC : ExtinguisherKind.CO2;

            ext.kind = kind;
            ext.sprayLoopClip = null;          // 运行期按类型合成喷射音（三瓶声音不同）

            // 喷射组件也同步写好类型与整套参数，存档即一致（运行期 Awake 还会再 ApplyKind 一次）
            var spray = root.GetComponentInChildren<ExtinguisherSpray>(true);
            if (spray != null)
            {
                spray.kind = kind;
                var p = ExtinguisherKindInfo.Profile(kind);
                spray.rate = p.rate;
                spray.startSpeed = p.startSpeed;
                spray.coneAngle = p.coneAngle;
                spray.lifetime = p.lifetime;
                spray.startSize = p.startSize;
                spray.sizeGrowth = p.sizeGrowth;
                spray.gravity = p.gravity;
                spray.noiseStrength = p.noiseStrength;
                spray.sprayColor = ExtinguisherKindInfo.SprayColor(kind);
            }

            // 标签挂在瓶身上方：按渲染包围盒高度定位，避免不同 prefab 尺寸下悬空或穿模
            float top = 0.5f;
            bool has = false;
            Bounds b = new Bounds(root.transform.position, Vector3.zero);
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            if (has) top = Mathf.Max(0f, b.max.y - root.transform.position.y);

            var tag = root.GetComponent<ExtinguisherKindTag>() ?? root.AddComponent<ExtinguisherKindTag>();
            tag.kind = kind;
            tag.localOffset = new Vector3(0f, top + 0.20f, 0f);
            tag.sizeMeters = new Vector2(0.72f, 0.30f);
            tag.fontSize = 48;
            tag.pushFromWall = 0.10f;
            tag.hideWhenHeld = true;

            Debug.Log("[Setup] ✔ 灭火剂类型：" + root.name + " → " + ExtinguisherKindInfo.Name(kind)
                      + "（适用 " + ExtinguisherKindInfo.SuitableFor(kind) + "），标签挂在瓶身上方 "
                      + tag.localOffset.y.ToString("F2") + " 米处");
        }

        /// <summary>装配压把（HandleLever）与喷射粒子（ExtinguisherSpray）。</summary>
        static void SetupHandleAndSpray(GameObject root, Extinguisher ext, Transform nozzle,
                                        AudioSource sfx, Transform leftHand, Transform rightHand)
        {
            // ---------- 压把 ----------
            var pivotT = root.transform.Find("HandlePivot");
            if (pivotT == null)
            {
                var pivot = new GameObject("HandlePivot");
                pivot.transform.SetParent(root.transform, false);
                pivot.transform.localPosition = new Vector3(0f, 0.33f, 0.07f);
                pivotT = pivot.transform;
            }

            var leverT = pivotT.Find("Handle");
            if (leverT == null)
            {
                var lever = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lever.name = "Handle";
                lever.transform.SetParent(pivotT, false);
                lever.transform.localPosition = new Vector3(0f, 0f, 0.08f);
                lever.transform.localScale = new Vector3(0.09f, 0.03f, 0.18f);
                UnityEngine.Object.DestroyImmediate(lever.GetComponent<Collider>());

                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                if (mat != null)
                {
                    mat.color = new Color(0.78f, 0.10f, 0.10f);
                    lever.GetComponent<Renderer>().sharedMaterial = mat;
                }
                leverT = lever.transform;
            }

            var handle = root.GetComponent<HandleLever>() ?? root.AddComponent<HandleLever>();
            handle.owner = ext;
            handle.lever = leverT;
            handle.handPoints = new[] { leftHand, rightHand };
            handle.touchRadius = 0.09f;          // 另一只手按到压把即触发
            handle.allowTrigger = true;          // 也可用扳机代替按压
            handle.pressAngle = 22f;
            handle.smoothSpeed = 12f;
            handle.audioSource = sfx;
            handle.pressClip = Resources.Load<AudioClip>("Sounds/SFX/ui_click");
            handle.blockedClip = Resources.Load<AudioClip>("Sounds/SFX/quiz_wrong");
            handle.leftActivateAction = GetOrCreateRef("XRI Left Interaction", "Activate");
            handle.rightActivateAction = GetOrCreateRef("XRI Right Interaction", "Activate");
            ext.handleLever = handle;

            // ---------- 喷射粒子 ----------
            var sprayT = nozzle.Find("SprayVFX");
            if (sprayT == null)
            {
                var vfx = new GameObject("SprayVFX");
                vfx.transform.SetParent(nozzle, false);
                vfx.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);   // 略微朝下喷
                sprayT = vfx.transform;
            }

            var ps = sprayT.GetComponent<ParticleSystem>();
            if (ps == null) ps = sprayT.gameObject.AddComponent<ParticleSystem>();
            var psMain = ps.main;
            psMain.playOnAwake = false;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var spray = sprayT.GetComponent<ExtinguisherSpray>() ?? sprayT.gameObject.AddComponent<ExtinguisherSpray>();
            spray.owner = ext;
            spray.nozzle = nozzle;
            spray.rate = 280f;
            spray.startSpeed = 3.4f;
            spray.coneAngle = 9f;
            spray.lifetime = 1.2f;
            spray.startSize = 0.05f;
            spray.sizeGrowth = 5.5f;
            spray.gravity = 0.45f;
            spray.noiseStrength = 0.45f;
            spray.spinUpTime = 0.12f;
            spray.sprayColor = new Color(0.94f, 0.96f, 0.98f, 0.75f);

            ext.sprayParticles = ps;

            // 喷射粒子的材质在运行期由 ExtinguisherSpray 自己生成（贴图是运行时画的，不能存进场景，
            // 存进去会变成丢失引用 → 洋红）。这里只确保渲染器存在且材质字段会在 Play 时补上。

            Debug.Log("[Setup] ✔ 压把 + 粒子喷射：" + root.name + "（HandleLever · ExtinguisherSpray）");
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
            ext.pin = pin;                    // 关键：反填到灭火器，鼠标拔销（PullByInput）依赖该引用
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
        // 8. 桌面鼠标调试输入
        // ================================================================

        static void SetupDesktopInput(XROrigin rig)
        {
            var go = GameObject.Find("DesktopInput") ?? new GameObject("DesktopInput");
            var input = go.GetComponent<DesktopDrillInput>() ?? go.AddComponent<DesktopDrillInput>();

            var cam = rig.Camera != null ? rig.Camera.transform : Camera.main != null ? Camera.main.transform : null;
            input.cameraTransform = cam;
            input.doubleClickWindow = 0.25f;
            input.handOffset = new Vector3(0.33f, -0.24f, 0.7f);   // 0.7 米：既不糊镜头又能看清瓶身
            input.handEuler = new Vector3(0f, -12f, 0f);
            input.mouseLookSensitivity = 0.12f;                      // 鼠标转头灵敏度
            input.pitchClamp = 75f;                                 // 俯仰限幅，防翻转

            // 桌面手部锚点：挂在相机下，随视角移动
            Transform anchor = null;
            if (cam != null)
            {
                var existing = cam.Find("DesktopHand");
                if (existing == null)
                {
                    var h = new GameObject("DesktopHand");
                    h.transform.SetParent(cam, false);
                    h.transform.localPosition = input.handOffset;
                    h.transform.localRotation = Quaternion.Euler(input.handEuler);
                    existing = h.transform;
                }
                anchor = existing;
            }
            input.handAnchor = anchor;

            // 主灭火器：优先按名字取 Extinguisher01，保证每次装配结果一致
            Extinguisher primary = null;
            var all = UnityEngine.Object.FindObjectsByType<Extinguisher>(FindObjectsSortMode.None);
            foreach (var e in all)
                if (e.gameObject.name == "Extinguisher01") { primary = e; break; }
            if (primary == null && all.Length > 0) primary = all[0];

            // 两台都要能走桌面喷射（避免玩家拿起另一台时压把自动喷射/右键失效）
            foreach (var e in all)
            {
                e.desktopMode = true;
                if (e.pin == null) e.pin = e.GetComponentInChildren<SafetyPin>(true);
            }

            input.extinguisher = primary;
            input.extinguishers = all;
            if (primary != null) input.quizPanel = primary.quizPanel;

            input.startScreen = UnityEngine.Object.FindObjectOfType<StartScreen>();
            input.stagePanel = UnityEngine.Object.FindObjectOfType<StageOneCompletePanel>();

            Debug.Log("[Setup] ✔ 桌面鼠标输入：右键单击灭火器答题/拿起 / 左键单击拔销 / 左键双击压把 / 右键双击喷射 / P 放下"
                      + "（主灭火器：" + (primary != null ? primary.gameObject.name : "未找到") + "）");
        }

        // ================================================================
        // 9. 头显输入层（Trigger 拔销/压把 · A/B/X 问答 · 摇杆按下蹲起 · VR 提示条）
        // ================================================================

        static void SetupVrInput(XROrigin rig, Transform cam)
        {
            var host = rig.Origin != null ? rig.Origin : rig.gameObject;
            var vr = host.GetComponent<VRDrillInput>();
            if (vr == null) vr = host.AddComponent<VRDrillInput>();

            var all = UnityEngine.Object.FindObjectsByType<Extinguisher>(FindObjectsSortMode.None);
            vr.extinguishers = all;
            vr.startScreen = UnityEngine.Object.FindObjectOfType<StartScreen>();
            vr.onboarding = UnityEngine.Object.FindObjectOfType<OnboardingScreen>();
            vr.stagePanel = UnityEngine.Object.FindObjectOfType<StageOneCompletePanel>();
            vr.stageTwo = UnityEngine.Object.FindObjectOfType<StageTwoFlow>();
            vr.leftHand = GameObject.Find(LeftHandName) != null ? GameObject.Find(LeftHandName).transform : null;
            vr.rightHand = GameObject.Find(RightHandName) != null ? GameObject.Find(RightHandName).transform : null;
            vr.rightTriggerAction = GetOrCreateRef("XRI Right Interaction", "Activate");
            vr.leftTriggerAction = GetOrCreateRef("XRI Left Interaction", "Activate");

            var hudGo = GameObject.Find("VRDrillHud") ?? new GameObject("VRDrillHud");
            hudGo.transform.SetParent(host.transform, false);
            var hud = hudGo.GetComponent<VRDrillHud>();
            if (hud == null) hud = hudGo.AddComponent<VRDrillHud>();
            hud.head = cam;
            hud.distance = 0.9f;               // 与玩家视角判图后调好的场景值保持一致
            hud.verticalOffset = -0.4f;
            hud.canvasScale = 0.00038f;
            hud.holdSeconds = 5f;
            hud.idleAlpha = 0.55f;
            vr.hud = hud;

            // 触觉反馈：抓取/拔销/压把/答题各一次短脉冲
            vr.hapticGrab = 0.22f;
            vr.hapticPin = 0.75f;
            vr.hapticPress = 0.42f;
            vr.hapticAnswer = 0.30f;
            vr.hapticDuration = 0.07f;

            // 场景里默认关掉：桌面模式不看这块面板，头显接通后由 VRDrillInput 打开
            hudGo.SetActive(true);

            Debug.Log("[Setup] ✔ 头显输入层：VRDrillInput（Trigger 拔销/压把/喷射 · A 开始/翻页 · A/B/X 选答 · 左摇杆按下蹲起）"
                      + " + VRDrillHud（视线下方 0.22 米的世界空间提示条）");
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
