# 阶段 16.2：服务器权威与客户端预测碰撞闭环实现记录

状态：**已完成。** revision 2 修复了本地动态 Rigidbody 与静态权威地图冲突，revision 3 继续修复客户端50Hz即时预测与服务器30Hz权威推进在障碍附近产生不同路径的问题。纯 C# 自动测试、独立服务器构建和 Unity 2021.3 程序集编译均已通过；用户已于 2026-08-22 完成 revision 3 的单客户端、双客户端、正常网络及 100--200 ms 弱网验收，确认两端障碍结果一致、最终收敛且不穿墙。

## 一、本阶段解决了什么

16.1 的求解器已经能计算“一个二维 Tank OBB 应该停在哪里”，但没有任何真实联网对象调用它。16.2 把同一条纯 C# 命令模拟链路接到了三个必须一致的位置：

```text
客户端当前输入
├─ TankMotor：本地即时预测
├─ UdpNetworkClient：权威快照后的未确认输入重放
└─ GameWorld：服务器固定 Tick 最终裁决
         ↓
TankCommandSimulation2D
         ↓
TrainingCollisionMap2D + TankWorldCollision2D
```

联网模式仍保持 Rigidbody kinematic。阻挡结果来自共享的纯 C# 碰撞求解，不是重新开启客户端 PhysX 后得到的本地假象。

## 二、共享碰撞地图与 Tank 形状

### 版本

- `mapId`：`sample-scene-training-ground`
- `collisionRevision`：`3`
- 唯一配置入口：`Assets/Code/TankCollision2D/TrainingCollisionMap2D.cs`

客户端与独立 .NET 服务器都直接编译这份源码，避免在 Inspector 和服务端各自维护一套尺寸。

### 当前 SampleScene 映射

| Unity 对象 | 二维权威表示 | 中心 X/Z | 半尺寸 X/Z | 当前语义 |
| --- | --- | --- | --- | --- |
| `Cube` | AABB | `(-9.3, 3.57)` | `(0.5851, 0.5)` | 静态障碍 |
| `Cube (1)` | 保守投影旋转 OBB | `(4.63, 7.63)` | `(2.0163, 1.5487)`，yaw 约 `78.31°` | 倾斜方块按俯视实心障碍处理；比 revision 1 的大 AABB 少空角 |
| `Cube (2)` | AABB | `(14.53, -1.61)` | `(0.5, 0.5)` | 联网时复位并冻结为服务端固定静态障碍；离线恢复原 Rigidbody |

世界边界来自 50 x 50 的地面，二维半尺寸为 `(25, 25)`。

权威 Tank 形状匹配当前 Tank Body 子物体的启用 BoxCollider：半尺寸 `(1.43, 2.18)`，相对 Tank 根节点的局部中心偏移为 `(-1, 0.92)`。中心偏移会随车身朝向旋转；炮塔和炮管属于表现，不扩大移动碰撞体。

倾斜方块目前**不能攀爬**。它在二维阶段只是一个保守的实心阻挡；Capsule、地面法线、斜坡、台阶、跳跃和墙跑属于实际人物 3C 出现后的 16.4。

## 三、实际接入点

### 1. 共享单步模拟

`TankCommandSimulation2D.Simulate()` 统一完成：

1. 校验并夹紧移动/转向输入；
2. 按固定时间步计算转角和前进距离；
3. 调用 `TankWorldCollision2D` 做旋转拒绝、有限子步、SAT 推出和滑墙；
4. 返回碰撞后的根节点位置与朝向。

这样服务端、即时预测和回放不会各自复制一份“先转再走”的公式。

### 2. 服务端最终裁决

`Server/Server/GameWorld.cs` 的 `SimulatePlayer()` 不再无条件累加 X/Z。每个固定 Tick 都从当前权威位姿调用共享单步模拟，再把合法结果写回玩家状态。

新增只读诊断计数：

- `BlockedMovementTickCount`
- `BlockedRotationTickCount`
- `CollisionResolutionCount`

服务器周期统计会输出 `collisionBlocked`、`rotationBlocked` 和 `collisionResolutions`，可用于确认实际 Tank 正在触发权威阻挡。

