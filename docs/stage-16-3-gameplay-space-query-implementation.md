# 阶段 16.3：射击遮挡、出生与玩法空间查询实现记录

状态：**revision 4 代码、自动测试、独立服务器构建、Unity 2021.3 程序集编译和 UDP 版本握手冒烟均已通过，等待用户手动验收。** 手动验收完成前，只能说明“16.3 已实现、待验收”，不能把 16.3 或当前 Tank 碰撞阶段写成完成。

## 一、阶段边界

16.2 已解决 Tank 如何被服务器权威静态地图阻挡。16.3 让战斗和生命周期规则读取同一个空间事实：

```text
TrainingCollisionMap2D revision 4
├─ TankWorldCollision2D：移动阻挡
├─ TankMapQueries2D：静态 Ray / Circle / OBB / 边界查询
├─ GameWorld：射击遮挡、首次出生、重生
└─ UdpNetworkClient：本地即时开火的静态墙距离表现
```

本阶段不实现玩家互相推动、服务器动态刚体、门/平台历史、投射物服务器推进、技能系统、人物 Capsule、斜坡、台阶或骨骼 HitBox。

## 二、共享查询层

新增文件：

- `Assets/Code/TankCollision2D/TankMapQueries2D.cs`
- `Assets/Code/TankCollision2D/TankMapQueries2D.cs.meta`

`TankMapQueries2D` 是不可变 `TankCollisionMap2D` 上的只读查询器：

- `RaycastStatic`：线性扫描当前少量静态 OBB，返回最近命中的 `colliderId`、点、法线和距离；同距离时以较小 `colliderId` 保证稳定选择。
- `OverlapStatic(Obb2D)`：用于 Tank 出生体和以后矩形玩法区域。
- `OverlapStatic(Circle2D)`：用于以后范围技能、视线触发区等圆形查询。
- `IsInsideWorldBounds`：分别支持 OBB 与 Circle，确保整个形状而不只是中心位于场地内。

当前静态物体只有三个，确定性线性扫描比空间树更小、更容易验证。物体规模明显增加后可在不改变查询调用者语义的前提下增加网格或 BVH 宽相。

### Circle 射线入口距离

`Assets/Code/CollisionCore/CollisionQueries2D.cs` 新增 `Raycast(Ray2D, Circle2D)`。旧的玩家命中计算使用圆心在线上的投影距离；该值不是实际表面入口，无法和墙面入口做严格最近命中比较。

revision 4 使用二次方程求射线进入圆面的距离，并覆盖：

- 射线从圆外正面进入；
- 从圆内或圆边开始时距离为 0；
- 相切；
- 背向、超出有限射程和完全错过。

## 三、服务端射击遮挡

修改文件：`Server/Server/GameWorld.cs`。

每个通过冷却和瞄准校验的权威开火按以下顺序裁决：

```text
构造服务器 FireRay
├─ 查询最近静态墙表面距离
└─ 查询命中测试 Tick 的最近历史玩家圆表面距离
       ↓
墙距离 <= 玩家距离：墙优先，玩家不扣血
玩家距离 < 墙距离：保留现有命中、伤害、死亡事件
```

静态墙使用当前 revision 4 地图，不进行历史回退；玩家目标继续使用现有有限历史和最大回退保护。因此高延迟客户端不能通过请求历史命中绕过一直存在的墙。

没有目标但射线撞墙时，可靠 `FireResult` 仍返回 `fired-no-hit`；协议没有新增“墙命中就是伤害命中”的错误语义。服务器 `fireStaticBlocked` 统计用于确认静态遮挡实际发生。

普通 `FireEvent.range` 在射线上存在静态墙时截到墙面，使远端观察者的表现子弹不会继续按完整 35 米寿命飞行。本地所有者已经即时播放开火，不能等服务器往返；它使用同一 revision 4 静态地图从本地炮口表现位置预裁剪生命周期。最终伤害始终以服务器 FireRay 为准。

## 四、首次出生与重生

版本化候选点仍保存在 `TrainingCollisionMap2D`。新增：

- `SpawnCandidateCount`；
- `GetSpawnCandidateRootPosition(playerId, candidateOffset)`。

玩家从与自身 id 对应的首选点开始，按固定顺序环绕查找。每个候选必须同时满足：

1. Tank 扩展 OBB 完整位于世界边界内；
2. 与静态 Collider 没有真实穿透；
3. 与任何存活玩家的 Tank OBB 没有真实穿透。

死亡玩家旧位置不占用出生点；这只是一条出生规则，不代表玩家移动已经互相碰撞。

首次连接没有合法点时：

- `GameWorld.AddPlayer(..., out reason)` 返回 `no-valid-spawn-candidate`；
- UDP 注册立即回滚；
- 客户端收到 `ServerReject`，不会进入一个没有权威玩家的半连接状态。

重生没有合法点时：

- 玩家保持死亡；
- 服务器按约一秒的有界周期重试；
- 不发送虚假的 RespawnEvent，也不生成到墙或其他玩家内部。

遥测包含候选拒绝总数、最终放置失败数和 `outside-world-bounds`、`overlaps-static-collider`、`occupied-by-alive-player` 分类。

## 五、版本与兼容性

- 地图 id：`sample-scene-training-ground`
- 当前版本：`collisionRevision = 4`

