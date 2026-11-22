# fire-drill-VR

办公室消防知识演练 VR 项目（Unity）

## 项目简介

基于 Unity（6000.6.0f1）+ XR Interaction Toolkit（3.6.1）开发的办公室消防知识 VR 演练项目。
玩家在办公室场景中完成「警报响起 → 寻找灭火器 → 常识考核 → 拔销 → 压把 → 喷射灭火 → 沿箭头撤离到大厅」的完整流程，
结束后弹出成绩单。同一份工程支持 **VR 双手柄** 与 **桌面鼠标键盘** 两条完整输入链。

## 玩法流程

1. **开始界面**：世界空间面板锚定在右手手柄正前方 1.0 米、上抬 0.15 米；三个入口：`开始演练` / `操作教学`（分页引导）/ `操作演练`（原第一阶段的完整实操，只练流程不计成绩）。
2. **操作教学（引导页）**：4 页（演练目标 / 桌面操作 / VR 手柄操作 / 安全考核规则），末页按钮变为「开始演练」。
3. **操作演练（第 23 轮起）**：在办公室中央点燃一团地板火，跑完整流程（5 题问答 → 抓取 → 拔销 → 压把 → 喷射灭火），结束后弹「操作演练完成 / 进入正式演练」，不计入成绩。
4. **操作演练（原第一阶段改造）**：见上，目标是让玩家先弄懂整条流程。
5. **正式第 1 阶段（三套情景，第 24 轮起）**：点「开始演练」后按所选情景随机落到该情景的 3 个出生点之一，**终点统一是安全出口门**，只有初始点不同。
   - **情景 1 · 电气设备起火（配电柜）**：设备柜顶起火（`DeviceFire`）+ 常驻烟雾，旁边墙上挂电源总闸，就近用 `Extinguisher01`（C 组·电气火灾题库 2 号）；
   - **情景 2 · 办公桌纸张起火**：南侧工位桌面纸堆起火（`PaperFire` @ 1.42 / -3.45）+ `Stage1Smoke_Paper` 薄烟，**无需断电**，就近用 `Extinguisher02`（E 组·A 类固体题库 4 号）；
   - **情景 3 · 茶水间小家电起火**：茶水台台面小家电起火（`PantryFire` @ 6.30 / -0.74）+ `Stage1Smoke_Pantry` 油烟，**无需断电**，就近用 `Extinguisher03`（F 组·油类/小家电题库 5 号）；
   - 开始界面第二排三个按钮直接选情景；拿灭火器答对 5 题后拔销；需要断电的情景在**未断电前喷射无效**（火焰锁定，HUD 提示「火焰仍带电，喷射无效」）；
   - 右键单击 / 扳机射线点电源总闸 → 闸柄扳下、指示灯转绿 → 火焰解锁 → 喷射灭火；火焰上方常驻 **血量条**（HP 100/100 → 0）。
6. **第 2 阶段（撤离 + 出口火情）**：灭火后烟雾停止、地面出现「安全出口 →」标识与引导箭头；走到大厅检查点后出口门前复燃（D 组疏散题库），拿出口旁的灭火器 → 5 题 → 拔销 → 压把 → 喷射；火灭后门解锁。
   **第 25 轮起**：纸张 / 茶水间两套情景按「就近原则」**跳过第 2 阶段**，第 1 阶段火一灭就直接在安全出口门前升起第三阶段的火（`skipEvacuation`），只有电气情景保留完整两段式撤离。
7. **走出门外才算通关（第 25 轮）**：门解锁后右键 / 扳机点门 → 门扇转开；必须真的跨出门洞 0.9 米（`ExitDoor.beyondDepth`）才判定撤离成功，站在门内不算。
8. **门外开阔场地**：安全出口门外是三面矮墙围合的 6.5 × 8 米广场（集合圈 + 集合点立牌 + 庭院灯 + 花箱），走廊东端墙拆出一个 3.8 米宽开口通向广场。
9. **结束页**：成功或失败都会弹出成绩单，显示总用时/剩余时间、答对题数、火源处理数、完成步骤，可「重新演练」或「关闭」。


## 第 24 轮：封住天空盒缺口与三情景演练

### 补墙（`FireSafetySetup.SetupWalls()`）

实测场景围合后发现地板是 L 形（`z∈[-4,4]` 全宽 `x∈[-6.75,7.25]`；`z∈[-8,-4]` 只有西翼 `x∈[-6.75,-0.75]`），四处边界直接漏到天空盒。新建父物体 `OfficeWalls`，用 4 段 3 米高 Cube（含 `BoxCollider`，材质 `OfficeWallMat` 浅米色，踢脚线 `OfficeWallSkirtingMat`）封住：

| 墙段 | 位置 | 尺寸 | 封住的缺口 |
|---|---|---|---|
| `Wall_SouthWing` | (-3.75, 1.50, -8.10) | 6.00 × 3.00 × 0.20 | 西翼南端 z=-8 |
| `Wall_WingEast` | (-0.85, 1.50, -6.00) | 0.20 × 3.00 × 4.00 | x=-0.75 的 z∈[-8,-4] |
| `Wall_LobbySouth` | (3.15, 1.50, -4.10) | 8.20 × 3.00 × 0.20 | 大厅南边界 z=-4 |
| `Wall_CorridorEast` | (7.15, 1.50, 0.00) | 0.20 × 3.00 × 8.00 | 东端 x=7.25 |

### 灭火器等距布点

三台灭火器由原先的 3.2 m / 6.9 m 不均匀间距改为沿「大厅 → 中部房间 → 茶水台」铺开，固定题库组各自独立：

| 瓶 | 位置 | 题库组 |
|---|---|---|
| `Extinguisher01` | (-3.60, 0.16, 0.60) | C（2）· 电气 |
| `Extinguisher02` | (1.60, 0.16, -1.60) | E（4）· A 类固体（纸张） |
| `Extinguisher03` | (6.00, 0.16, -3.20) | F（5）· 油类/小家电 |

实测间距 `01↔02 = 5.65 m`、`02↔03 = 4.68 m`（相邻两段比值 ≤ 1.4，自检断言）。第 2 阶段仍按原设计把该瓶题库切到 D 组（疏散），并在出口旁自动补一台备用瓶（`autoMoveSpareExtinguisher` + `spareExtinguisherSpot = (3.70, 0, 1.60)`）。

### 多情景骨架（`DrillScenario.FireScenario`）

```
scenarios[3] = { 电气（DeviceFire / 需断电 / 题库 2 / 出生组 1）
                 纸张（PaperFire  / 无需断电 / 题库 4 / 出生组 2）
                 茶水间（PantryFire / 无需断电 / 题库 5 / 出生组 3） }
```

- `StartFormal(forceSpawn, forceScenario)`：先 `ApplyScenario()` 收掉其它情景的火与烟、切该瓶题库、设置是否需要断电，再按 `DrillSpawnPoints/SpawnGroupNN` 随机取出生点，用 `Quaternion.LookRotation(lookAt - pos)` 让玩家一出生就正对火源；
- 出生点每组 3 个候选（`Spawn01..03`），**终点统一为 `ExitDoor`**，第 2 阶段流程与之前完全一致；
- `BeginTutorial()` 会先把全部情景火收掉、切回 A 组题库，保证「操作演练 / 操作教学」不受情景残留影响。

### 题库扩到 30 题（六组）

`ExtinguisherQuizPanel` 由 20 题四组扩到 **30 题六组**：A 通用 / B 厨房 / C 电气 / D 疏散 / **E 纸张·A 类固体** / **F 小家电·油类**，`questionSetIndex` 的 `[Range(0,5)]` 同步放宽，`StageTwoFlow.spareQuizSetIndex` 同样放宽。

### 玩家视角验收（第 24 轮）

真 Play + 离屏渲染逐张判读（不是编辑模式断言）：三套情景出生/起火/答题/喷射中段共 9 张、补墙 4 张、主流程 20 张，全部 `FAIL_COUNT=0`。其中两处**玩家视角才暴露的真实缺陷**已修：

1. 情景二起火点原在打印机包围盒内部，玩家只看得到一张浮在机器上方的血量条 → 移到打印机右侧空桌面 (1.42, 1.06, -3.45)，烟雾终点同步；
2. 情景三取瓶区（东端 x≈6、z≈-3.2）无灯，画面近乎全黑 → 属已知照明缺口，取瓶路径上靠火光照明确认可见（记录待优化）。

## 第 25 轮：可打开的安全出口门 + 就近原则跳阶段 + 门外广场 + 灭火器贴墙

### 5.1 安全出口门「可打开」，并且必须真正走出门外

用户截图里那扇浅棕木门（`door_b_variant` @ 5.25 / 1.02，门框 `wall_standard_door_frame (1)`）由「装饰门」改成**可交互的终点门**：

| 新增（`ExitDoor.cs`） | 作用 |
|---|---|
| `requireBeyond` / `beyondDepth = 0.9` / `beyondLateral = 1.6` | 通关判据从「靠近门 0.9 米」改为「越过门洞 0.9 米、横向不超出 1.6 米」 |
| `IsOpen` / `TryOpenByPlayer(maxDistance)` / `IsPassable` | 门扇可被玩家转开；`IsPassable = 已解锁 && 门扇已开` |
| `IsDoorPart(Collider)` | 射线命中判定：桌面右键、VR 扳机射线都能认出「这是那扇门」 |
| `DetectPlayerEnter()` 用本地 `forward` 求 `along` | 门内 `along = 0`、门外为负；`-along ≥ 0.9` 才置 `PlayerEntered` 并触发 `onEntered` |

交互：

- **桌面**：右键点门（6 米内）→ 未解锁提示「这扇安全出口还锁着：先把门前的火灭掉」；已解锁则门扇缓缓转开并提示「直接走出这扇门即算撤离成功」；
- **VR**：未持灭火器时右手射线扳机指向门扇，同样两档提示；
- 探针实测：站在门内侧 0.9 米时 `PlayerEntered=false`（`vr6b_door_threshold.png`），走到门外 1.35 米才结算（`vr6c_outside_plaza.png`）。

### 5.2 纸张 / 茶水间第 1 阶段灭火后跳过第 2 阶段（就近原则）

- `StageTwoFlow` 新增 `skipEvacuation`；`Begin()` 里若为真，则 `arrows.BeginExitGuidance()`（箭头直指安全出口门）→ 提示「本情景就近撤离：直接前往安全出口门，门前已复燃」→ 立即 `EnterStageThree()`，**不再要求走进大厅**；
- `DrillScenario.FireScenario.skipEvacuation` 随情景下发，`ApplyScenario()` 同步到流程脚本；
- 情景二（纸张）/ 情景三（茶水间）= `true`，情景一（电气）保持 `false`（电气仍需断电 → 撤离 → 出口复燃的完整两段式）。

### 5.3 门外改为「开阔场所」（`FireSafetySetup.SetupOutdoorPlaza()`）

原 `Wall_CorridorEast`（x=7.25、z∈[-4,4] 整面封死）把门外封成 2 × 3.8 米死角。现在：

- 东端墙只封茶水间东侧 `z∈[-4.05,0.15]`（`(7.15, 1.50, -1.95)`, `0.20 × 3.00 × 4.10`），走廊东端 `z 0.1~3.9` 留出开口；
- 门外新建父物体 `OutdoorPlaza`：`PlazaGround`（6.50 × 8.00，顶面 y=0，x 7.25~13.75 / z -4~4）、三面 1.1 米矮墙 `PlazaFence_South/North/East`（西侧敞开接走廊开口）、黄色集合圈 `PlazaAssembleRing`、绿色集合点立牌 `PlazaAssembleSign`、2 盏庭院灯（`PointLight` intensity 1.6 / range 7.0）、2 个花箱。

### 5.4 灭火器一律贴墙 / 挂墙（不占走道）

| 瓶 | 贴墙位置 | 墙面 | 到本情景起火点 | 题库组 |
|---|---|---|---|---|
| `Extinguisher01` | (0.85, 0.16, 3.45) | 北墙 | 3.31 m（配电柜） | C（2）· 电气 |
| `Extinguisher02` | (3.45, 0.16, -3.55) | 大厅南墙 | 2.04 m（纸张火） | E（4）· A 类固体 |
| `Extinguisher03` | (5.95, 0.16, -3.80) | 茶水间南墙 | 3.08 m（小家电火） | F（5）· 油类/小家电 |

