using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// =============================================================================
// UDP游戏服务器 - Program.cs
// =============================================================================
//
// 【这个文件是什么？】
// 这是一个C#控制台程序（不是Unity项目），独立运行在命令行中。
// 它监听7777端口，等待Unity客户端的UDP消息，处理ClientHello并回复ServerWelcome。
//
// 【为什么服务器和客户端用不同的JSON库？】
// Unity客户端：使用 UnityEngine.JsonUtility（Unity内置，轻量但功能少）
// 服务器这边：使用 System.Text.Json（.NET标准库，功能更全，支持异步）
// 两者生成的JSON格式完全兼容，可以互通。
//
// 【这个文件用了C# 10的"顶级语句"(Top-level Statements)特性】
// 没有显式的 class Program { static void Main() }，代码直接写在文件顶层。
// 编译器会自动生成Main方法包裹这些代码。
// 用这个特性的原因是：服务器代码量少，这样写更简洁。
// =============================================================================

// ---- 配置 ----

/// <summary>服务器监听的端口号，必须和Unity客户端的serverPort一致</summary>
const int listenPort = 7777;

// ---- 核心状态 ----

/// <summary>
/// UDP服务器Socket。
/// new UdpClient(port) 创建并绑定到指定端口——任何发往本机7777端口的UDP包都会被它接收。
///
/// using声明（C# 8+）：当变量离开作用域时自动调用Dispose()释放资源。
/// 和Unity的udpClient不同——Unity那里是手动Close的，因为MonoBehaviour生命周期不同。
/// </summary>
using UdpClient udpServer = new UdpClient(listenPort);

/// <summary>
/// 已连接的客户端字典。
///
/// Key: "IP:Port" 字符串，例如 "192.168.1.5:54321"
///      为什么用IP+Port作为Key？因为UDP是无连接的，服务器无法"记住"客户端。
///      收到数据时通过RemoteEndPoint来区分不同客户端。
///
/// Value: ConnectedClient 对象，包含playerId、名字、网络端点
///
/// 用Dictionary而不是List是为了O(1)查找——收到消息时快速定位是哪个客户端。
/// </summary>
Dictionary<string, ConnectedClient> clients = new Dictionary<string, ConnectedClient>();

/// <summary>
/// 自增的玩家ID计数器。
/// 每来一个新客户端就+1，保证每个玩家有唯一编号。
/// </summary>
int nextPlayerId = 1;

// ---- 启动日志 ----

Console.WriteLine($"UDP server started on port {listenPort}.");
Console.WriteLine("Waiting for Unity ClientHello messages...");

// ---- 主循环 ----
//
// 【主循环的工作方式】
// while(true) 无限循环，服务器永远运行，直到按Ctrl+C关闭。
//
// 【await关键字和异步模式】
// await的意思是"暂停当前方法，直到这个异步操作完成，但不阻塞线程"。
// 相当于：你去咖啡店点单，服务员不会站在你面前等你想好——
// 他去做别的事，等你想好了（异步操作完成）再回来服务你。
//
// udpServer.ReceiveAsync() 返回 Task<UdpReceiveResult>
// - 它背后的原理是：操作系统内核收到UDP包后通知.NET运行时，.NET再唤醒这里的代码
// - 没有消息时，线程处于休眠状态，不消耗CPU
// - 这和Unity客户端的Update()轮询（每帧检查）是完全不同的模式
//
// 【为什么服务器用异步但客户端用轮询？】
// - 服务器：C#控制台程序，可以用全异步（await），代码简洁，资源效率高
// - 客户端：Unity主线程不能await（会卡住整个游戏），所以用Update()主动轮询
//   如果客户端也用异步，需要配合UniTask等库，对新手更复杂