静态几何坐标未改变，但客户端即时射击表现开始依赖共享静态查询语义，因此递增契约版本。revision 3 客户端连接 revision 4 服务器会收到 `collision-map-mismatch`，必须重新构建第二客户端。

revision 3 已经完成的 16.2 联网移动验收仍然有效；revision 4 是在其上新增 16.3 查询，不是重新定义 16.2 的完成历史。

## 六、自动验证

新增测试文件：`Server/Server.Tests/GameWorldGameplayQueryTests.cs`。

自动测试覆盖：

- 最近静态 Raycast 返回正确 Collider；
- Circle、OBB 静态重叠和世界边界查询；
- Circle 射线的表面入口、相切、内部、背向和错过；
- 墙后玩家不扣血，不产生 HitEvent，墙截断 FireEvent range；
- 玩家位于墙前时仍正常命中；
- 延迟补偿回看墙后历史玩家时仍被当前静态墙挡住；
- 首选出生点与静态墙重叠时选择下一点；
- 首选出生点被存活玩家占用时选择下一点；
- 所有点无效时明确失败且不把玩家插入世界；
- 重生首选点被占用时选择下一点。

已执行：

```powershell
dotnet run --project Server/Server.Tests/Server.Tests.csproj
dotnet build Server/Server/Server.csproj --nologo -o Temp/Stage16ServerBuildRevision4
dotnet build Temp/Stage16UnityCompile/Stage16UnityCompile.csproj --nologo -p:NoWarn=CS0649
```

结果：纯 C# 测试通过；独立服务器与 Unity 2021.3 程序集编译均为 0 警告、0 错误。UDP 冒烟确认正确 `@4` 返回 Welcome，旧 `@3` 返回 `collision-map-mismatch`。

## 七、用户手动验收

### 准备

1. 退出 Unity Play Mode，等待编译完成并清空 Console。
2. 重启服务器：

```powershell
dotnet run --project Server/Server/Server.csproj
```

3. 服务器启动日志、两个客户端 Console/F3 都必须显示 `sample-scene-training-ground@4`。
4. 重新构建第二客户端；旧 revision 3 Build 必须被拒绝，不能参与本次验收。

### 用例 A：无遮挡命中

- 两辆 Tank 之间保持清晰直线，射手瞄准目标开火。
- 目标应扣血并显示命中反馈。
- 射手可靠结果应为 `fired-hit` 并带目标玩家 id。

### 用例 B：静态墙遮挡

- 让目标 Tank 完全位于现有 Cube/倾斜实心障碍后方，使射手到目标中心的直线先穿过障碍。
- 连续开火，目标血量不得下降，不得播放玩家命中反馈。
- 射手可靠结果应为 `fired-no-hit`；服务器下一次遥测的 `fireStaticBlocked` 应增长。
- 远端观察者的表现子弹应在墙附近结束，不能继续飞完整射程。所有者的即时表现允许因本地炮口与服务器简化炮口原点不同而有很小的视觉距离差，但不能影响扣血结论。

### 用例 C：目标在墙前

- 排列成“射手 -> 目标 -> 墙”。
- 目标必须正常受伤，不能因为射线上更远处还有墙而被错误挡掉。

### 用例 D：延迟补偿与弱网

- 分别开启约 100 ms、200 ms 延迟和项目已有少量抖动/丢包。
- 重复墙前命中、墙后不命中。
- 弱网只能改变确认与表现到达时间，不能让历史玩家被静态墙后的射线命中。

### 用例 E：出生点占用

- 普通双客户端连接时，两名玩家不得出生在墙内、场外或完全重叠。
- 可选加强验证：只连接玩家 1，将其权威位置移动到玩家 2 的首选点 `(4, 0)` 附近，再启动玩家 2；玩家 2 应选择后续合法候选，而不是叠在玩家 1 内。服务器 `spawnRejected` 和 `occupied-by-alive-player` 应增长。
- 死亡重生时若首选点被存活玩家占据，应在后续合法候选重生；不得在占用者内部出现。

### 合格标准

以下条件全部满足才可将 16.3 标为完成：

- `@4` 双客户端连接，旧版本明确拒绝；
- 无遮挡和“目标在墙前”保持原有权威伤害；
- 墙后目标在正常网络与 100/200 ms 弱网下均不扣血；
- 两个客户端看到相同伤害、死亡与重生结论；
- 出生/重生不进入场外、静态墙或存活玩家；
- Unity 无新增脚本异常，服务器空间查询遥测与实际操作对应。

## 八、已知边界

- 玩家命中仍是二维根 Circle，不是 Tank OBB 伤害体，也不是骨骼 HitBox。
- 静态地图不回退；以后若加入会移动且能挡枪的门/平台，必须保存其历史或明确不参与延迟补偿遮挡。
- 网络武器仍是即时 hitscan，客户端炮弹只是表现；尚未实现服务器权威 projectile 飞行时间。
- 服务器炮口仍由权威 Tank 根位置、固定前伸和固定高度推导，本地真实炮管原点可能有小幅表现差异。
- 玩家移动仍不互相阻挡，出生占用校验不能被表述为玩家对玩家碰撞已完成。
- `SweepCircle/Capsule` 只保留后续冲刺/人物 3C 的查询语义，本阶段没有制造未被玩法调用的空实现。
- 16.4 人物斜坡和 16.5 骨骼 HitBox 仍未开启。