每台后面新增一块墙面挂钩板（父物体 `ExtinguisherBrackets`，`<Name>_Bracket` 背板 + `<Name>_Bracket_Hook` 挂钩横杆，深红 `ExtinguisherBracketMat`，带轻微自发光 emission 0.26/0.02/0.02，昏暗的南墙与茶水间也能一眼看到挂板位置），看起来就是「挂在墙上」；三处机位取证见 `vr17_ext_mount_01.png` / `vr18_ext_mount_02.png` / `vr19_ext_mount_03.png`。第三阶段备用瓶落点也从门前的过道挪到贴墙位 `(4.95, 0, 3.20)`。

自检断言随之改写：三台各自 `Physics.Raycast` 四方向 0.6 米内必须有立面（贴墙）、到本情景起火点 ≤3.6 米（就近）、两两间距 ≥2.0 米（不重叠）。

## 第 26 轮：情景二/三右键拾取与问答面板修复

### 6.1 缺陷①：从开始页点「情景 2 / 情景 3」后，右键 / 左键 / VR A 键全部失效

- **现象**：情景一（电气）正常，但情景二（纸张）、情景三（茶水间）里右击灭火器拿不起、答题面板不弹出，HUD 只回一句「请先开始演练」。
- **根因**：`StartScreen.StartScenario(int)` 只调用 `scenario.StartFormal(-1, idx)` + `ExitRoutine()`，**从不把 `Started` 置真**。情景一走主按钮 `StartGame()`（内部会置 `Started`）所以正常；从开始页的情景按钮进入的那两条路径，`Started` 始终是 `false`。
- **影响面**：`DesktopDrillInput.OnRightClick()` 里的「没开始演练就不理鼠标」门禁共 6 处 —— `DesktopDrillInput.cs` 4 处、`VRDrillInput.cs` 2 处，导致桌面右键、桌面左键（拔销/压把）、头显 A 键在这些情景里被整段拦掉。
- **修复**：
  - `StartScreen.StartScenario(int)` 补 `Started = true;` 与 `FireDrillManager.Instance?.ForceCompleteUpTo(DrillStep.AlarmRinging);`（与 `StartGame()` 一致，保证阶段推进到「警报响」而不是停在待机）；
  - 新增 `public bool DrillStarted => Started || (DrillScenario.CurrentMode == Mode.Formal);`，6 处门禁统一改读 `DrillStarted`，避免以后再加入口时又漏一处。

### 6.2 缺陷②：问答面板挂在灭火器根节点下，瓶一被拿起面板就翻转/镜像

- **现象**（玩家视角截图 `vr20_deskgrab_sc3_held.png` 修复前）：答完题灭器被拿在手里时，面板文字左右镜像（看到的是画布背面），面板下半部还被瓶体压住。
- **根因**：`ExtinguisherQuizPanel.BuildUI()` 里 `go.transform.SetParent(transform, false)` —— 世界空间画布是**灭火器根节点的子物体**。瓶子被拿上手后继承手部姿态（旋转/位移），画布随之一起转，`KeepInFrontOfCamera()` 只在「相机位移 >0.5 米或朝向点积 <0.8」时才重放，管不到这种「相机没动、道具转了」的情况。
- **修复**：画布改为 `SetParent(null, false)` 独立世界空间；新增 `OnDestroy()` 在组件销毁时一并回收画布（播放态 `Destroy`、编辑态 `DestroyImmediate`），不留孤儿 UI。

### 6.3 新增回归探针 `Assets/Editor/DesktopGrabProbe.cs`

批处理入口：`RunFromBatch`（情景二）/ `RunSc2` / `RunSc3`。断言链：

| 断言 | 情景二 | 情景三 |
|---|---|---|
| 点情景按钮后 `StartScreen.Started` / `DrillStarted` 为真 | ✔ | ✔ |
| 视线中心射线首个命中 = 本情景目标瓶 | Ext02 | Ext03 |
| 右键后问答面板 `IsOpen`，题库组匹配 | 4（期望 4） | 5（期望 5） |
| 面板在正前方（1.15 米）且落在屏幕内 | ✔ | ✔ |
| 答满 5 题（3 对 2 错）→ `Passed=True` | ✔ | ✔ |
| 通过后 `grab.IsHeld=True`、`QuizPassed=True`、HUD「已拿起」 | ✔ | ✔ |
| 结论行 | `FAIL_COUNT=0 PROBE_EXIT=0` | `FAIL_COUNT=0 PROBE_EXIT=0` |

截图：`Logs/shots/vr20_deskgrab_sc2|sc3_before/quiz/held.png`；结论：`Logs/shots/_deskgrab.txt`。

> 批处理坑（写进探针注释，避免下次重踩）：`-batchmode` 下系统鼠标每帧被真机状态覆盖，`QueueStateEvent` / `InputState.Change` / 自建 `Mouse` + `MakeCurrent()` 都留不到 `LateUpdate` 那一帧；`Keyboard.current` 初始为 `null` 会让 `LateUpdate` 整段早退。因此探针改为反射调用 `OnRightClick()`（与真实右键按下的同一入口），并保留「点情景按钮后 `DrillStarted` 必须为真」这条针对真实 bug 的断言。


## 第 27 轮：情景二/三「房间门」可打开（灭完第一阶段火才能开）

### 7.1 需求与勘察：这扇门是南侧唯一的出口通路

用户点名的是截图右侧那扇**没有火焰的棕色玻璃窗房间门**（场景里是 `Floor 0/door_b`，root `(2.75, 0, 0)`，门扇 MeshCollider `0.97 × 2.02 × 0.09`，把手 `Handle1/Handle2` 无碰撞体）。为了确认它不是「可有可无的装饰门」，本轮先用一次性编辑器脚本 `DoorSurvey` 做跨 `z=0` 隔墙的水平射线扫描（两条线 `y=1.0` / `y=1.5`，`x` 从 −1.0 每 0.2 m 到 7.3），结果见 `Logs/doorsurvey6.log`：整段只有 `x≈−1.0` 一格外侧可穿，`x≈−0.8` 到 `7.3` **全被挡**。也就是说门关着时，南侧没有任何其他通路。

| 情景区位 | 出生点 | 与 `z=0` 隔墙关系 | 灭完火能不能走到出口 |
|---|---|---|---|
| 情景一（电气 · 配电柜） | (2.90, 1.00, 4.30) | 北侧，不跨墙 | 能（走 `Lobby` → `ExitDoor`） |
| 情景二（纸张 · 办公桌） | (3.30, 4.20, 2.60) | 南侧 | **不能**，被 `z=0` 隔墙挡住 |
| 情景三（茶水间 · 小家电） | (5.00, 4.60, 3.60) | 南侧 | **不能**，同上 |

`Lobby` 检查点 `(3.35, 0.02, 1.02)` 与 `ExitDoor (5.25, 0, 1.02)` 都在北侧，所以「只点名情景二、三」并不是口味问题 —— 这两条路线本来就必须穿这扇门。这也解释了怎么会出现「火灭了却无路可走」的死局。

### 7.2 实现：新增 `RoomDoor.cs`（门禁 + 转身动画 + 走近提示）

| 参数 | 值 | 说明 |
|---|---|---|
| `openAngle` | `+95°` | 朝**北侧走廊（疏散方向）**开，符合「疏散门朝逃生方向开」；同时不会把南侧走来的玩家顶住 |
| `openDuration` | `0.8 s` | smoothstep 缓入缓出，不是瞬移开门 |
| `maxInteractDistance` | `4.0 m` | 超出距离不响应，避免隔墙点门 |
| `requireStageOneCleared` | `true` | 第一阶段火未灭 → 只提示不开门 |

- **门禁判定** `CanOpenNow`：没有 `DrillScenario` / 非正式演练 / `FireScenario.skipEvacuation == false`（情景一）一律放行；否则要求 `Stage1Cleared || Phase == Stage2 || Phase == Done`。
- **拒绝提示**：`这扇门还打不开：先把第一阶段的火灭掉`，走 `DesktopDrillInput.PushHint` 进 HUD 状态行。
- **送达提示**：`房间门已打开 —— 穿过这扇门去安全出口`。
- **走近一次性提示**：3.2 m 内朝向门时提示一次「门可以开了，右键单击门」，避免玩家在远处点门。
- **桌面输入**：`DesktopDrillInput.OnRightClick()` 在 `TryOpenExitDoorUnderMouse()` 之后插入 `TryOpenRoomDoorUnderMouse()` —— `ScreenPointToRay(Mouse.current)` + `RaycastAll` 按距离排序 + `RoomDoor.IsDoorPart()` 判门件 + 6 m 距离上限。
- **头显输入**：`VRDrillInput` 扳机分支新增 `TryOpenRoomDoorByRay()`（`rightHand ?? Camera.main` 6 m 射线）。
- **装配**：`FireSafetySetup.RunSetup()` 在 `SetupStageTwoFlow()` 之后调用新的 `SetupRoomDoor()`（`public static`，`AddComponent<RoomDoor>` 并写死 95° / 0.8 s / 4.0 m / 要求一阶段）。
- **重开演练复位**：`DrillScenario.StartFormal()` 里 `RoomDoor.Instance.ResetDoor()`，把上一次演练开过的门关回去。

> 场景里序列化的 `openAngle` 会覆盖代码默认值：本轮先设成 −95°（往房间内开，正对走来的玩家），改代码后必须重跑装配把场景值刷成 +95°，否则 `Play` 里仍是老方向。`Logs/doorpatch.log` 记录 `[PATCH] door_b=(2.75, 0.00, 0.00) RoomDoor=OK angle=95.0 leaf=self colliders=2`。

### 7.3 玩家视角验收（`RoomDoorProbe.RunSc2 / RunSc3`）

探针以真实 `Play` + 玩家站位（`(2.30, 0, −1.75)`，眼高 1.60 m，到门 1.81 m）逐张出图，情景二/三各 5 张：

| 图 | 玩家看到什么 | 判读 |
|---|---|---|
| `vr27_roomdoor_sc2_0doorview.png` | 正对那扇棕色玻璃窗门，窗内可见里面的柜子/桌椅，左侧文件柜、右侧绿植 | 与用户截图的同构门，确认就是这个目标 |
| `_1closed.png` | 门关着，只能透过门洞看到北侧走廊的「安全出口 EXIT」牌 | 死路成立：门不开就过不去 |
| `_2locked.png` | 右键只出提示「这扇门还打不开：先把第一阶段的火灭掉」，门纹丝不动 | 门禁生效 |
| `_3opened.png` | 一阶段灭火后右键，门扇转开、门洞通透，HUD 出绿色「安全出口 →」 | 开门与引导同时到位 |
| `_4crossed.png` | 玩家已站到北侧，正对燃烧的出口门 + 上方「安全出口 EXIT」、左侧书架与带书椅子 | 与用户原截图构图一致，证明真走过门 |

断言链（`Logs/shots/_roomdoor.txt`，末两行 `FAIL_COUNT=0` / `PROBE_EXIT=0`，情景二、三各跑一遍）：

| 断言 | 情景二 | 情景三 |
|---|---|---|
| 一阶段未完成时 `CanOpenNow=False`，右键后 `IsOpen=False` | ✔ | ✔ |
| 拒绝提示文案出现在 HUD | ✔ | ✔ |
| `deviceFire.Extinguish()` 后 `Stage1Cleared=True` / `CanOpenNow=True` | ✔ | ✔ |
| 右键后门扇 `Y` 角 `0 → 95`（Δ=95） | ✔ | ✔ |
| 玩家从 `z=−2.00` 逐 0.1 m 前进 60 步到达 `z=1.05`（穿过门洞） | ✔ | ✔ |
| 方法探针 `TryOpenRoomDoorUnderMouse=True` 且 `RoomDoor.Instance` 与场景同一个 | ✔ | ✔ |

### 7.4 探针踩坑（写进探针注释）