### 3. 客户端即时预测

`TankMotor.ApplyMovementAndRotation()` 在联网模式下用同一地图和命令模拟计算下一位姿，再通过 kinematic Rigidbody 的 `MovePosition/MoveRotation` 显示结果。单机路径继续使用原有 Rigidbody 行为，不改变离线控制语义。

### 4. 权威校正与输入重放

`UdpNetworkClient.ReplayBufferedInput()` 以及当前输入的部分 Tick 重放均经过同一碰撞单步。收到服务器快照后，客户端从权威位姿起算，再重放未确认输入；不会出现“即时预测停住，但重放结果又穿墙”的第二条路径。

### 5. 地图版本握手

`ClientHello` 上报 `mapId + collisionRevision`；`ServerWelcome` 回显服务器版本。版本不一致时服务器在注册玩家前返回：

```json
{"type":"ServerReject","reason":"collision-map-mismatch","expectedMapId":"sample-scene-training-ground","expectedCollisionRevision":3}
```

客户端收到拒绝后停止联网本地控制、关闭套接字并显示明确原因；即使异常服务器错误发送 Welcome，客户端仍会再次检查版本。

## 四、自动验证

### 纯 C# 测试

```powershell
dotnet run --project Server/Server.Tests/Server.Tests.csproj
```

新增覆盖包括：

- Tank 局部中心偏移在 0° 与 90° 时正确旋转；
- 120 Tick 持续前进不能穿过权威墙体；
- 斜向接触保留切向滑动；
- 靠墙旋转会被权威拒绝；
- 默认训练地图的倾斜方块投影会阻挡；
- 所有预设出生根位置与静态地图/边界相容；
- Hello/Welcome/Reject 的 JSON 字段和版本语义正确。

结果：全部通过。

### 构建与程序集编译

```powershell
dotnet build Server/Server/Server.csproj --nologo -o Temp/Stage16ServerBuildFinal
dotnet build Temp/Stage16UnityCompile/Stage16UnityCompile.csproj --nologo -p:NoWarn=CS0649
```

结果：独立服务器 0 警告、0 错误；受影响 Unity 脚本使用 Unity 2021.3 程序集编译为 0 警告、0 错误。`CS0649` 只在独立编译器中抑制无法识别 Inspector 序列化赋值的假阳性。

### 真实 UDP 冒烟

- 正确版本 Hello 收到带相同地图版本的 `ServerWelcome`；
- 错误版本 Hello 收到明确 `ServerReject`；
- Windows UDP 在测试客户端关闭后产生的 `ConnectionReset` 被作为单个数据报错误记录并继续接收，服务器不再退出；
- 冒烟结束后服务器可正常停止。

## 五、第一次双客户端验收暴露的问题与修复

### 现象与根因

用户第一次双客户端验收发现：子弹或 Tank 推动场景物块后，两端物块位置、方向和是否出界不一致；Tank 回滚不包含物块；物块移走后 Tank 仍可能在原位置“撞空气”或转向被拒绝。

核对确认 `SampleScene/Cube (2)` 同时具有两个互相冲突的身份：

- Unity Scene 中是 `isKinematic = false` 的本地动态 Rigidbody；
- `TrainingCollisionMap2D` 中又是永远留在初始位置的服务端静态 Collider 3。

联网子弹也是各客户端独立生成的带重力 Rigidbody 和实体 Collider。服务器快照没有动态物块状态，Tank 的预测/回放也不拥有物块状态，因此两个 PhysX 世界必然分叉。可见物块被本地推走后，共享二维地图仍在初始位置阻挡，形成了可以复现的“空气墙”；旋转候选 OBB 与该不可见阻挡重叠时也会被正确但令人困惑地拒绝。

### revision 2 路线一修复

