# 阶段 12A：可靠开火请求

本小阶段只补足“客户端的开火意图能否到达服务器”的 UDP 丢包问题；它不是死亡、重生和结算等关键事件账本的完整替代品。`WorldSnapshot` 仍保持独立、可丢弃的全量状态管线，`FireEvent`、`HitEvent`、`HealthChangedEvent` 的语义也不变。

## 协议和边界

服务器对每个已完成握手、且 `playerId` 与 endpoint 一致的 `FireRequest` 回发：

```text
FireReceipt {
  playerId,
  fireSequence,
  accepted,
  reason,
  serverTick
}
```

`accepted=true` 仅表示该请求通过 `GameWorld` 的接收校验并且已经进入本次开火队列；它不表示命中、伤害或扣血。实际开火、延迟补偿、命中检测和生命值修改仍只在下一次权威 `GameWorld.Tick` 中发生，随后仍由原有的 `FireEvent`、`HitEvent`、`HealthChangedEvent` 表现。

`GameWorld` 为每位玩家保留有界的近期 `fireSequence -> 收据决定` 历史（默认 128 条）。UDP 重发命中该历史时不会再次入队、不会再次扣血，并回放第一次决定的 `accepted`、`reason` 和 `serverTick`。历史长度覆盖客户端的短期有限重发窗口，而不会把客户端控制的数据无限保留在服务器中。

## 客户端有限重发

`UdpNetworkClient` 发出 `FireRequest` 时会保存原始 DTO、序列号和序列化后的原始 JSON。默认在 0.2 秒未收到对应收据时，使用完全相同的 JSON（因此 `fireSequence` 不变）重发；最多重发 3 次。收到同一玩家、同一序列号的 `FireReceipt` 后立即移除待确认请求。达到上限后停止重发，记录失败并输出警告。

F3 网络面板新增 `Fire request receipts` 与 `Last fire receipt`：可查看待确认数量、重发量、失败量、接收/拒绝收据数，以及最近一次收据的结果与原因。

## 自动验证

`Server.Tests` 覆盖：

- 重复 `fireSequence` 只入队一次，并只在 Tick 内结算一次；
- 对同一合法序列号重发时，收据的接受结果、原因和首次 `serverTick` 保持一致；
- 首次非法请求得到拒绝收据，同序列重发回放同一拒绝结果且不重复计数。

运行：

```powershell
dotnet build Server/Server/Server.csproj
dotnet build Server/Server.Tests/Server.Tests.csproj
dotnet run --project Server/Server.Tests/Server.Tests.csproj
```