`-batchmode` 下**无法注入鼠标坐标**：`InputState.Change`、`QueueStateEvent`、`WarpCursorPosition`、`MakeCurrent` 全部试过，`Mouse.current.position.ReadValue()` 恒为 `(0,0)`（第 26 轮曾经成功过的那一套本轮不可复现，屏幕分辨率还会在 2560×1440 / 2007×1174 之间跳）。最终改用「反相」方案：按相机视锥左下角方向 `vLocal = normalize(−hx, −hy, 1)` 反解旋转 `R = LookRotation(dir) × Inverse(LookRotation(vLocal))`，让**屏幕 (0,0) 那条射线正好指向门中心** —— 等价于玩家把鼠标停在画面左下角点门，右键链路照样被真实验证。

另两处编译坑：`TryOpenByPlayer(float maxDistance = -1f, out string hint)` 触发 `CS1737`（可选参数必须在必需参数之后）→ 改签名并同步两处调用；`DrillScenario.Phase` 没有 `StageThree`（实际是 `Idle/Tutorial/Stage1/Stage2/Done`）→ 判定改 `Phase.Stage2 || Phase.Done`。少一个 `)` 造成的 `CS1026` 会让 Unity 保留旧程序集，探针读到的是上一次结论，日志里没有任何 `DBG` 输出就是该症状。

### 7.5 参考调研（外部公开资料）

| 来源 | 采用的点 | 落到本项目哪里 |
|---|---|---|
| 抖音「VR 还原办公室火灾」（`iesdouyin.com/share/video/7076468724064210183`） | 顺绿色「安全出口」标志撤离、浓烟弯腰、通道火用灭火器清障 | 门开后 HUD 直接给「安全出口 →」；`C` 蹲下穿烟；阶段二「灭掉门前火」 |
| 顺企网 VR 办公室火灾逃生系统（`m.11467.com/product/d33904492.htm`） | 关电源 / 湿毛巾 / 试门温 / 按报警 / 不乘电梯等 7 条知识点；办公室 + 楼梯间 + 电梯三场景 | 断电门控、报警步骤已有；本轮补上「先试门再开门」的物理动线（门关着就是走不通） |
| Dagger Interactive《Building-Specific VR Emergency Training》（2025-11-10） | 按真实楼层复刻布局、烟雾渐进降低能见度、随机起火点避免记路线 | 三情景随机起火点/出生点、烟雾体积、按真实户型布门 |

### 7.6 改动文件清单（第 27 轮）

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/FireSafety/RoomDoor.cs` | **新增**：门禁、smoothstep 转身、走近提示、`ResetDoor()`、`IsDoorPart()` |
| `Assets/Scripts/FireSafety/DesktopDrillInput.cs` | 新增 `TryOpenRoomDoorUnderMouse()` 与右键分支；HUD 操作行补「右键单击门：开门」 |
| `Assets/Scripts/FireSafety/VRDrillInput.cs` | 新增 `TryOpenRoomDoorByRay()` 与扳机分支 |
| `Assets/Scripts/FireSafety/DrillScenario.cs` | `StartFormal()` 里复位房间门 |
| `Assets/Editor/FireSafetySetup.cs` | `RunSetup()` 增 `SetupRoomDoor()` + 新的 `SetupRoomDoor()` 方法 |
| `Assets/Editor/FireSafetySelfTest.cs` | 新增 7 条 `door_b` / `RoomDoor` 断言 |
| `Assets/Editor/RoomDoorProbe.cs` | **新增**：`RunSc2` / `RunSc3` 玩家视角探针 |
| `Assets/Scenes/FireSafetyOffice.unity` | `Floor 0/door_b` 挂上 `RoomDoor`（95° / 0.8 s / 4.0 m / 要求一阶段） |

### 7.7 自检与回归

- `FireSafetySelfTest.RunAll` → `Logs/selftest27.log`（9048 行），末尾 `[TEST] ==== 全部通过 ====`；新增 7 条 `door_b` 断言（存在 / 已挂 `RoomDoor` / 轴心用自身 / 开向 +95° / 0.8 秒 / 要求一阶段已灭 / 门扇带碰撞体）全 ✔。
- 第 26 轮拾取回归 `DesktopGrabProbe.RunSc2` → `Logs/deskgrab27.log` / `Logs/shots/_deskgrab.txt`，`FAIL_COUNT=0 PROBE_EXIT=0`（改 `DesktopDrillInput` 没有碰坏右键拾取与问答链路）。

## 第 28 轮：三情景「灭火剂对路」——粒子 / 音效 / 熄灭特效差异化 + 门外知识墙

### 8.1 需求与勘察：三种灭火器此前喷的是同一套东西

用户这轮提了三件事：①情景一、二、三要用**相适应的灭火剂**，粒子与音效也要跟着变；②出门的空地上写本次演练的安全知识；③以玩家视角测试后推仓库、出日志。

先查代码确认这不是「调个颜色」的小活：

| 现状 | 位置 | 后果 |
|---|---|---|
| 喷射参数全写死 `rate 280 / startSpeed 3.4 / cone 9° / lifetime 1.2 / startSize 0.05 / gravity 0.45`、颜色近白 | `ExtinguisherSpray.cs` | 三瓶喷出来一模一样 |
| `colorOverLifetime` 的 RGB 三键是近白色 | `ExtinguisherSpray.Configure()` | 会**盖掉** `sprayColor`，改基色没用 |
| 只有一支共用循环音 `Resources/Sounds/SFX/spray_loop.wav` | `Extinguisher.sprayLoopClip` | 三种灭火剂听起来一样 |
| `FireSource.Extinguish()` 只停粒子、灯归零、停火声 | `FireSource.cs` | 火灭瞬间没有任何收尾表现 |

### 8.2 实现：一套参数表 + 三支合成音 + 一次性收尾特效

#### 8.2.1 `ExtinguisherKind.cs`（新增）

`enum ExtinguisherKind { PowderABC, CO2, WetChemical }`，`ExtinguisherKindInfo` 提供 `Name()` / `SuitableFor()` / `BandColor()` / `SprayColor()` / `Profile()`，`struct SprayProfile` 九个字段。最终三套参数（经三轮「判图后加浓」定稿）：

| 参数 | CO₂（情景一·电气） | 水基/湿化学（情景三·小家电） | ABC 干粉（情景二·纸张） |
|---|---|---|---|
| `rate` | 600 | 480 | 520 |
| `startSpeed` | 5.6 m/s | 4.0 m/s | 3.2 m/s |
| `coneAngle` | 11° | 10° | 16° |
| `lifetime` | 0.90 s | 1.20 s | 1.60 s |
| `startSize` | 0.140 m | 0.130 m | 0.165 m |
| `sizeGrowth` | 3.2× | 4.2× | 7.0× |
| `gravity` | 0.06（几乎不下落） | 0.95（水滴明显下坠） | 0.50 |
| `noiseStrength` | 0.12 | 0.28 | 0.50 |
| `SprayColor` | 蓝白 `(0.90,0.95,1.00,α0.85)` | 淡蓝 `(0.82,0.93,1.00,α0.90)` | 暖白 `(0.97,0.95,0.89,α0.95)` |
| 瓶身色带 | 蓝 `(0.16,0.42,0.78)` | 淡蓝 `(0.34,0.68,0.86)` | 米黄 `(0.86,0.78,0.42)` |

三瓶与情景绑定：`Extinguisher01 → CO₂`（电气，需先断电）、`Extinguisher02 → ABC 干粉`（纸张）、`Extinguisher03 → 水基湿化学`（小家电/油火，简介里写明「不能直接泼水」）。

#### 8.2.2 `ExtinguisherSfx.cs`（新增）：三支运行时合成的循环音

44100 Hz 单声道、运行时合成，不依赖 wav 资源（避免仓库里塞三个大文件）：`SprayLoop(kind)` 生成 1.0 s 循环，`CrossfadeLoop` 做 40 ms 首尾交叉淡入避免爆音；另有 `Burst(kind)` 0.9~1.2 s 的收尾声。

| 类型 | 音色构成 | clip 名 |
|---|---|---|
| CO₂ | 一阶高通「嘶——」的窄带气流 | `SFX_Spray_CO2` / `SFX_Burst_CO2` |
| ABC 干粉 | 低通粉噪 + 110 Hz rumble + 4.5 Hz 摆动 | `SFX_Spray_Powder` / `SFX_Burst_Powder` |
| 水基 | 带通 + 8 Hz 抖动 + 90 Hz 水滴颗粒 | `SFX_Spray_Wet` / `SFX_Burst_Wet` |

合成用的伪随机数按 `(uint)cell * 2654435761u` 打散，同一格每次结果一致。

#### 8.2.3 `ExtinguishFx.cs`（新增）：火灭瞬间的一次性收尾

`Play(pos, kind)` 建临时 GameObject + ParticleSystem（Sphere r=0.18、burst 70 / 80 / 100），`sizeOverLife` 增长、alpha 渐隐、带噪声，材质复用 `ExtinguisherSpray.CreateSoftMaterial("ExtinguishFxMat")`，另挂 3D 音源播 `Burst(kind)`（pitch 0.94~1.06）。

| 类型 | `gravityModifier` | 观感 |
|---|---|---|
| CO₂ | −0.05 | 白雾向上泄压飘散 |
| 水基 | −0.32 | 蒸汽上腾 |
| 干粉 | +0.18 | 粉尘下落沉到桌面 |

`Extinguisher.ApplySprayToFires()` 里 `fire.ApplyExtinguish()` 之后 `if (fire.IsExtinguished && _burstDone.Add(fire))` 触发，`_burstDone` 用 HashSet 保证一瓶对一堆火只播一次。

#### 8.2.4 `ExtinguisherKindTag.cs`（新增）：瓶身上方的类型标签

世界空间 Canvas，深色底 + 左侧同色条 + 两行文字（类型名 `fontSize 48`、适用火源 `fontSize - 14`），尺寸 **0.72 × 0.30 m**，`LateUpdate` 只绕 Y 轴朝向相机、并沿朝相机方向前移 `pushFromWall = 0.10 m` 避免插进背后墙面；拿在手上时 `hideWhenHeld` 自动隐藏。标签**不挂在瓶身下面**，而是放进场景根、每帧按瓶身锚点摆世界坐标——瓶体本身带非等比缩放，挂在下面会把文字一起压歪。

#### 8.2.5 `PlazaKnowledgeBoard.cs`（新增）：门外广场知识墙 + 地面标语

`SetupOutdoorPlaza()` 末尾挂载（`boardCenter (13.40,0,0)`、`facingYaw 90` 正面朝西、`floorTextCenter (10.90,0.02,1.55)`、`floorTextYaw 90`）：

| 构件 | 尺寸 / 参数 | 内容 |
|---|---|---|
| 红标题条 | 3.60 × 0.32 m，y≈2.60 | 「办公室消防演练 · 安全知识」 |
| 展板 ×3 | 每块 2.00 × 1.55 m，底部 0.62 m、带立柱，深青底 + 绿标题条 + 黄标题（54）+ 白正文（38） | ①灭火器要对路 ②灭火器四步 ③疏散与集合 |
| 地面标语 | 4.40 × 0.72 m，深绿底白字，字头朝东 | 「灭火器四步：提·拔·握·压 ｜ 疏散：低姿·湿毛巾·到集合点」 |

展板内容（每块 5 条）：电气→CO₂/干粉（先断电）、A 类→水基/ABC、F 类→湿化学/干粉、油火禁泼水、对准根部；提/拔/握/压 + 留守防复燃；低姿湿毛巾、绿色出口不乘电梯、集合点清点报警、先试门温、不折返取物。

### 8.3 玩家视角验收（`ExtinguisherFxProbe.RunSc1 / RunSc2 / RunSc3`）

探针走真实 `Play`：开始演练 →（电气情景先 `Cut()` 断电）→ 站到瓶前 1.54 m 拍标签 → `GrabExternal` 吸附 → 答题通过 → 拔销 → 压把 → 站到火前 2.23 m 起喷，0.45 s 拍喷射 → 火灭瞬间拍收尾 → 放下瓶走进广场拍知识墙总览、近距、地面字。三情景各 6 张，共 18 张。

| 图 | 玩家看到什么 | 判读 |
|---|---|---|
| `vr28_fx_sc1_1bottle.png` | 蓝色标签「二氧化碳 CO₂ / 适用：电气设备 / 精密仪器」 | 类型一眼可辨 |
| `vr28_fx_sc1_2spray.png` | 瓶口向火根喷出一道**白亮窄气柱** | CO₂ 细高速，区别于粉雾 |
| `vr28_fx_sc1_3burst.png` | 火灭瞬间柜顶腾起白色气雾并向上飘 | CO₂ 泄压上飘 |
| `vr28_fx_sc2_1bottle.png` | 米黄标签「ABC 干粉 / 纸张 / 木材（A 类）+ 电器」 | 类型可辨 |
| `vr28_fx_sc2_2spray.png` | 瓶口到桌面一大团**宽锥白色粉雾** | 干粉宽、慢、浓 |
| `vr28_fx_sc2_3burst.png` | 火灭瞬间桌面扬起粉尘并落下 | 干粉下沉 |
| `vr28_fx_sc3_1bottle.png` | 淡蓝标签「水基（湿化学）/ 食用油 / 小家电（F 类）」 | 类型可辨 |
| `vr28_fx_sc3_2spray.png` | 蓝白色**带颗粒的水雾**斜打向炉面 | 水基带水滴下坠 |
| `vr28_fx_sc3_3burst.png` | 火灭瞬间一大片**蓝色蒸汽**沿柜体上腾 | 水基蒸汽，与干粉明显不同 |
| `*_4wall.png` / `*_5wallclose.png` / `*_6floor.png` | 三块展板全貌（一/二/三 顺序正确）、近距可读正文、地面标语横排正读 | 知识墙与地面字可读 |

断言链（`Logs/shots/_extfx.txt` + 三份副本，末两行 `FAIL_COUNT=0` / `PROBE_EXIT=0`）：

| 断言 | 情景一 | 情景二 | 情景三 |
|---|---|---|---|
| 场景瓶 `kind` 与情景期望一致 | CO₂ ✔ | 干粉 ✔ | 水基 ✔ |
| 手持后 `sprayLoopClip` 名字含本类型 token | `SFX_Spray_CO2` ✔ | `SFX_Spray_Powder` ✔ | `SFX_Spray_Wet` ✔ |
| 喷射时粒子在播 / 音效在播 / HUD 含类型名与适用火源 | ✔ | ✔ | ✔ |
| 喷射几何：粒子数 ≥ 10、喷口朝向对准火 dot ≥ 0.94 | 236 / 0.96 ✔ | ✔ | ✔ |
| 火灭：`progress01 = 1.00`，`ExtinguishFx.SpawnCount 0→1`，`LastKind` 对应 | ✔ | ✔ | ✔ |
| 知识墙 3 块 + 每块 ≥ 4 条 + 地面字存在 | ✔ | ✔ | ✔ |

### 8.4 判图后返工的三处（都按真实缺陷处理）

| 现象 | 根因 | 处理 |
|---|---|---|
| 第一版 `_2spray` 上**几乎看不到喷流** | 初始粒径曲线首帧只有 0.35×、alpha 首帧 0、单粒子尺寸 0.035~0.05 m，2 m 外只剩几个像素；干粉还会被火光 bloom 吃掉 | 首帧粒径 0.9×、alpha 首帧 0.55；三套 `startSize` 加大到 0.13~0.165 m、`rate` 480~600，CO₂ 提速到 5.6 m/s 让气柱穿过火面 |
| 瓶身标签文字**被压扁、字挤在一起** | 标签挂在瓶身下面，瓶体是非等比缩放（压扁/拉高），Canvas 跟着被压 | 标签脱离父级放进场景根，每帧按瓶身锚点写世界坐标；同时放大到 0.72×0.30 m、字号 48 |
| 同一探针两次跑，喷口朝向 dot 一次 0.97、一次 0.82 导致喷不中 | 探针直接改 `rigRoot.position`，玩家根上的 `CharacterController` 会在随后几帧把「站进桌柜体积」的相机顶开，取景悄悄退回上一个站位 | 探针 `MoveStand()` 先 `cc.enabled = false` 再摆位；顺带补 `喷射几何` 日志（粒子数 / 喷口朝向 / 对准 dot）做断言 |

### 8.5 改动文件清单（第 28 轮）

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/FireSafety/ExtinguisherKind.cs` | **新增**：枚举 + Info + `SprayProfile` 九字段 |
| `Assets/Scripts/FireSafety/ExtinguisherSfx.cs` | **新增**：三种合成循环音 + 收尾声 |
| `Assets/Scripts/FireSafety/ExtinguishFx.cs` | **新增**：一次性熄灭粒子 + 音效，暴露 `SpawnCount` / `LastKind` |
| `Assets/Scripts/FireSafety/ExtinguisherKindTag.cs` | **新增**：瓶身类型标签（脱离瓶体缩放、朝相机、手持隐藏） |
| `Assets/Scripts/FireSafety/PlazaKnowledgeBoard.cs` | **新增**：知识墙三展板 + 红标题条 + 地面标语 |
| `Assets/Scripts/FireSafety/ExtinguisherSpray.cs` | 加 `kind` / `ApplyKind()`；粒径与 alpha 首帧；`CreateSoftMaterial()` 静态化供复用 |
| `Assets/Scripts/FireSafety/Extinguisher.cs` | 加 `kind` / `KindLabel` / `SuitableLabel` / `SetKind()`，熄灭瞬间触发 `ExtinguishFx` |
| `Assets/Scripts/FireSafety/DrillScenario.cs` | `FireScenario.extinguisherKind`，`ApplyScenario` 里 `SetKind`，HUD 追加「灭火器：类型（适用 …）」 |
| `Assets/Editor/FireSafetySetup.cs` | 新增 `SetupExtinguisherKind()`（01=CO₂/02=干粉/03=水基 + 标签）；情景简介改写；`SetupOutdoorPlaza()` 挂知识墙 |
| `Assets/Editor/FireSafetySelfTest.cs` | 新增 `TestExtinguisherKindRound28()`：三套参数差异、三色互异、合成 clip 名/时长、情景 kind、标签、HUD、知识墙 |
| `Assets/Editor/ExtinguisherFxProbe.cs` | **新增**：`RunSc1/2/3` 玩家视角探针（含喷射几何断言） |
| `Assets/Scenes/FireSafetyOffice.unity` | 三瓶 kind / 标签组件 / 知识墙组件写回 |