while (true)
{
    // ============================================================
    // 第1步：等待接收UDP数据包
    // ============================================================
    // ReceiveAsync() 异步等待，直到有UDP包到达
    // 返回的 UdpReceiveResult 包含两部分：
    //   - Buffer: byte[] 收到的原始字节数据
    //   - RemoteEndPoint: IPEndPoint 发送方的IP地址和端口
    UdpReceiveResult received = await udpServer.ReceiveAsync();

    // ============================================================
    // 第2步：字节 → JSON字符串
    // ============================================================
    string json = Encoding.UTF8.GetString(received.Buffer);

    // ============================================================
    // 第3步：生成客户端唯一标识
    // ============================================================
    // "192.168.1.5:54321" 这样的字符串，用于在字典中查找客户端
    string clientKey = GetClientKey(received.RemoteEndPoint);

    // ============================================================
    // 第4步：打印接收日志
    // ============================================================
    Console.WriteLine();
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] From {clientKey}");
    Console.WriteLine(json);

    // ============================================================
    // 第5步：解析消息类型并分发
    // ============================================================
    // 先只读type字段，不关心具体内容
    string messageType = ReadMessageType(json);

    switch (messageType)
    {
        case "ClientHello":
            // 客户端发来打招呼消息 → 分配或更新玩家ID，回复欢迎消息
            await HandleClientHello(udpServer, clients, received.RemoteEndPoint, json);
            break;

        case "":
            // type字段缺失或JSON格式错误
            Console.WriteLine("Message ignored: JSON is missing a readable type field.");
            break;

        default:
            // 不支持的消息类型
            Console.WriteLine($"Message ignored: unsupported type '{messageType}'.");
            break;
    }
}

/// <summary>
/// 处理客户端的ClientHello消息。
///
/// 【业务逻辑】
/// 1. 解析JSON获取玩家名字
/// 2. 检查这个客户端之前是否来过：
///    - 新客户端 → 分配新的playerId，加入字典
///    - 老客户端 → 更新信息，复用旧的playerId
/// 3. 回复ServerWelcome消息
///
/// 【为什么需要接收 UdpClient 和 Dictionary 作为参数？】
/// 顶级语句中，这些是局部变量。如果不传参，方法里访问的udpServer会不明确。
/// 传递参数比使用全局变量更清晰、更易测试。
/// </summary>
async Task HandleClientHello(
    UdpClient udpServer,
    Dictionary<string, ConnectedClient> clients,
    IPEndPoint remoteEndPoint,
    string json)
{
    // ---- 生成客户端Key ----
    string clientKey = GetClientKey(remoteEndPoint);

    // ---- 解析消息体 ----
    // JsonSerializer.Deserialize 是 System.Text.Json 的反序列化方法
    // <ClientHelloMessage?> 中的 ? 表示结果可能为null（JSON格式不对时）
    ClientHelloMessage? hello = JsonSerializer.Deserialize<ClientHelloMessage>(json);

    // 如果名字为空就用默认值
    // string.IsNullOrWhiteSpace() 同时检查 null、空字符串""、纯空格"  "
    string playerName = string.IsNullOrWhiteSpace(hello?.Name) ? "Player" : hello.Name;

    // ---- 注册或更新客户端 ----
    // TryGetValue 比 ContainsKey + 索引器更高效（只查一次字典）
    if (!clients.TryGetValue(clientKey, out ConnectedClient? client))
    {
        // 新客户端：创建记录，分配playerId
        client = new ConnectedClient(nextPlayerId, playerName, remoteEndPoint);
        clients.Add(clientKey, client);
        nextPlayerId++;  // 自增，保证下一个客户端拿到不同的ID

        Console.WriteLine($"New client connected: {playerName}, playerId={client.PlayerId}");
    }
    else
    {
        // 老客户端再次打招呼（比如断线重连）：更新信息，不换ID
        client.Name = playerName;
        client.RemoteEndPoint = remoteEndPoint;
        Console.WriteLine($"Known client said hello again: {playerName}, playerId={client.PlayerId}");
    }

    // ---- 构造回复消息 ----
    ServerWelcomeMessage welcome = new ServerWelcomeMessage
    {
        Type = "ServerWelcome",
        PlayerId = client.PlayerId,
        Message = "Welcome to the UDP demo server."
    };

    // ---- 序列化并发送 ----
    // C#对象 → JSON字符串 → UTF-8字节数组 → UDP发送
    string replyJson = JsonSerializer.Serialize(welcome);
    byte[] replyBytes = Encoding.UTF8.GetBytes(replyJson);

    // SendAsync：异步发送UDP数据包
    // 需要指定 remoteEndPoint，因为服务器面对多个客户端，必须知道发给谁
    // （不像客户端那边Connect后可以不指定目标）
    await udpServer.SendAsync(replyBytes, replyBytes.Length, remoteEndPoint);

    Console.WriteLine($"Sent to {clientKey}");
    Console.WriteLine(replyJson);
}

