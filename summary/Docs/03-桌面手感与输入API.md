# 桌面模式手感与输入 API（第 33 轮）

> 适用：无头显、仅用 PC 键鼠运行 `FireSafetyOffice` 的调试与演示。
> 真机（Quest 2 + Link）仍走手柄 / 摇杆，参数共用同一套手感层。

## 1. 这轮修了什么

| 现象 | 根因 | 处理 |
| --- | --- | --- |
| 没碰鼠标，视角自己慢慢转 | 桌面模式下 `TrackedPoseDriver` 仍按输入系统里的 mock HMD 每帧写相机；`DrillTrackedPose` 在非真机时也可能读 XRHMD | 桌面/模拟器停用 `TrackedPoseDriver`；`DrillTrackedPose` 只有 `RealHeadset` 才接管相机 |
| 鼠标过于灵敏，轻轻一动就转很大 | 灵敏度 0.06 度/像素、未按分辨率归一化；场景序列化值覆盖脚本默认值 | 降到 0.018 / 0.012 度/像素；按 `1080 / Screen.height` 归一化；单帧限幅 40 px |
| 手抖会让画面累积漂移 | 死区只有 0.6 px | 死区提高到 1.5 px，并加 0.05 s 位移低通 |
| 画面会慢慢平移 | XR Origin / 角色控制器会改写相机本地位置 | 桌面模式每帧锁回 Awake 时的相机本地位置 |
| 一推就窜、一松就定 | 加速度 9 / 减速度 14，且松手立即丢方向 | 加速度 4.5、减速度 6；松手后保留最后方向滑行约 0.2~0.4 m；40 ms 卡帧保护 |
| 摇杆小推杆太冲、零漂 | 线性响应、死区偏小 | 径向死区 + 指数曲线 1.55 + 0.06 s 低通（仅真实摇杆，虚拟输入不整形） |

桌面小跑改为 **Shift**；摇杆推满自动小跑只在真机生效，避免桌面"满杆直接 3.19 m/s"。

## 2. 运行时 API

脚本：`Assets/Scripts/FireSafety/DrillInputAPI.cs`（静态门面，找不到组件返回 `false`，不抛异常）。

| 方法 | 作用 |
| --- | --- |
| `GetLook(out LookConfig)` | 读当前视角参数（灵敏度 / 死区 / 限幅 / 平滑 / 参考高度 / 锁位） |
| `SetLook(yaw, pitch)` | 设置水平、垂直灵敏度（度/像素） |
| `SetLookFilter(deadzonePx, clampPx)` | 设置死区与单帧限幅 |
| `SetLookSmoothing(seconds)` | 平滑时间常数，0 = 关闭 |
| `SetLockCameraPosition(bool)` | 桌面是否锁相机本地位置 |
| `GetMove(out MoveConfig)` | 读移动速度 / 转身速度 / 摇杆参数 |
| `SetMove(mps, degPerSec)` | 设置移动与连续转身速度 |
| `SetStick(deadzone, exponent, smoothing)` | 摇杆死区、响应曲线、平滑 |
| `SetDeviceMode("auto/desktop/headset/simulator")` | 强制设备模式，等价 F2/F3/F4 |
| `Recenter()` | 把玩家整体挪回头部位置 |
| `Snapshot()` | 一行诊断，可直接写日志 |

示例：

```csharp
DrillInputAPI.SetLook(0.018f, 0.012f);   // 度/像素
DrillInputAPI.SetLookFilter(1.5f, 40f);  // 像素
DrillInputAPI.SetLookSmoothing(0.05f);   // 秒
DrillInputAPI.SetMove(2.2f, 90f);        // 米/秒，度/秒
DrillInputAPI.SetStick(0.2f, 1.55f, 0.06f);
DrillInputAPI.SetDeviceMode("desktop");
Debug.Log(DrillInputAPI.Snapshot());
```

## 3. 验证结果

- `FireSafety.EditorTools.FireSafetySelfTest.RunAll`：**598 通过 / 0 失败**。
- `FireSafety.EditorTools.DesktopFeelProbe.RunFromBatch`：**FAIL_COUNT=0，PROBE_EXIT=0**。
  - 100 px → 1.80°；60 帧 0.3 px 手抖 → 0.000°；
  - 5000 px 突跳 → 0.72°（被 40 px 限幅）；
  - 静置 1.5 s → 0.000°；相机本地位置偏移 → 锁回 0.000 m；
  - 满杆巡航 2.20 m/s，1 s 位移 1.99 m；松手 0.6 s 滑行 0.17 m。
- 玩家视角截图：`Logs/shots/vr33_feel_look.png`、`vr33_feel_idle.png`（HUD、火焰、灭火器标签均正常）。

## 4. 复现

```text
Unity.exe -batchmode -projectPath D:\43763\VR\summary ^
  -executeMethod FireSafety.EditorTools.FireSafetySetup.RunSetup ^
  -logFile D:\43763\VR\summary\Logs\setup33.log -quit
```

探针（不要加 `-quit` / `-nographics`，需要渲染出图）：

```text
Unity.exe -projectPath D:\43763\VR\summary ^
  -executeMethod FireSafety.EditorTools.DesktopFeelProbe.RunFromBatch ^
  -logFile D:\43763\VR\summary\Logs\feelprobe33.log
```