### 8.6 自检与回归

- `FireSafetySelfTest.RunAll` → `Logs/selftest28.log`，末尾 `[TEST] ==== 全部通过 ====`；新增 `TestExtinguisherKindRound28()` 12 条断言全 ✔。
- 三情景探针 → `Logs/extfx_RunSc1/2/3.log`、`Logs/shots/_extfx_RunSc1/2/3.txt`，全部 `FAIL_COUNT=0` / `PROBE_EXIT=0`。
- 第 27 轮门禁、第 26 轮拾取链路未被本轮改动影响（本轮只动了灭火剂与广场装饰，未触碰 `RoomDoor` / `ToggleGrab` 逻辑）。

## 第 29 轮：头显移动/操作补齐 + 「模拟器 / 真机 / 桌面」运行环境识别与操作模式切换

### 9.1 需求与勘察：头显的移动操作第 21 轮就有了，缺的是「现在到底是什么设备」

用户这轮要三件事：①给项目补 VR 头显相关的移动、操作代码与步骤；②运行时判断是 XR 模拟器还是真接了头显，据此切不同操作模式；③以玩家视角测试，通过后推仓库、出日志。

先查代码，避免重复造轮子。第 21 轮已经把两套输入层建好了：

| 已有能力 | 位置 | 具体参数 / 行为 |
|---|---|---|
| 头显移动 | `XRDrillLocomotion.cs` | 左摇杆沿头显朝向平移（`moveSpeed 2.2` m/s、`crawlSpeed 1.05`、推满 >0.92 触发 `sprintMultiplier 1.6` 小跑）；右摇杆 45° 瞬转（`snapAngle 45`、`turnSpeed 120`、`snapCooldown 0.35`）；头显低于 `crouchHeight 1.15 m` 自动蹲行；另有 WASD/QE/Shift 键盘调试与 `SetVirtualInput` 探针注入口 |
| 头显操作 | `VRDrillInput.cs` | Grip 抓取、Trigger 拔销→压把→喷射、A 开始/翻页、B/X 选答、左摇杆按下蹲起、右手射线点 UI、`SendHapticImpulse` 触觉脉冲 |

真正的缺口只有一处：**运行环境判据太粗**。

| 原判据 | 问题 |
|---|---|
| `XRSettings.isDeviceActive` 为真就当成真机 | XR 模拟器（XR Simulation / MockHMD）也会让它为真，模拟器被"真机"接管后：桌面 HUD 消失、键鼠全部失效，人在电脑前反而动不了 |

所以本轮不是重写输入，而是加一层「设备识别 + 模式编排」，并补上头显侧的位移与重定位接口。

### 9.2 实现

#### 9.2.1 `RuntimeDeviceMode.cs`（新增，约 350 行）：三态识别 + 一张模式表

三态：`DeviceKind { Desktop, Simulator, Headset }`、两套控制：`ControlMode { KeyboardMouse, XRControllers }`。

检测证据链（四路并用，缺一路都能退化）：

| 证据 | 取值方式 | 用途 |
|---|---|---|
| XR 是否在输出 | `XRSettings.isDeviceActive` | 为假直接判桌面 |
| 已加载设备名 | `XRSettings.loadedDeviceName` | 命中关键字判模拟器 |
| 显示子系统描述 | `SubsystemManager.GetSubsystems<XRDisplaySubsystem>()` → `subsystemDescriptor.id` | 命中关键字判模拟器 |
| HMD 设备名 | `InputDevices.GetDeviceAtXRNode(XRNode.Head).name` | 命中关键字判模拟器，否则真机 |

纯函数 `Classify(bool xrActive, string loadedDevice, string hmd, string displayId, out DeviceKind, out string reason)`：未激活 → `Desktop`；四段文本任一含 `simulat` / `mock` / `xrsimulation` / `editor test` / `fake`（不区分大小写）→ `Simulator`；其余 → `Headset`。纯函数不碰引擎状态，自检可以直接灌字符串。

`PlanFor(kind)` 出来的 `struct ModePlan` 就是"切换"的全部内容：

| 层 | 真机 Headset | XR 模拟器 | 桌面 Desktop |
|---|---|---|---|
| `VRDrillInput.enabled` | 开 | 关 | 关 |
| `DesktopDrillInput.enabled` + 底部 HUD | 关（HUD 一并收起） | 开 | 开 |
| `XRDrillLocomotion` 控制模式 | 手柄模式（键盘调试关） | 键鼠（WASD/QE/Shift 可用） | 键鼠 |
| 手柄模型 `VRControllerVisual` | 显示 | **显示**（模拟器里它是"手在哪"的唯一参照） | 隐藏 |
| VR 世界空间提示条 | 显示，状态写「VR 头显 · 手柄摇杆 / Grip+Trigger」 | 收起 | 收起 |
| 灭火器 `desktopMode` | 强制关（否则压把链路断） | 保留 | 保留 |

`ApplyMode(bool log)` 一次性把上表落到各组件上，并打 `[Mode] 真机 → 手柄 · …（切换次数 N）`；`ForceKind(kind)` 置手动覆盖、`ClearForce()` 回到自动检测。静态出口：`Instance` / `HasInstance` / `RealHeadset` / `VisualsVisible` / `XRControlActive`。

自动复检：`pollInterval = 1.0 s` 轮询 + 订阅 `InputDevices.deviceConnected / deviceDisconnected` 事件立即复检；热键 **F2 = 强制键鼠、F3 = 强制手柄、F4 = 回自动**（真机不在手边时也能排练两种模式）。

#### 9.2.2 `XRDrillLocomotion.cs`（改）：给头显补三个接口

