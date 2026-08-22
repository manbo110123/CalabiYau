# 阶段 16.0：CollisionCore 二维碰撞数学实现记录

状态：**已实现并通过自动测试与 Unity 脚本编译验证；尚未接入 Tank 移动、服务器地图或联网预测。**

## 一、本阶段完成范围

- 纯 C# `Vec2D`：点积、长度、左法线、带 epsilon 的安全归一化和向量投影。
- 基础形状：`Aabb2D`、`Obb2D`、`Circle2D`、单位方向 `Ray2D`。
- 投影区间：`ProjectionInterval2D`。
- 重叠结果：`OverlapResult2D`，包含命中、从第二形状推出第一形状的法线和穿透深度。
- 射线结果：`RaycastResult2D`，包含命中点、法线、距离和 `StartedInside`。
- `AABB vs AABB`。
- `Circle vs AABB/OBB`，以及反向参数顺序的法线语义。
- `AABB/OBB vs OBB` 的二维 SAT。
- 有限 `Ray/Segment vs AABB/OBB` slab 查询。
- 不修改 Scene/Prefab 的可选 Unity Gizmo 调试组件。

## 二、文件与依赖边界

### 纯数学核心

```text
Assets/Code/CollisionCore/
├─ CollisionMath2D.cs
├─ CollisionShapes2D.cs
├─ CollisionResults2D.cs
└─ CollisionQueries2D.cs
```

这些文件只依赖 `System`，不引用以下内容：

- `UnityEngine`、`Vector3`、`Rigidbody`、`Collider`、GameObject；
- UDP、JSON 或任何网络消息；
- `GameWorld`、玩家状态、地图对象或表现脚本。

Unity 直接编译 `Assets` 中的唯一源码；`Server/Server.csproj` 和 `Server/Server.Tests.csproj` 通过明确的 `Compile Include` 引用相同文件。因此客户端和服务器以后可以复用一份公式，而不是维护两份镜像代码。

### Unity 调试适配器

```text
Assets/Code/CollisionDebug/CollisionCore2DGizmo.cs
```

该文件负责 `UnityEngine.Vector2/Vector3` 与 `Vec2D` 的显示转换，不进入服务器工程，也不反向污染 CollisionCore。

### 自动测试

```text
Server/Server.Tests/CollisionCoreTests.cs
Server/Server.Tests/Program.cs
```

沿用现有无第三方测试框架的控制台测试入口。CollisionCore 测试与原有 GameWorld、复制、可靠事件测试在同一命令中回归。

## 三、关键语义

### X/Y 与 Unity X/Z

CollisionCore 使用通用二维 `X/Y`。当前 Tank Adapter 约定：

```text
Vec2D.X <-> Unity world X
Vec2D.Y <-> Unity world Z
```

类型全部显式带 `2D` 后缀，避免未来人物三维 Capsule、斜坡和墙跑误用二维接口。

### 接触和法线

- 刚好接触视为 `Hit = true`、`PenetrationDepth = 0`。
- 小于等于 epsilon 的间隙视为数值接触；超过 epsilon 才判定分离。
- `Overlap(first, second)` 的法线表示把 `first` 推出 `second` 的方向。
- 非完全对称情形交换 first/second 后，深度相同、法线反向。
- 完全同心且对称的形状没有唯一几何法线；实现选择确定性的首个最小轴，不赋予它唯一物理含义。

### 穿透深度

包含情形不能使用简单的区间交叠长度。例如小盒完全位于大盒内部时，返回值必须是小盒真正移出大盒所需的最短距离。当前 SAT 会分别计算沿轴正、负方向推出第一形状的距离，再选择更短方向。

### epsilon 与 skin width

`CollisionMath2D.DefaultEpsilon` 只处理浮点误差，不是玩法间隙。阶段 16.1 的 Tank `skinWidth`、最大子步和碰撞迭代次数属于移动层配置，不能用增大 epsilon 替代。

### Ray 内部起点

Ray 从形状内部或边界开始时立即返回：

```text
Hit = true
Distance = 0
Point = Origin
StartedInside = true
Normal = -Direction
```

该法线是保守查询语义，不声称是内部点对应的唯一表面法线。这样未来射击起点进入墙体时会立即被静态障碍截断。

## 四、SAT 原理与调试信息

二维 OBB 只需要检查四条候选轴：

```text
first.AxisX
first.AxisY
second.AxisX
second.AxisY
```

每条轴上分别投影两个 OBB：

```text
任一轴区间分离 -> 两个 OBB 不碰撞，该轴是分离轴
四条轴都重叠 -> 两个 OBB 碰撞
退出距离最小的轴 -> 最小穿透轴和推出法线
```

`CollisionQueries2D.Project()` 和 `AreSeparated()` 是公开、无状态查询，自动测试和 Gizmo 使用同一投影/容差语义。

## 五、自动测试覆盖

CollisionCore 当前有 18 组测试方法，覆盖：

- 点积、投影、垂直法线、单位轴与近零向量拒绝；
- 非法尺寸、零尺寸退化形状和单位 Ray；
- AABB 分离、接触、重叠、包含、负坐标和极小尺寸；
- Circle/AABB/OBB 切点、穿透、内部包含、旋转最近点和参数反向；
- OBB SAT 分离轴、旋转、接触、包含、AABB 兼容和 epsilon 间隙；
- Ray 外部、内部、边缘、平行、背向、最大距离、旋转 OBB；
- Segment 和负坐标极小几何。

验证命令：

```powershell
dotnet build Server/Server/Server.csproj --nologo
dotnet run --project Server/Server.Tests/Server.Tests.csproj
```

阶段完成时结果：服务端构建 0 警告、0 错误；CollisionCore 与原有 GameWorld 测试全部通过。Unity 2021.3.45f2c1 完整脚本域重载后，C# 编译错误与 Tundra 编译失败均为 0。

## 六、Gizmo 手动查看

本阶段没有自动修改 Scene 或 Prefab。需要观察算法时，可在测试场景手动创建空物体并添加 `CollisionCore2DGizmo`：

1. 打开 Scene 视图的 Gizmos。
2. 调整两个 OBB 的中心、半尺寸和 Yaw。
3. 青色为第一个 OBB；第二个 OBB 分离时为绿色、碰撞时为红色。
4. 四组投影线对应四条 SAT 候选轴；第二个区间为黄色时，该轴是分离轴。
5. 红色箭头显示把第一个 OBB 推出第二个 OBB 的最小方向。
6. 可调整有限 Ray 的起点、方向和距离，观察入口点与法线。

该组件只负责显示，不提供 Tank 移动或联网能力。

## 七、明确未完成与后续边界

阶段 16.0 **没有**完成：

- `GameWorld` 静态地图碰撞；
- Tank 阻挡、推出、子步、防穿透或滑墙；
- 客户端即时预测和未确认输入重放的碰撞；
- 双客户端联网碰撞验收；
- 射击墙体遮挡、出生点占用或地图版本；
- 三维斜坡、台阶、Capsule、墙跑、攀爬或骨骼 HitBox；
- Sweep/Cast 和动态刚体模拟。

下一阶段 16.1 应在独立的 `TankWorldCollision2D`/移动层中使用这些查询。碰撞检测继续只回答“是否重叠、法线和深度”，停止、滑墙、子步和迭代上限由移动层决定。未来 3C 增加三维 CollisionCore/CharacterMotor，不把当前二维类型伪装成通用三维接口。
