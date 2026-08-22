# 阶段 16.1：Tank 二维静态碰撞实现记录

状态：**已实现并通过纯 C# 自动测试、服务器构建和 Unity 2021.3 程序集编译验证；尚未接入 `GameWorld`、客户端预测或未确认输入重放。**

这表示 Tank 静态碰撞已经能在离线/纯模拟层得到实际受阻后的位姿，但不能写成联网移动碰撞已经完成。联网闭环与双客户端验收属于 16.2。

## 一、本阶段完成范围

- Tank 使用 X/Z 平面的二维 OBB，静态墙体使用 AABB/OBB。
- 静态碰撞体启动校验：正数 ID、唯一 ID、正尺寸、不能越出世界边界。
- OBB 外包 AABB 宽相；只有宽相可能重叠时才执行 SAT 窄相。
- 候选旋转检查；旋转后的 OBB 穿墙或越界时拒绝本次旋转。
- 过长位移拆成有限子步，降低 30 Hz、7 m/s 基线下穿过薄墙的风险。
- 每个子步按最深穿透推出，保留切向位移形成滑墙。
- 碰撞迭代和移动子步都有明确上限；异常墙角无法在预算内解开时回退到上一合法子步。
- 世界边界与静态墙体使用同一“最终位姿必须合法”的结果语义。
- 提供不修改 Scene/Prefab 的可选 Unity 手动演示组件。

## 二、文件与依赖边界

### 纯 C# 地图与移动层

```text
Assets/Code/TankCollision2D/
├─ TankCollisionMap2D.cs
└─ TankWorldCollision2D.cs
```

主要类型：

| 类型 | 职责 |
| --- | --- |
| `StaticCollider2D` | 保存正数 ID、静态 OBB 和预计算外包 AABB |
| `TankCollisionMap2D` | 保存世界边界和经过校验的只读静态碰撞体列表 |
| `TankPose2D` | 保存 X/Z 平面位置与 gameplay yaw |
| `TankCollisionSettings2D` | 集中保存 Tank 半尺寸、skin、子步与迭代预算 |
| `TankMoveResult2D` | 返回请求/实际位移、最终位姿、阻挡和安全上限诊断 |
| `TankWorldCollision2D` | 执行旋转候选检查、子步移动、宽相、SAT、推出和滑墙 |

这些文件只依赖 `System` 和 16.0 的 `CalabiYau.CollisionCore`，不引用：

- `UnityEngine`、Rigidbody 或 Collider；
- UDP、JSON 和网络消息；
- `GameWorld`、`UdpNetworkClient`、`TankMotor` 或 `NetworkTankAvatar`。

`Server/Server.csproj` 与 `Server/Server.Tests.csproj` 明确引用同一份源码。后续 16.2 接入服务端和客户端时，不应复制另一套碰撞公式。

### Unity 手动演示适配器

```text
Assets/Code/CollisionDebug/TankCollision2DPlayground.cs
```

该组件只把 Unity Inspector、键盘和 Transform 适配到纯 C# 求解器。它没有自动挂到现有 Tank，没有修改 Scene/Prefab，也没有重新开启 Rigidbody 联网碰撞。

### 自动测试

```text
Server/Server.Tests/TankWorldCollision2DTests.cs
Server/Server.Tests/Program.cs
```

沿用项目现有的无第三方测试框架控制台入口，与 CollisionCore 和原有 GameWorld/复制/可靠事件测试一起回归。

## 三、坐标和旋转约定

```text
Vec2D.X <-> Unity world X
Vec2D.Y <-> Unity world Z
gameplay yaw 0° -> forward +Z
gameplay yaw +90° -> forward +X
```

CollisionCore 的二维角度使用数学上的逆时针 X/Y，Unity gameplay yaw 在 X/Z 平面表现为顺时针，因此创建 Tank/地图 OBB 时显式使用 `-gameplayYawRadians`。OBB 的本地 Y 轴保持为 Tank 前方。

这个转换已由自动测试固定，避免 16.2 接入时服务器和 Unity 出现镜像旋转。

## 四、移动求解原理

每次 `Move(start, forwardDistance, desiredYawDelta)` 按以下顺序执行：

```text
校验起始位姿
-> 检查期望旋转后的 OBB
-> 穿透则保留旧 yaw，否则接受新 yaw
-> 按已接受 yaw 计算前向位移
-> 按 maxSubstepDistance 拆分有限子步
-> 每个子步计算候选终点
-> Tank 外包 AABB 对静态外包 AABB 做宽相
-> 对候选静态墙体做 OBB SAT 窄相
-> 同时检查 Tank 外包 AABB 是否越出世界边界
-> 沿最深穿透法线推出 depth + epsilon
-> 在迭代预算内继续消除其他穿透
-> 仍不合法则回退到上一合法子步并停止
```

### 为什么会滑墙

斜向位移到达墙内时，推出只修正墙体法线方向，候选终点的切线坐标不变。因此法向运动被抵消，切向运动被保留，形成滑墙。当前实现不需要额外引入 Rigidbody 摩擦或速度求解器。

### skin 与 epsilon