| 新增 | 作用 |
|---|---|
| `xrControlMode` + `ApplyControlMode(bool xr)` | 手柄模式只读摇杆、不读键盘；键鼠模式反过来，避免两套输入同时推人 |
| `RecenterOrigin()` | 把移动锚点挪到当前头显位置（`offset.y = 0`），玩家原地踏步后"脚下就是新原点" |
| `RecenterTo(pos, yawDegrees, applyYaw)` | 供编辑器探针 / 传送把玩家摆到指定站位与朝向；内部 `MoveRoot()` 先关 `CharacterController` 再写位置，避免被自身碰撞顶开 |

#### 9.2.3 `VRDrillInput.cs` / `VRControllerVisual.cs` / `VRDrillHud.cs` / `DesktopDrillInput.cs`（改）

| 文件 | 改动 |
|---|---|
| `VRDrillInput` | `HeadsetPresent` 改为「有设备层就问 `RuntimeDeviceMode.RealHeadset`，没有才退回 `XRSettings.isDeviceActive`」；新增 `locomotion` 字段；HUD 状态行改用 `StatusLine`；**右摇杆按下 = `Recenter()`**，提示「已重定位：你脚下成为新的移动原点」 |
| `VRControllerVisual` | 新增 `modeVisible`；可见性最终由 `RuntimeDeviceMode.VisualsVisible` 决定（未挂设备层时退回旧行为） |
| `VRDrillHud` | 新增 `CurrentStatus` 属性（状态行文本），探针与自检断言用它，不再误读提示行 |
| `DesktopDrillInput` | `EnsureReady()` 公开（组件被 XR 模式禁掉后重开也能补建 HUD）；新增 `SetHudVisible(bool)`；底部 HUD 首行加模式标签 `【桌面模式】/【XR 模拟器】`，面板 120 → 168 高 |

#### 9.2.4 `FireSafetySetup.cs`（改）：把设备层装进场景

`RunSetup()` 在 `SetupVrInput()` 之后调 `SetupRuntimeDeviceMode()`：挂在 `rig.Origin` 上，接线 `vrInput / desktopInput / locomotion / vrHud / cam / rigRoot`，`autoDetect = true`、`pollInterval = 1.0`、`allowHotkeys = true`，并把 `mode.locomotion` 回写给 `vrInput`。日志打印「✔ 设备检测层：RuntimeDeviceMode…」。

### 9.3 使用步骤（头显 / 模拟器 / 桌面）

1. **桌面直接跑**：Unity 里 Play，检测结果「桌面模式 · 键盘鼠标 / WASD+鼠标」，底部 HUD 第一行显示 `【桌面模式】`，鼠标右键拾瓶、左键拔销/压把。没有头显、也不想开模拟器时什么都不用配。
2. **XR 模拟器**：装了 XR Simulation（或 MockHMD）后 Play，检测结果「XR 模拟器 · 键盘鼠标」，仍用键鼠操作，但**手柄模型会出现**，用来确认虚拟手柄的位置与射线方向；此时 `VRDrillInput` 不接管，避免模拟器冒充真机。
3. **真机头显（Quest 等）**：
   - 有线：装好 Quest Link / Air Link，头显里允许数据连接，Unity Play 后 `XRSettings.isDeviceActive` 为真、HMD 名不含模拟器关键字 → 自动进「VR 头显 · 手柄摇杆 / Grip+Trigger」。
   - 打包：`Project Settings → XR Plug-in Management` 在 Android 页勾选 **OpenXR**（并按厂商要求启用对应 feature）；Player Settings 里 `Scripting Backend = IL2CPP`、`Target Architectures = ARM64`；打包安装后在头显里启动。
4. **真机键位**：左摇杆推 → 沿头显朝向走（推满小跑）；右摇杆左右 → 45° 瞬转；**右摇杆按下 → 重定位**（把当前脚下设为新原点，坐姿/原地转身后必用）；Grip 抓瓶、Trigger 拔销/压把/喷射；A 开始与翻页、B/X 答题；头显蹲下低于 1.15 m 自动切蹲行。
5. **排练用热键**：F2 强制键鼠、F3 强制手柄、F4 回自动检测；切换会打 `[Mode]` 日志并计入「切换次数」。

### 9.4 玩家视角验收（`InputModeProbe.RunFromBatch`）

探针走真实 `Play`（不加 `-nographics`、不加 `-quit`），六阶段状态机，每阶段出图并断言：

| 阶段 | 动作 | 断言 | 图 |
|---|---|---|---|
| 1 | 自动检测（批处理无头显） | 判 Desktop、键鼠层开、手柄层关、键盘调试开、手柄模型隐藏、VR 提示条隐藏、底部 HUD 含「桌面模式」 | `vr29_mode_1desktop.png` |
| 2 | `ForceKind(Headset)` | 判「VR 头显」、手柄层接管、键鼠让位、移动层切手柄、手柄模型出现、VR 提示条出现且状态含「VR 头显」 | `vr29_mode_2headset.png` |
| 3 | 虚拟摇杆推 0.8 走 0.9 s | 位移 ≥ 1.2 m、速度落在 1.5~4.0 m/s、y 未顶飞 | `vr29_mode_3xrmove.png` |
| 4 | `RecenterTo(3.20, 0, 1.20, yaw 90)` | 水平误差 ≤ 0.2 m、眼高 1.4~1.8 m | `vr29_mode_4recenter.png` |
| 5 | `ForceKind(Simulator)` | 判「XR 模拟器」、切回键鼠、手柄层关、键盘调试开、手柄模型仍可见、VR 提示条收起、HUD 含「XR 模拟器」 | `vr29_mode_5simulator.png` |
| 6 | `ClearForce()` | 回自动判 Desktop、键鼠接管、HUD 回「桌面模式」 | `vr29_mode_6auto.png` |

最终读数：位移 **1.51 m**、速度 **1.76 m/s**、重定位误差 **0.00 m**、眼高 **1.68 m**，`切换次数=2 · 通过=是`，`_modeprobe.txt` 末两行 `FAIL_COUNT=0` / `PROBE_EXIT=0`。

逐张判读（玩家视角）：

| 图 | 玩家看到什么 | 结论 |
|---|---|---|
| `_1desktop` | 情景三茶水间内景，火光在操作台，底部三行 HUD 完整（`【桌面模式】` / `【鼠标操作】…` / `状态：第 1 阶段…`） | 桌面键鼠可玩、UI 不裁字 |
| `_2headset` | 视线下方一条世界空间提示条「操作模式（手动）：VR 头显 · 手柄摇杆 / Grip+Trigger」，桌面 HUD 已收起 | 手柄模式接管、只有一套 UI |
| `_3xrmove` | 玩家被推到门外广场，正对知识墙：红标题条 + 三块展板正文可读，画面底部仍是 VR 提示条 | 摇杆真的位移了，且取景能证明"走过去了" |
| `_4recenter` | 回到大厅指定站位，正对「安全出口 EXIT」与门洞，透过门能看到广场知识墙 | 重定位生效、眼高正常 |
| `_5simulator` | 同一站位，底部 HUD 换成 `【XR 模拟器】…`，VR 提示条收起 | 模拟器不冒充真机 |
| `_6auto` | 回到 `【桌面模式】…` | 清除覆盖后能回到自动检测 |

### 9.5 判图后返工（四条，全部按真实问题处理）

| 现象 | 根因 | 处理 |
|---|---|---|
| 室内截图里知识墙文字**穿透墙面与火光**显示，像穿墙 bug | 探针离屏 RT 用了 **0 深度位**：没有深度缓冲时所有物体"没有前后关系"，世界空间 Canvas 直接叠在墙上 | `RenderTexture(1280,720,**24**,…)` 补 24 位深度。加深度后同一角度再看，墙正常遮住知识墙（曾先怀疑 Canvas 未做深度测试，写了一份 `ZTest` 材质副本，验证出是探针问题后已回退，代码保持原样） |
| 手柄模式下**两块 HUD 叠在画面底部**（世界空间 VR 提示条 + 桌面黑条） | `DesktopDrillInput.enabled=false` 只停逻辑，Overlay Canvas 仍在渲染 | 新增 `SetHudVisible()`，`ApplyMode()` 里按 `desktopInputEnabled` 一起收/放 |
| 桌面 HUD 加模式标签后**末行被裁** | 1640×120 的黑条容不下三行折行文本 | 标签缩成 `【桌面模式】`（不再塞整串 StatusLine），面板 120 → 168 |
| 移动截图只剩天空与地平线 | 起点在出生点旁、朝 +z 走 4.7 m 直接越过广场矮墙，镜头对着空天际线 | 起点改到广场 `(7.80, 0, 1.20)` 朝东对知识墙，摇杆 0.8（不触发小跑）走 0.9 s，速度与位移都可核对 |