- 新增 `NetworkStaticMapBody2D`，只挂到明确属于权威静态地图、但离线仍保留 Rigidbody 行为的 `Cube (2)`。
- 收到正确服务器 Welcome 后，该组件把物块复位到 Scene 编写位置、清零速度、关闭重力并设为 kinematic；退出联网或连接被拒绝时复位并恢复 Scene 原有 Rigidbody 设置。
- 联网子弹继续按服务器射线起点、方向和距离播放，但会禁用自身及子物体 Collider、`detectCollisions` 和重力；它不能再给本地物块施加冲量。离线开火仍保留原有物理子弹。
- `Cube (1)` 从 revision 1 的轴对齐投影 AABB 改为包围真实 X/Z 投影的较紧旋转 OBB，减少可见斜坡四角附近的提前阻挡。
- 地图版本递增为 `sample-scene-training-ground@2`；旧 `@1` 客户端会被服务器明确拒绝。

这是一项明确的阶段性分类，不是动态物体同步。可推动物块的服务器位置、速度、冲量、快照、插值和预测仍属于以后单独设计的权威动态实体阶段。

### revision 3 预测/权威时间步修复

第二次双客户端复验发现：本地玩家可以流畅转向并擦过障碍，但远端观察者看到该 Tank 被同一障碍持续阻挡；本地停止输入后才被权威快照拉回远端所见位置。

根因不是远端插值，而是同一碰撞公式使用了不同外层时间步：

- Unity `Fixed Timestep = 0.02`，`TankMotor` 即时预测以 50 Hz 推进；
- 服务端、输入发送和未确认输入回放以 30 Hz 推进；
- 180°/s、7m/s 基线下，客户端每外层步约转 `3.6°`、走 `0.14m`，服务端每步约转 `6°`、走 `0.233m`；
- 转向候选是否合法、何时接触墙面和何时开始滑动都是非线性离散决策。多个小转角可能逐个通过，而一个大转角会被拒绝，因此“使用同一类和同一地图”仍不足以保证相同路径。

revision 3 将 `TankCommandSimulation2D` 的转向与移动统一拆为最大 1/150 秒的命令微步。50 Hz 每次正好执行3个微步，30 Hz 每次正好执行5个微步；服务端、即时预测与输入回放因此在恒定输入下经过相同微步序列。自动测试新增：

- 开放空间持续转向移动时，30 Hz 与50 Hz最终位置/朝向一致；
- 实际接触有限墙体、推出并滑动后，30 Hz 与50 Hz最终位置/朝向一致，且测试确认两条路径确实发生过阻挡；
- 靠墙旋转允许逐个接受仍合法的微小角度，但必须在 OBB 即将插墙前停止。

作为第二道权威保护，大于硬修正阈值的误差现在即使玩家仍按住移动键也会立即应用；持续输入只允许延后较小的表现校正，不能再长期维持墙体两侧的不同结果。由于碰撞语义改变，版本递增为 `sample-scene-training-ground@3`，旧 `@2` 客户端必须拒绝。

## 六、用户双客户端复验

### 准备

1. 删除 16.1 的临时 Playground 方块是安全的；16.2 不依赖它，也不需要再看粉色/青色投影线。
2. 退出 Unity Play Mode，等待脚本刷新完成，清空 Console。若 Unity 显示编译错误，先不要继续验收，保留第一条错误。
3. 必须重启服务器，旧进程不包含碰撞地图握手：

```powershell
dotnet run --project Server/Server/Server.csproj
```

4. 服务器启动日志应显示：`Collision map: sample-scene-training-ground@3`。
5. 重新构建第二客户端；旧 revision 2 Build 不能参与本次复验。两个客户端 Console 与 F3 的 `Collision map` 都应显示 `sample-scene-training-ground@3`。

### 用例 A：单客户端实际 Tank

用场景里真正的 Tank 驶向三个可见方块，而不是创建测试 Cube：

- 正面持续前进：Tank 车体应停在方块前，不能穿过去；
- 斜向顶住：朝墙内的分量停止，沿墙方向仍可移动；
- 持续顶墙 5 秒：不应逐帧渗入、明显来回抖动或被权威快照拉到墙后；
- 紧贴墙转向：会让长方形车身插墙的旋转应暂时被拒绝；离墙后恢复；
- 倾斜方块：当前应被当作实心障碍，不能把它当斜坡开上去。
- `Cube (2)`：连接成功后应回到初始位置并保持不动；Tank 和联网子弹都不能再把它推走。选中它时，运行期 Rigidbody 的 `Is Kinematic` 应为真。
- 联网子弹是射击反馈，不应撞飞或改变任何场景 Rigidbody；其真实命中仍由服务端权威 hitscan 决定。