- `skinWidth` 扩大 Tank 的 OBB，是可调玩法安全间隙。
- `epsilon` 只处理浮点接触误差。
- 推出使用 `depth + epsilon`，避免刚好停在数值穿透边缘后逐 Tick 渗入。
- 不能用放大 epsilon 代替 skin。

### 两层安全预算

- `MaxMovementSubsteps` 防止异常大位移造成无限循环或单 Tick 计算尖峰。预算不足时只处理安全距离，并在结果中设置 `ReachedSubstepLimit`。
- `MaxCollisionIterations` 防止病态墙角反复推出。预算耗尽且候选位姿仍不合法时回退上一合法子步，并设置 `ReachedCollisionIterationLimit`。

## 五、集中配置

当前自动测试与演示基线使用：

```text
Tank half extents       = (0.5, 0.75)
skinWidth               = 0.05
maxSubstepDistance      = 0.05
maxMovementSubsteps     = 128
maxCollisionIterations  = 8
movement baseline       = 7 m/s at 30 Hz
```

这些是 16.1 的可测试基线，不是最终美术尺寸。16.2 接入时服务器和客户端必须从同一权威配置/地图版本加载，不能在 Inspector 与服务器各自维护不同数值。

## 六、自动测试覆盖与结果

`TankWorldCollision2DTests` 当前包含 13 组测试：

- 旋转 OBB 的外包 AABB 包含全部角点；
- 地图拒绝重复 ID、非法 ID 和越界静态碰撞体；
- gameplay yaw 与当前 Tank 前向约定一致；
- 正面撞墙在墙前停止；
- 斜向撞墙保留切向位移；
- 连续 120 Tick、30 Hz、7 m/s 顶墙不漂移、不穿透；
- 双墙角在迭代预算内稳定停止；
- 靠墙旋转造成穿透时被拒绝；
- 空旷位置旋转被接受；
- 窄通道允许纵向位姿，但拒绝横向转身越界；
- 世界边界阻挡法向移动并保留切向移动；
- 30 Hz、7 m/s 连续移动不能跨过厚度 0.05 的薄墙；
- 异常大位移受子步预算限制并返回明确诊断。

验证命令：

```powershell
dotnet build Server/Server/Server.csproj --nologo -o Temp/Stage16ServerBuild
dotnet run --project Server/Server.Tests/Server.Tests.csproj
dotnet build Temp/Stage16UnityCompile/Stage16UnityCompile.csproj --nologo
```

阶段完成时：服务器构建 0 警告、0 错误；全部自动测试通过；`TankCollision2DPlayground` 连同纯 C# 依赖使用 Unity 2021.3.45f2c1 的 `UnityEngine` 程序集编译为 0 警告、0 错误。

## 七、Unity 手动验收

本验收只验证 16.1 离线移动求解，不启动服务器，不代表 16.2 联网通过。

1. 在临时测试场景创建一个可见 Cube，命名为 `Stage16_1_PlaygroundTank`；建议移除它的 BoxCollider，且不要添加动态 Rigidbody。
2. 将位置设为 `(0, 0.5, 0)`、Y 旋转设为 `0`，挂载 `TankCollision2DPlayground`。
3. 保持默认参数，打开 Scene 视图的 Gizmos 并选中该物体。白线是世界边界，青线是纯 C# 静态墙，绿线是带 skin 的 Tank OBB。
4. 进入 Play Mode，用上/下方向键前后移动、左/右方向键转向，`R` 回到初始位姿。
5. 正对青色横墙持续按上方向键：Tank 应停在墙前；运行时 `Last Move Was Blocked` 为真，物体不能继续穿过。
6. 斜向接近墙体并持续前进：靠墙方向停止，沿墙方向继续移动。
7. 在墙前持续按住上方向键：位置不应逐 Tick 渗入或来回明显抖动，`Reached Safety Limit` 应保持为假。
8. 让长边靠近墙后尝试转身：会造成 OBB 穿透的旋转应被拒绝，`Last Rotation Was Blocked` 为真。

验收后可直接删除这个临时物体；核心实现与自动测试不依赖它。

## 八、明确未完成与已知边界

16.1 **没有**完成：

- `GameWorld.SimulatePlayer()` 的服务端权威地图阻挡；
- `UdpNetworkClient` 即时预测和未确认输入重放的碰撞；
- 地图 `mapId + collisionRevision` 握手与不一致拒绝；
- 双客户端正常网络/100--200 ms 弱网验收；
- 射击墙体遮挡、出生点占用和重生校验；
- 玩家对玩家、动态刚体或可推动物体；
- 三维 Capsule、斜坡、台阶、贴地、跳跃、墙跑或攀爬；
- 动画骨骼 HitBox；
- 通用 Sweep/Cast 或完整物理引擎。

当前静态碰撞体数量很少，宽相仍为确定性线性扫描，没有网格、四叉树或 BVH。旋转只检查本 Tick 的最终候选角度，没有做旋转 Sweep；在固定 Tick 和受限转速下满足当前 Tank 基线，未来高速/长形角色需要更细的旋转子步或连续查询。

地图目前是经过校验的内存只读清单；Unity 演示器手工填写同样的数据。16.2 必须补上唯一权威地图配置、版本确认，并把同一求解器接入服务端推进、客户端即时预测和输入重放。完成双客户端验收前，不能称为联网碰撞能力。