### 9.6 改动文件清单（第 29 轮）

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/FireSafety/RuntimeDeviceMode.cs` | **新增**：三态识别、`ModePlan` 编排、F2/F3/F4 热键、自动复检、HUD 文案 |
| `Assets/Editor/InputModeProbe.cs` | **新增**：六阶段玩家视角探针（6 图 + 断言 + `_modeprobe.txt`） |
| `Assets/Scripts/FireSafety/XRDrillLocomotion.cs` | 加 `xrControlMode` / `ApplyControlMode` / `RecenterOrigin` / `RecenterTo` / `MoveRoot` |
| `Assets/Scripts/FireSafety/VRDrillInput.cs` | 真机判定改走设备层、接 `locomotion`、右摇杆按下重定位、状态行用 `StatusLine` |
| `Assets/Scripts/FireSafety/VRControllerVisual.cs` | `modeVisible` + 由 `VisualsVisible` 决定显示 |
| `Assets/Scripts/FireSafety/VRDrillHud.cs` | 新增 `CurrentStatus` |
| `Assets/Scripts/FireSafety/DesktopDrillInput.cs` | `EnsureReady()` / `SetHudVisible()` / 模式标签 / 面板加高 |
| `Assets/Editor/FireSafetySetup.cs` | 新增 `SetupRuntimeDeviceMode()` 并在 `RunSetup()` 调用 |
| `Assets/Editor/FireSafetySelfTest.cs` | 新增 `TestRuntimeDeviceModeRound29()` 25 条断言 |
| `Assets/Scenes/FireSafetyOffice.unity` | XROrigin 挂 `RuntimeDeviceMode` 并写回接线 |

### 9.7 自检与已知限制

- `FireSafetySelfTest.RunAll` → `Logs/selftest29.log`，末尾 `[TEST] ==== 全部通过 ====`；第 29 轮 25 条断言全 ✔（三态 `Classify`、三套 `ModePlan` 字段组合、场景组件与五项接线、默认 Desktop、`locomotion` 默认非手柄模式与三个方法存在、移动参数表、`RealHeadset` 默认假）。
- 探针 → `Logs/modeprobe.log`、`Logs/shots/_modeprobe.txt`，`FAIL_COUNT=0` / `PROBE_EXIT=0`；六张图 `Logs/shots/vr29_mode_*.png`。
- **已知限制（如实记录）**：本机没有可用的头显运行时（批处理日志里有 OpenXR 初始化异常），真机分支只做了两件事——`Classify()` 纯函数灌入 `Oculus Quest 3` / `MockHMD` 等字符串的判定、以及 `ForceKind(Headset)` 注入后各层真实切换与位移；**摇杆死区、瞬转舒适度、HUD 在头显 FOV 里的实际位置**仍需接上真机复验。16:9 截图里键位提示行贴近画面下缘，属相机 FOV 比头显窄造成的取景差异，未做额外下压。

## 第 30 轮：手柄按键「只有摇杆能动」的根因修复 + 头显内即时反馈

### 10.1 用户报告与实物确认

玩家在 Quest 2（Link 串流到 PC）里进入演练后：**电脑上的模拟器画面能正常投到头显里，左摇杆也能走、能转向，但 Grip / Trigger / A / B / X 全部没有任何反应**。随附三张实拍确认设备为 Quest 2 头显（背面贴「동아대학교 / 오큘러스 퀘스트2 / 컴퓨터공학과」资产标签）+ Quest 2 右手柄（A/B、摇杆、扳机），排除了"手柄没电/没配对"这类表层原因。

### 10.2 根因：两套输入在读同一只手柄，但只有一套读得到

| 证据 | 说明 |
|---|---|
| `ProjectSettings/ProjectSettings.asset` → `activeInputHandler: 1` | 工程是「**Input System Package (New)**」独享模式，旧 `UnityEngine.XR.InputDevices` 不再自动接线 |
| 摇杆能走 | `XRDrillLocomotion` 的移动/转向走 XRI 的 `InputActionReference`（新输入系统路径），所以正常 |
| 其余键全哑 | 第 21 轮的 `VRDrillInput` 用 `InputDevices.GetDeviceAtXRNode(...)` + `CommonUsages` 裸读（旧 XR 路径），`isValid=false`，读到的是"永远没按下" |
| 探针现场读数 | `VRInputSource.Diagnostics()` 显示 `设备 左=未识别 右=未识别 头显=— · 输入源 InputSystem` —— **新输入系统里已经有 XRController 设备，旧 XR 裸读却一只都看不到**，与症状完全对上 |
| 附带隐患 | 显示被串流/镜像接管时 `XRSettings.isDeviceActive` 可能为假，第 29 轮的检测会把真机判成「桌面模式」→ `VRDrillInput` 被停掉，同样表现成"按键全哑" |

### 10.3 修复方案：一条统一输入通道 + 三层证据 + 三处可见反馈

**（1）`VRInputSource`（新增，静态多路读取层）**

按顺序取第一个拿得到值的来源，`Snapshot` 一次给出 9 个按键 + 4 个设备状态 + 诊断串：

1. XRI `InputActionReference`（扳机，场景里已绑定）；
2. 新输入系统 `UnityEngine.InputSystem.XR.XRController`（usages `LeftHand`/`RightHand`，控制名 `trigger`/`grip`/`primaryButton`/`secondaryButton`/`primary2DAxisClick`）；
3. 旧 `UnityEngine.XR.InputDevices` + `CommonUsages`（兼容老插件）；
4. **键盘兜底**：仅当"判定为头显、但左右手柄都没被识别"时启用（`Space`=Trigger、`G`=Grip、`Enter`=A、`B`/`X`/`Y`、`R`=右摇杆按下），并让面板明写"正在用键盘兜底"，避免玩家以为按键坏了。

**（2）`RuntimeDeviceMode` 检测补丁**：`DetectNow()` 额外统计左右手柄 `isValid` 数量写入 `controllerCount`；若 `Classify` 判 Desktop 但手柄 ≥1 只，改判 `Headset`，理由写明「显示未激活，但已识别到 N 只 XR 手柄（串流/镜像场景），按真机处理」。`RealHeadset` 语义同步改为 `deviceKind == Headset`，静态 `Controllers` 供 HUD/自检查询。

**（3）`VRDrillInput` 接线**：`OnEnable` 调 `VRInputSource.BindActions(rightTrigger, leftTrigger)`；`Update` 用一份 `Snapshot` 取代所有裸读，新增虚拟键 `vGrip` / `vLeftSecondary` / `vStickClickRight`（探针注入用）；删掉旧的 `ReadTrigger()` / `Button()`。

**（4）三处"按了有反应"的反馈**

| 反馈 | 触发 | 玩家看到/感到 |
|---|---|---|
| 操作帮助 · 输入诊断面板（`VRHelpPanel`） | 左手柄 **Y**（或键盘 F5） | 视线前 1 m 的 900×800 世界面板：模式、`设备 左/右/头显`、实时 `T/G` 数值、完整键位表、三步排查；打开时自动收起提示条，避免两层字叠在一起 |
| 右手激光 + 命中点（`VRPointerVisual`） | 常驻（仅在头显接管时显示） | 从右手柄射出的线，指到墙是暖白、指到电源箱/门/灭火器/UI 变绿并放大命中点，进入可交互目标时右手**轻震一下**（0.6 s 冷却） |
| 空按提示 | Grip 按下但手里没瓶 / Trigger 按下但射线没指到可交互物 | 提示条各出一句明确原因（"手边没有可抓的灭火器（站到瓶旁 2 米内…）" / "射线没指到可交互物（电源箱 / 门 / 灭火器）"），不再"按了毫无动静" |

另外：手柄未识别时，VR 提示条状态行直接显示「VR 头显 · 手柄未识别（按 Y 看诊断）」；HUD 字号/面板放大（`canvasScale 0.00038 → 0.00046`，40/36/26 → 44/40/30），并把提示条上移 6 cm（`verticalOffset -0.40 → -0.34`），800×450 三行不再贴画面下缘。

### 10.4 玩家视角验收（`InputModeProbe.RunFromBatch`，八阶段）

| 阶段 | 动作 | 断言 | 图 |
|---|---|---|---|
| 1~6 | 同第 29 轮（桌面 → 强制真机 → 摇杆走 → 重定位 → 模拟器 → 回自动） | 全部保持 ✔ | `vr29_mode_1~6*.png` |
| 3 | 摇杆 0.8 走 0.9 s | 位移 **1.59 m**、速度 **1.76 m/s**、y 未被顶飞 | `vr29_mode_3xrmove.png` |
| 7 | 强制真机 + 虚拟输入，按 **Y** | 帮助面板能打开、绑定玩家相机、诊断含「设备」「输入源」、键位表含 Trigger/Grip、右手 `LineRenderer` 已启用且两点齐全 | `vr30_input_help.png` |
| 8 | 收起面板、空按 **Grip** | 提示条出现且含「Grip」、面板已收起且提示条恢复 | `vr30_grip_hint.png` |

最终读数 `切换次数=3 · 通过=是`，`_modeprobe.txt` 末两行 `FAIL_COUNT=0` / `PROBE_EXIT=0`。

### 10.5 判图后返工（按真实缺陷处理，不掩盖）

| 现象 | 根因 | 处理 |
|---|---|---|
| 帮助面板第一版标题换行压住正文、正文与排查提示**溢出面板右/下边界** | 900×640@0.00075 太小、标题 44 px 太长、文案单行过长 | 面板 900×700→**900×800**，标题 44→38 且只写「操作帮助 · VR 头显」，正文 26 px 重排为短行（"左摇杆 移动…"），提示改成 ①②③ 三行 |
| 面板文字**糊到看不清**（0.95 m 处字高仅 0.7°） | 字号×缩放的物理尺寸过小 | 缩放到 **0.00078**（字高 ≈0.020 m → 1.15°）、`dynamicPixelsPerUnit 3 → 8`、面板推到 1.0 m |
| 面板后面的知识墙**透字**、正文读不清 | 背景色 alpha 0.94 仍偏透 | 背景改不透明白黑（`0.03,0.05,0.08,alpha 1`） |
| 面板打开时 VR 提示条仍压在下面（两层字叠一起） | HUD 每帧 `SetVisible(active)` 不顾面板状态 | `hudWant = active && (helpPanel == null \|\| !helpPanel.Visible)`，面板打开即收提示条 |
| 阶段 7 第一次判定失败：Y 没反应 | ① 阶段 6 拍图很慢，`_wait` 一帧跨过 0.05/0.25 s 时间窗，注入代码整段被跳过；② 探针没开 `virtualMode`，Y 读的是真实设备（没有） | 阶段 7/8 改成**状态驱动**（`!_vr.enabled` → 强制真机并开虚拟输入；`!_yPressed` → 按下）而不是时间窗；并补 `virtualMode=true` |
| HUD 放大后三行贴底被切 | 面板变高 + `verticalOffset -0.40` | 上移到 **-0.34 m**，字号 30，键位行完整入镜 |

### 10.6 改动文件清单（第 30 轮）

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/FireSafety/VRInputSource.cs` | **新增**：XRI→InputSystem→XR.Input→键盘四级读取、`Snapshot`、`Diagnostics()`、`ControllerBrief()` |
| `Assets/Scripts/FireSafety/VRHelpPanel.cs` | **新增**：Y/F5 开关的操作帮助 + 输入诊断世界面板（900×800@0.00078，1 m） |
| `Assets/Scripts/FireSafety/VRPointerVisual.cs` | **新增**：右手激光 + 命中点 + 可交互物变绿 + 悬停触觉 |
| `Assets/Scripts/FireSafety/VRDrillInput.cs` | 接 `VRInputSource`、删裸读、Y 开关帮助、Grip/Trigger 空按提示、`PulseRightPublic`、新虚拟键、HUD 让位与状态短句 |
| `Assets/Scripts/FireSafety/RuntimeDeviceMode.cs` | `controllerCount` + 「显示未激活但识别到手柄 → 按真机处理」、`RealHeadset` 语义、静态 `Controllers` |
| `Assets/Scripts/FireSafety/VRDrillHud.cs` | 字号 44/40/30、面板 1500×460、`canvasScale 0.00046`、`verticalOffset -0.34` |
| `Assets/Editor/FireSafetySetup.cs` | `SetupVrInput()` 挂 `VRHelpPanel` / `VRPointerVisual` 并接线，日志文案更新 |
| `Assets/Editor/FireSafetySelfTest.cs` | 新增 `TestInputFeedbackRound30()` 18 条断言并在 `RunAll()` 调用 |
| `Assets/Editor/InputModeProbe.cs` | 阶段 6 → 7（Y 帮助面板）、7 → 8（Grip 空按提示），新增 `HelpBody()`、射线断言、`vr30_*.png` |
| `Assets/Scenes/FireSafetyOffice.unity` | RunSetup 写回：右手柄挂 `VRPointerVisual`、XR Origin 下挂 `VRHelpPanel` |

### 10.7 自检、已知限制与真机复验步骤

- `FireSafetySelfTest.RunAll` → `Logs/selftest30.log`，末尾 `[TEST] ==== 全部通过 ====`；第 30 轮 18 条断言全 ✔（四级读取可调用、诊断含设备/输入源/T/G、无手柄时"未识别"、面板存在且能开关、激光挂在右手且接线、三个新虚拟键、`PulseRightPublic`、旧裸读已删、`controllerCount`/`Controllers`/`RealHeadset` 语义）。
- 探针 → `Logs/modeprobe30.log`、`Logs/shots/_modeprobe.txt`，`FAIL_COUNT=0` / `PROBE_EXIT=0`；新增 `vr30_input_help.png`、`vr30_grip_hint.png`。
- **已知限制（如实记录）**：本机没有可用头显运行时（日志里 OpenXR 仍报 `XR_ERROR_FORM_FACTOR_UNAVAILABLE`），所有"真机"结论都来自 `ForceKind(Headset)` + 虚拟输入 + 手柄计数补丁，**没有在 Quest 2 上实测按键**；`InputSystem` 层能枚举到 XRController 说明修复方向正确，但具体控制名是否与你所用的 OpenXR 交互 profile 完全一致，仍需真机确认。
- **真机复验 5 步**（戴上头显后）：① 按 **Y** 打开帮助面板，看「设备 左/右」是否显示手柄名；② 捏 **Grip** 看 `T/G` 里的 `G` 是否跳到 1.00，松手回 0；③ 扣 **Trigger** 看 `T` 是否跳动，若跳动但没拔销，说明只是射线没指到瓶；④ 抬右手看激光是否跟着手走、指到电源箱/门是否变绿；⑤ 面板里若显示「正在用键盘兜底」，说明手柄没被识别，先查 Link/Air Link 与 SteamVR/OpenXR 运行时，而不是改键位。

## 第 31 轮：头显「只镜像桌面」的 XR 接管自检、F6 重试与位姿改走 Input System

### 11.1 用户复述与实时日志证据

玩家复述：Quest 2 接 Link 后，**头显里能看到电脑画面（虚拟显示屏）**，打开演练后只有摇杆能转视野，Grip / Trigger / A / B / X 全无反应。

本轮直接读玩家当时正在 Play 的编辑器实时日志（`summary/Logs/Editor.log`），证据链如下：

| 证据 | 日志原文 |
|---|---|
| OpenXR 实例能建 | `Runtime Name: Oculus`、`XrInstance Created with OpenXR API Version: 1.1.54` |
| 但拿不到头显形态 | `xrGetSystem: XR_ERROR_FORM_FACTOR_UNAVAILABLE`（`unity_session.cpp:328`） |
| 显示子系统启动失败 | `[Subsystems] Failed to initialize subsystem OpenXR Display [error: 1]` |
| Unity 判定为无 XR | `[Mode] Desktop → KeyboardMouse · 桌面模式（无 XR 设备）· XRSettings.isDeviceActive=false（切换次数 0）` |
| 相机没有位姿驱动 | `Camera "Main Camera" does not use a Tracked Pose Driver (Input System)` |
| 玩家实际用鼠标点开始界面 | `InputSystemUIInputModule.ProcessPointerButton → StartScreen.BuildScenarioRow`，随后 `[Drill] 情景切换到…` |