/// <summary>
/// 从JSON字符串中安全地读取type字段。
///
/// 【为什么专门写一个方法而不直接用JsonSerializer？】
/// - 先读type才能知道用什么类型去完整反序列化
/// - 使用JsonDocument（只读、轻量的JSON解析器）只解析type，不解析整条消息
/// - TryGetProperty 安全地检查字段是否存在，不会因为缺字段而崩溃
/// - try-catch 保证即使收到乱码/非法JSON也不会让整个服务器挂掉
///
/// 【对比Unity客户端】
/// 客户端用 JsonUtility.FromJson<MessageHeader>() 来读type——同样的理念，不同的库。
/// </summary>
/// <param name="json">完整的JSON字符串</param>
/// <returns>type字段的值；如果解析失败返回空字符串</returns>
string ReadMessageType(string json)
{
    try
    {
        // JsonDocument.Parse 解析JSON但不创建强类型对象
        // 类似于把一个JSON字符串变成一棵树，我们可以遍历这棵树
        using JsonDocument document = JsonDocument.Parse(json);

        // RootElement 是JSON的根节点
        // TryGetProperty 查找名为"type"的属性
        if (document.RootElement.TryGetProperty("type", out JsonElement typeElement))
        {
            // GetString() 获取字符串值；?? 表示如果是null就返回""
            return typeElement.GetString() ?? string.Empty;
        }
    }
    catch (JsonException exception)
    {
        Console.WriteLine($"Invalid JSON: {exception.Message}");
    }

    return string.Empty;
}

/// <summary>
/// 根据IP端点生成客户端的唯一标识字符串。
///
/// 例如：IPEndPoint(192.168.1.5, 54321) → "192.168.1.5:54321"
///
/// 为什么用IP+端口而不是用一个GUID？
/// - UDP是无连接的，服务器无法给客户端"发"一个标识
/// - 但每次收到数据时，系统会告诉我们发送方的IP和端口
/// - 同一个客户端的IP和端口在短时间内通常不变（NAT环境下端口可能变，这就是为什么
///   更复杂的游戏会加入Token/Session机制——但这个入门项目先保持简单）
/// </summary>
string GetClientKey(IPEndPoint endPoint)
{
    return $"{endPoint.Address}:{endPoint.Port}";
}

// =============================================================================
// 数据传输对象（DTOs）
//
// 注意：服务器和客户端各有各的消息类定义——
// - Unity客户端用的是 public字段 + [Serializable] 特性
// - 服务器这边用的是 属性{get;set;} + [JsonPropertyName] 特性
//
// 为什么不用同一套代码？
// - 客户端和服务器是两个独立的项目，不共享代码
// - Unity使用.Net Standard 2.1而服务器用.Net 9，JSON库不同
// - 实际项目中可以用共享库（shared library），但入门阶段分开放更清晰
//
// JSON字段名必须保持一致！客户端发的{"type": "ClientHello", "name": "Player"}
// 和服务器期待的type/name字段要完全对应，区分大小写。
// =============================================================================

/// <summary>
/// 客户端打招呼消息的数据结构（服务器端定义）。
/// [JsonPropertyName("type")] 告诉System.Text.Json：C#属性Type对应JSON中的"type"字段。
/// 这样C#遵循的PascalCase命名（Type, Name）和JSON的camelCase（type, name）可以不同。
/// </summary>
public sealed class ClientHelloMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// 服务器欢迎消息的数据结构。
/// </summary>
public sealed class ServerWelcomeMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("playerId")]
    public int PlayerId { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// 服务器内部使用的"已连接客户端"记录。
/// 这个类不会直接序列化成JSON发给客户端——它只是服务器在内存中跟踪客户端状态用的。
/// 注意：这不是一个DTO（数据传输对象），而是领域模型（Domain Model）。
///
/// 区别：
/// - DTO（ClientHelloMessage等）：定义"线路上传输什么"，和JSON格式一一对应
/// - 领域模型（ConnectedClient）：定义"服务器内部怎么表示一个客户端"，包含业务逻辑
/// </summary>
public sealed class ConnectedClient
{
    /// <summary>
    /// 构造函数：新建客户端时必须提供这三个信息。
    /// 用构造函数而不是对象初始化器，保证创建时所有必要信息都被填充。
    /// </summary>
    public ConnectedClient(int playerId, string name, IPEndPoint remoteEndPoint)
    {
        PlayerId = playerId;
        Name = name;
        RemoteEndPoint = remoteEndPoint;
    }

    /// <summary>玩家ID，分配后不变（只有get没有set）</summary>
    public int PlayerId { get; }

    /// <summary>玩家名字，可以改变</summary>
    public string Name { get; set; }

    /// <summary>客户端的IP端点，重连时会更新</summary>
    public IPEndPoint RemoteEndPoint { get; set; }
}