同时观察服务器统计：发生接触后 `collisionBlocked` 或 `collisionResolutions` 应增长。这证明阻挡确实在服务端权威世界发生，而不只是客户端画面停住。

### 用例 B：双客户端一致性

1. 再启动 Windows Build 或第二个 Unity 客户端，确认两个客户端都显示同一地图版本。
2. 分别操作两个本地 Tank 撞向障碍：当前操作端应立即停住；另一端看到的远端 Tank 也应停在同一侧，不能穿墙后又弹回。
3. 两个客户端互换操作和观察角色，重复正撞、斜滑和靠墙转向。
4. 观察 F3：停止输入后预测误差应回到接近 0；不能持续出现大幅硬校正或固定频率拉扯。
5. 两端反复向 `Cube (2)` 开火或顶住它：两端都应看到它保持在同一初始位置，不能再出现一端出界、一端留场或隔空推动。
6. 重点复现“持续按住前进和转向、从障碍边缘擦过”：本地玩家和远端观察者必须都通过或都被阻挡，不能一个继续走、另一个长期停在墙前。

### 用例 C：弱网

按现有弱网开关分别测试约 100 ms 和 200 ms 延迟，并保留项目原有的少量抖动/丢包设置：

- 本地 Tank 仍应即时在墙前停住；
- 远端显示允许延迟和平滑，但最终位置必须与权威结果收敛；
- 不能因输入重放穿墙、瞬移到墙后或持续在墙面两侧来回校正。

### 合格预期

只有以下条件全部满足，才可把 16.2 改为“完成”：

- Unity 无新增脚本异常；
- 单客户端三个现有障碍的正撞、斜滑、持续顶墙和靠墙转向均符合预期；
- 服务端碰撞统计确实增长；
- 双客户端看到相同的权威阻挡结果；
- revision 3 中 `Cube (2)` 在联网模式下始终固定，联网子弹不会推动任何本地物块；
- 同一持续输入擦过障碍时，本地50Hz预测与远端30Hz权威轨迹不再分叉；
- 100--200 ms 弱网下本地预测及时、最终收敛且不穿墙；
- 客户端和服务器地图版本一致，错误版本拒绝已有自动测试与 UDP 冒烟支撑。

若 Tank 在可见模型前过早停止、穿入模型、左右镜像或某个方块完全不阻挡，应记录 Tank 根节点坐标、方块名称、客户端截图和服务器统计；不要把它判为通过。此时应修正共享地图尺寸或 Tank 根节点偏移，并递增 `collisionRevision`。

### 实际验收结论（2026-08-22）

- revision 3 的单客户端正撞、斜滑、持续顶墙和靠墙转向通过；
- 双客户端对同一障碍的通过/阻挡结果一致，不再出现所有者擦过而远端长期停墙；
- `Cube (2)` 联网时保持静态，联网表现子弹不再推动本地 Rigidbody 物块；
- 100 ms 与 200 ms 弱网下本地预测仍及时，权威结果能够收敛且没有穿墙；
- 用户确认全部验收项通过，因此从本记录起可正式说明“16.2 联网权威移动碰撞已完成”。

后续 16.3 将共享契约递增为 revision 4，用于静态射击遮挡和出生玩法查询。这不撤销 revision 3 的 16.2 验收结论；revision 3 是联网移动碰撞完成时的验收基线。

## 七、明确未完成

16.2 仍不包含：

- 射击射线的墙体遮挡、出生点动态占用和重生查询（16.3）；
- 玩家对玩家阻挡、动态刚体、推动或堆叠；
- 三维地面、人物 Capsule、斜坡/台阶、跳跃、攀爬或墙跑（16.4）；
- 动画骨骼 HitBox（16.5）；
- 通用连续旋转 Sweep 或完整刚体物理引擎。

当前静态物体很少，宽相仍使用确定性线性扫描。地图数据是共享、版本化的纯 C# 清单，但尚未实现从 Unity Scene 自动导出；每次修改 Scene 碰撞布局都必须同步修改该清单、自动测试并递增版本。