**结论**：这次 Play 里 Unity **根本没有进入 XR 会话**。头显只是把 Windows 桌面镜像过去；摇杆被虚拟桌面当成鼠标，桌面输入层又用鼠标做视角旋转，所以"只有摇杆能转视野"；手柄按键从头到尾没有进过 Unity。场景侧另有两处真缺陷：1 台相机、0 个 `TrackedPoseDriver`；`DrillTrackedPose` 仍在走旧 `InputDevices`（本工程是 Input System 独享，读不到位姿）。

### 11.2 修复方案：先让玩家看清"卡在哪"，再让位姿真正可用

**（1）`DrillTrackedPose` 改走 Input System 位姿**

- 相机（`XRNode.CenterEye`）优先读 `XRHMD.centerEyePosition / centerEyeRotation`，手柄读 `XRController.leftHand / rightHand` 的 `devicePosition / deviceRotation`；
- 只有 Input System 拿不到时才回落旧 `UnityEngine.XR.InputDevices`；
- 新增 `preferInputSystem`（默认 true）与只读 `HasPose`，`isTracked=false` 或数值 NaN/Inf 时不写入，避免把相机写成原点。

**（2）`RuntimeDeviceMode` 新增 XR 接管诊断与 F6 重试**

- 记录 `xrActive`（`XRSettings.isDeviceActive`）；
- 新增 `Guidance` / 纯函数 `GuidanceFor(kind, controllers, active)`，只在两种会误判的情况下出提示：
  - 桌面/镜像：说明 Unity 没进入 XR、点名「虚拟显示屏/远程桌面」，并给出"退出桌面视图→回 Link 首页→F6/按钮重试"；
  - 已进 XR 但 0 手柄：提示唤醒手柄、查电量配对；
  - 手柄正常（≥1 只）：返回空，不打扰玩家。
- 新增 `RetryXr()`（F6）：已激活时只重读状态；未激活时 `DeinitializeLoader → InitializeLoaderSync → StartSubsystems`，全程 try/catch 并把结果写日志。

**（3）`DesktopDrillInput`：顶部连接提示条 + 屏幕按钮**

- 新增顶部琥珀色提示条（1520×86，23 px），只在 `Guidance` 非空时出现；
- 屏幕右下角新增「重试 XR（F6）」按钮，鼠标可点，调用 `RetryXr()`；
- 底部键位 HUD 恢复 180 高，不再塞连接提示，避免叠字/裁切。

**（4）`VRHelpPanel`：头显里也能看到同一条指引**

- 正文在诊断行后插入 `⚠ Guidance`；
- 当指引已出现时，隐藏底部"没反应就按顺序查 ①②③"通用排查段，用腾出的高度保证 900×800 面板不溢字。

### 11.3 玩家视角判图后的返工（按真实缺陷处理）

| 现象（离屏出图） | 根因 | 处理 |
|---|---|---|
| 第一版把指引塞进底部 HUD，最后一行被屏幕下缘裁掉 | 键位 2 行 + 状态 1 行 + 指引 2 行超出 200 高 | 指引移到顶部独立提示条，HUD 恢复 180 |
| 帮助面板加指引后，正文与排查段**顶到背景外**（`vr30_input_help` 第一版） | 800 高装不下新增 3~4 行 | 有指引时隐藏 ①②③ 排查段 |
| 缩面板到 880@0.00072、字号 24 后文字发糊、行距过挤 | 字高×缩放变小 | 回到 900×800@0.00078、26 px、`dynamicPixelsPerUnit 8` |
| 顶部提示条遮挡倒计时 | 提示条贴顶 14 px、高 86 | 只占顶部 8%，倒计时在画面中部，实测不遮挡 |

### 11.4 改动文件清单（第 31 轮）

| 文件 | 改动 |
|---|---|
| `Assets/Scripts/FireSafety/DrillTrackedPose.cs` | 位姿改走 `XRHMD`/`XRController`，旧 `InputDevices` 兜底；新增 `preferInputSystem`、`HasPose` |
| `Assets/Scripts/FireSafety/RuntimeDeviceMode.cs` | `xrActive`、`Guidance`/`GuidanceFor`、`RetryXr()`（F6） |
| `Assets/Scripts/FireSafety/DesktopDrillInput.cs` | 顶部连接提示条、右下「重试 XR（F6）」按钮、HUD 180 |
| `Assets/Scripts/FireSafety/VRHelpPanel.cs` | 插入指引行；有指引时隐藏通用排查段 |
| `Assets/Editor/FireSafetySelfTest.cs` | 新增 `TestConnectionRound31()` 11 条断言并在 `RunAll()` 调用 |
| `Assets/Scenes/FireSafetyOffice.unity` | RunSetup 写回（无结构变化，刷新位姿脚本参数） |

### 11.5 验证证据

- `Logs/setup31.log`：末尾 `[Setup] ==== 搭建完成，场景已保存 ====`，无 `error CS`。
- `Logs/selftest31.log`：末尾 `[TEST] ==== 全部通过 ====；第 31 轮 11 条断言全 ✔（镜像指引文案、F6 提示、XR 有手柄时不打扰、`xrActive`/`RetryXr` 存在、`DrillTrackedPose` 默认走 Input System、相机锚点为 CenterEye）。
- `Logs/modeprobe31.log` + `Logs/shots/_modeprobe.txt`：八阶段全过，读数 **位移 1.58 m / 速度 1.76 m/s / 重定位误差 0.00 m / 眼高 1.68 m**，`切换次数=3 · 通过=是`，末尾 `FAIL_COUNT=0`、`PROBE_EXIT=0`。
- 出图复核：`vr29_mode_1desktop.png`（顶部指引条 + 右下按钮 + 底部 HUD 无裁切）、`vr29_mode_2headset.png`（手柄未识别状态行）、`vr30_input_help.png`（指引在面板内、无溢字）、`vr30_grip_hint.png`。

### 11.6 复跑与调参

```powershell
# 1) 装配（必须先关闭 Unity 编辑器，释放工程锁）
Unity.exe -batchmode -nographics -quit -projectPath D:\43763\VR\summary `
  -executeMethod FireSafety.EditorTools.FireSafetySetup.RunSetup -logFile Logs\setup31.log

# 2) 自检
Unity.exe -batchmode -nographics -quit -projectPath D:\43763\VR\summary `
  -executeMethod FireSafety.EditorTools.FireSafetySelfTest.RunAll -logFile Logs\selftest31.log

# 3) 玩家视角探针（不要加 -quit，脚本自己退出）
Unity.exe -batchmode -projectPath D:\43763\VR\summary `
  -executeMethod FireSafety.EditorTools.InputModeProbe.RunFromBatch -logFile Logs\modeprobe31.log
```

调参：提示条位置/字号在 `DesktopDrillInput.BuildHud()` 的 `XrGuideBar`（`anchoredPosition (0,-14)`、`sizeDelta (1520,86)`、`fontSize 23`）；帮助面板在 `VRHelpPanel.Build()`（`900×800`、`0.00078`、正文 26 px）。

### 11.7 已知限制与真机复验

- **本轮仍无法在 Quest 2 上实测按键**：日志证明 Unity 侧 OpenXR 拿不到 HMD（`XR_ERROR_FORM_FACTOR_UNAVAILABLE`），原因是头显当时停在「虚拟显示屏/远程桌面」而没有回到 Link 的 VR 会话；这段是环境问题，代码只能负责识别、提示与重试。
- 本轮能确认的是：① 相机/手柄位姿不再依赖失效的旧接口，XR 一旦接管就会跟随头显；② 玩家在 PC 画面和头显里都能看到"为什么没接管 + 下一步"；③ 不用重开 Play，F6/按钮即可重试。
- **真机复验 4 步**：① 头显里退出虚拟显示屏，回到 Link 首页（PC 端 Oculus 应用确认 Quest 2 已连接）；② Unity 点 Play；③ 若顶部仍提示"没有进入 XR"，按 F6 或点右下按钮；④ 进入 XR 后转头，画面应随头转动（不是只有摇杆能转），再按 Y 看「设备 左/右」是否识别到手柄。
- 若第 ③ 步一直失败：确认注册表 `HKLM\SOFTWARE\Khronos\OpenXR\1\ActiveRuntime` 指向 Meta 运行时、重启 `OVRServer_x64`、并确保 Link 没有被其它串流软件（Virtual Desktop / ALVR）占用。

**提交信息**

```
第31轮：头显只镜像桌面时补齐 XR 接管诊断、F6 重试与 Input System 位姿读取

- DrillTrackedPose 改读 XRHMD/XRController 位姿，旧 InputDevices 仅兜底
- RuntimeDeviceMode 记录 xrActive，新增 Guidance 与 RetryXr（F6）
- 桌面模式顶部提示条 + 右下「重试 XR」按钮，明确「虚拟显示屏不是 Link 会话」
- 帮助面板插入连接指引，并在有指引时隐藏通用排查段，避免溢字
- 自检新增 11 条断言；玩家视角八阶段探针 FAIL_COUNT=0 / PROBE_EXIT=0
```

**三条判成功硬条件**：① 玩家视角下能直接看到"Unity 没进 XR / 请退出桌面视图回 Link"的原因与重试入口，且不遮不裁；② 相机与手柄位姿默认走 Input System，`HasPose` 可作为是否真的拿到数据的判据；③ 自检与八阶段探针零失败，提交与远端 `main` 双向一致。

## 主要功能

- **手柄锚定 UI**：开始界面（前 1.0 m / 上 0.15 m）、倒计时（上 0.42 m / 前 0.12 m）、引导页（前 1.0 m / 上 0.14 m）都跟随右手手柄并朝向玩家（`StartScreen.cs` / `DrillCountdown.cs` / `OnboardingScreen.cs`）
- **引导页与结束页**：分页教学 + 成功/失败成绩单（`OnboardingScreen.cs` / `EndingScreen.cs`）
- **9 步顺序状态机**：①身处火场 → ②警报 → ③确认火点 → ④移动到灭火器 → ⑤拿起 → ⑥拔销 → ⑦压把 → ⑧喷射 → ⑨扑灭，`enforceOrder` 禁止跳步（`FireDrillManager.cs`）
- **限时问答门控**：5 题 × 10 秒、60% 通过才允许拔销与喷射，答错给知识点讲解（`ExtinguisherQuizPanel.cs`）
- **粒子累积灭火**：喷射锥内逐帧累积，达阈值 2.5 秒立即清空火焰粒子、灯光归零并上报 ⑨（`Extinguisher.cs` / `FireSource.cs`）
- **两阶段地面箭头**：警报后指向灭火器；阶段一完成后指向大厅，到达 0.6 m 触发结束页（`GuidanceArrows.cs`）
- **桌面鼠标完整操作链**：右键单击灭火器=答题/拿起、左键单击=拔销、左键双击=压把、右键双击=喷射/停止、P=放下、回车/空格=开始、问答 1/2/3（`DesktopDrillInput.cs`）
- **背景音乐与音效**：办公环境音 → 火灾警笛交叉淡入，拾取/拔销/喷射/答对/答错/通关等全套音效（`BackgroundMusic.cs`）
- **抓取手型匹配**：抓取时手部姿态与物体外形匹配（`GrabPoseMatcher.cs`）
- **天花板与吸顶灯**：14×12 米顶板（内表面 3.00 米）+ 4 盏吸顶灯，纯视觉件不带碰撞体，避免挡光（`FireSafetySetup.SetupCeiling()`）
- **断电门控**：`PowerSwitch.cs`（闸柄扳下 / 指示灯转绿 / 标签更新 / 4 米内射线或鼠标点击触发）；`FireSource.extinguishLocked` 让带电火焰吞掉所有喷射进度
- **火焰血量条**：`FireHealthBar.cs`（世界空间画布，读 `progress01`，billboard + 熄灭淡出）
- **流程总控**：`DrillScenario.cs`（`Tutorial / Formal` 两种模式、随机出生点、开始即烟雾、断电解锁、灭火转阶段、`ReleaseHeldExtinguishers()` 切换时放下手持瓶）
- **结束页成绩单**：`EndingScreen.cs`（世界画布朝向玩家正面，显示用时/剩余/答对/火源/步骤）

## VR 头显接入（第 21 轮）

真机头显接上后无需改场景，直接按下面的输入映射操作；无头显时同一套逻辑走「虚拟驱动」通道，可批处理自测。

| 操作 | 桌面（键鼠） | VR（手柄） |
|---|---|---|
| 移动 | W/A/S/D | 左摇杆推杆（`moveSpeed 2.2 m/s`，蹲姿 1.05 m/s） |
| 转向 | 鼠标移动 | 右摇杆瞬转（`snapAngle 45°`，冷却 0.35 s） |
| 抓取灭火器 | 右键单击 | Grip（XRI Select） |
| 拔销 / 压把 | 左键单击 / 双击 | Trigger 第一下拔销、第二下压把并保持 |
| 停止喷射 | 右键双击 | 松开 Trigger |
| 开始 / 翻页 / 继续 | 回车 / 空格 | A 键 |
| 开门（房间门 / 出口门） | 右键单击门 | 空手时 Trigger 指向门 |
| 答题选项 | 数字 1 / 2 / 3 | B / X 键切换，A 确认 |
| 蹲下 / 站起 | Ctrl | 左摇杆按下 |
| 重定位（把脚下设为新原点） | — | 右摇杆按下 |

> 第 29 轮补上了运行环境识别：真机、XR 模拟器、纯桌面三种情况会自动切到对应操作模式，并可用 F2/F3/F4 手动覆盖，详见上文「第 29 轮」。

- **OpenXR 运行时**：`Assets/Editor/VROpenXRConfig.cs` 装配 OpenXRLoader 与 11 项手柄 profile（Oculus Touch / Meta Quest Touch Plus / Vive / Index / 微软 MR 等），批处理入口 `FireSafety.EditorTools.VROpenXRConfig.RunFromBatch`，校验入口 `...VROpenXRConfig.Verify`
- **输入层**：`Assets/Scripts/FireSafety/VRDrillInput.cs`（按键上升沿 + `_triggerConsumed` 锁，避免一按到底连拔销带压把）
- **位移与瞬转**：`XRDrillLocomotion.cs`（CharacterController 位移/重力/蹲姿，`ResolveReferences()` 自动补组件）
- **世界空间 HUD**：`VRDrillHud.cs`（视线下方提示条，模态面板打开时自动让位）+ `HudPlacement.cs`（把面板夹进视野的纯函数）
- **相机层级**：`FireSafetySetup.FindOrCreateRig()` 只认 `XR Origin (Drill)` 直接子物体下的 Camera Offset，并自建 Main Camera，避免移动逻辑挪的是另一套 rig


## 第 37 轮：答题距离提示、结束页入口、三情景路线与窗户排烟

- 问答面板顶部新增「距灭火器 X.X 米」一行，右侧红色「关闭」按钮可临时隐藏提示；重新打开问答时自动恢复。
- 开始按钮文案改为「开始演练（默认：电器起火）」，固定进入电气设备情景；开始界面未点火源不再显示明火。
- 结束页点「关闭」后整块结算画布收起，屏幕右下角保留「重新演练 / 选择情景」小入口，点按后重载场景回到开始界面。
- 纸张起火点迁到西南档案室办公桌，电气（北侧设备柜）、纸张（西南档案桌）、茶水间（东南台面）各有独立出生点与接近路线；纸张 02 号灭火器同步移位。
- 北、西、东三处窗洞加窗框/玻璃/天光板与室内日光补光；每个窗绑定 `WindowSmoke`，只有对应情景火源燃烧时向窗外排烟，火灭即停。

## 第 38 轮：问答面板连续跟随 + 玩家生命值（烟雾 / 烧伤）

### 问答面板连续跟随

- `ExtinguisherQuizPanel` 原来的「移动超过 0.5 米或转头超过约 37° 才瞬移回正前方」改为每帧连续跟随：`FollowCamera(dt)` 用 `1 - exp(-followSpeed * dt)` 做位置 Lerp 与旋转 Slerp，`followSpeed = 12`。
- 目标点仍是相机前 `1.15 米`、视线下 `0.05 米`；打开面板时 `PlaceInFrontOfCamera()` 先即时定位，之后 `KeepInFrontOfCamera()` 每帧平滑跟随。
- 自检与玩家视角探针会直接按固定步长驱动 `FollowCamera`，断言横移 0.3 米 + 转头 45°~50° 后，面板仍距相机 `1.15 米`、中心与相机前向 `dot > 0.96`。

### 生命值系统（`PlayerVitals.cs`）

| 规则 | 参数 / 行为 |
|---|---|
| 初始生命值 | `20 / 20` |
| 烟雾伤害 | 进入任一激活的 `SmokeZone` 后累计，满 `10 秒` 扣 1 点，离开烟雾计时清零 |
| 火源警告 | 进入燃烧火源水平距离 `1.55 米` 内立即扣 1 点，并弹出「离火源太近，已烧伤 -1 生命值；立即后退！」 |
| 火源持续伤害 | 警告后仍停留在原地，每 `2 秒` 再扣 1 点；离开危险半径即停止 |
| 生命值归零 | 进入死亡状态，弹出 `EndingScreen.Show(false, "生命值耗尽")`，结算页显示失败原因与「生命值：0 / 20（耗尽）」 |
| HUD | 世界空间画布跟随头显，位于视线前约 `0.78 米`、左下偏移；显示 `当前 / 上限`、烟雾计时与最近火源距离 |

- `SmokeZone.ContainsPlayer(Vector3)` 用「烟带中心线水平距离 ≤ 1.25 米 + 高度 0.2~烟层 +1.15 米」判定，站立眼高与蹲姿都算在烟雾内；仅 `Active` 时生效。
- 火源距离取玩家相机水平位置到所有「正在燃烧」`FireSource.AimPoint` 的 XZ 最短距离；火灭后自动停止扣血。
- `FireSafetySetup.SetupVrInput` 自动装配 `PlayerVitals` 并绑定主相机；重开场景会重置为 20 点。

### 第 38 轮验收

- 编辑器自检 `selftest38c.log`：`[TEST] ==== 全部通过 ====`，失败项 0；包含 20/20 初始值、烟雾 9.9 秒不扣 / 满 10 秒扣 1 / 离开清零、火源首次 -1 / 1.9 秒不重复 / 满 2 秒再 -1、归零死亡、问答面板跟随距离与朝向断言。
- 玩家视角探针 `round38_vitals8.log`：`结果 DONE，失败数=0`；真实进入 `Stage1Smoke` 站立 10 秒后 HUD `20→19`，进入电气火 `0.86 米` 危险半径后 `20→19` 并显示烫伤警告，原地再停留 2 秒 `19→18`；转头 50° 后问答面板仍距 `1.15 米`、`dot=0.999`。
- 截图：`vr38_start_hud.png`、`vr38_smoke_hp19.png`、`vr38_fire_warning.png`、`vr38_fire_repeat.png`、`vr38_quiz_follow_0.png`、`vr38_quiz_follow_45.png`、`vr38_hud_after.png`（均在 `summary/Logs/shots/`）。
- 测试环境说明：未连接 Quest 2 真机，`XRSettings.isDeviceActive = False`；本轮为「虚拟头显 + 桌面输入 + 玩家相机视角」验收，不等同于真机佩戴测试。

## 目录结构

```
Assets/
├── Scenes/FireSafetyOffice.unity   # 主场景（一键搭建脚本已装配好全部 VR 设施）
├── Scripts/FireSafety/             # 玩法脚本（开始界面/引导页/结束页/倒计时/箭头/问答/灭火/输入）
├── Editor/FireSafetySetup.cs       # 一键搭建（菜单 FireSafety/一键搭建并保存场景）
├── Editor/FireSafetySelfTest.cs    # 自动化自检（400+ 项断言，菜单 FireSafety/自检）
├── Editor/VROpenXRConfig.cs        # OpenXR 运行时与手柄 profile 装配/校验
├── Editor/VRPlayProbe.cs           # 玩家视角验收探针（播放态离屏渲染 15+ 张截图 + 全流程断言）
├── Editor/Round38VitalsProbe.cs     # 第 38 轮玩家视角验收（烟雾/火源扣血 + 问答面板跟随）
└── Resources/Sounds/               # 背景音乐与音效（BGM/ SFX/）

Docs/
├── 01-先行研究调查-既有消防训练程序.md   # 既有消防训练程序（VR/商用/韩国现状）调查
└── 02-本项目差异化与独创性.md           # 与既有程序的逐维对比、独创性主张与边界
```

## 一键搭建与自检

场景已通过 `FireSafety/一键搭建并保存场景` 菜单（或批处理 `-executeMethod FireSafety.EditorTools.FireSafetySetup.RunSetupFromMenu`）完成装配，包括：

- XR Origin（Drill）+ 左右手柄控制器 + XR 交互管理器
- 用户移动（CharacterController + XRDrillLocomotion）
- StartScreen 开始界面（锚定右手手柄正前方；文案可在 StartScreen 组件的 Instructions 字段修改）
- OnboardingScreen 引导页（4 页教学，锚定右手手柄）
- FireAlarm 火灾警报（警笛 + 红色爆闪灯）
- BackgroundMusic 背景音乐切换
- DrillCountdown 倒计时（锚定右手手柄正上方）
- GuidanceArrows 地面引导箭头（灭火器 → 大厅两阶段）
- StageOneCompletePanel 阶段一完成面板
- EndingScreen 结束页（到大厅=成功 / 倒计时耗尽=失败）
- 天花板 + 4 盏吸顶灯（纯视觉，内表面 3.00 米）
- OfficeWalls 四面补墙（4 段 3 米高墙板 + 踢脚线，带 BoxCollider）
- DrillScenario 流程总控 + 三套情景（起火点 / 灭火器 / 题库 / 出生点）
- 出生点 3 组 × 每组 3 个候选（`DrillSpawnPoints/SpawnGroup01..03`）
- DeviceCabinet / DeviceFire / DeviceSmoke / PowerBox（情景一：电气设备起火 + 电源总闸）
- PaperFire / PaperSmoke / Stage1Smoke_Paper（情景二：办公桌纸张起火）
- PantryFire / PantrySmoke / Stage1Smoke_Pantry（情景三：茶水间小家电起火）
- Stage1Smoke 设备旁烟雾（开始即冒烟）与 SmokeZone 撤离通道雾
- PlayerVitals 生命值 HUD（20 点 / 烟雾每 10 秒 -1 / 火源 1.55 米内警告并持续扣血）
- FireHealthBar 火焰血量条（设备火 / 出口火各一条）
- Extinguisher01 / Extinguisher02 / Extinguisher03（抓取/问答/拔销/压把/粒子喷射/手型匹配；分别绑定 CO₂ / ABC 干粉 / 水基湿化学，含瓶身类型标签）
- OutdoorPlaza 知识墙（三块 2.00 × 1.55 m 展板 + 红标题条 + 地面标语）
- EventSystem + XRUIInputModule（手柄射线点按 UI）
- DesktopDrillInput（桌面鼠标完整操作链）

自检（批处理）：`-executeMethod FireSafety.EditorTools.FireSafetySelfTest.RunAll`，成功判据为日志出现 `[TEST] ==== 全部通过 ====`、`error CS` 为 0、退出码为 0。

## 先行研究与差异化

- 既有消防训练程序调查（含 Go Fire、FLAIM、LION BullsEye、ADMS/XVR、国内 VR 仿真等 13 个来源）见
  [`summary/Docs/01-先行研究调查-既有消防训练程序.md`](summary/Docs/01-先行研究调查-既有消防训练程序.md)
- 本项目差异化与独创性论证（含对比表、六项可验证差异、主张边界与待补实验）见
  [`summary/Docs/02-本项目差异化与独创性.md`](summary/Docs/02-本项目差异化与独创性.md)
