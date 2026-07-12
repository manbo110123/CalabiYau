using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>
/// UDP网络客户端组件
/// 挂载到Unity场景中的GameObject上，负责与UDP服务器通信。
///
/// 核心流程：
/// 1. Start()时打开UDP套接字，向服务器发送ClientHello消息
/// 2. 每帧Update()检查是否有服务器发来的消息
/// 3. 收到ServerWelcome后记录自己的playerId
/// 4. 退出时关闭套接字
///
/// 为什么用UDP而不是TCP？
/// - UDP是无连接协议，延迟低，适合实时游戏（如坦克大战）
/// - UDP不保证送达，但游戏场景中丢失一帧位置数据影响不大，下一帧会覆盖
/// - 如果做的是需要可靠传输的功能（如聊天、交易），需要在UDP之上自己实现确认/重传机制
/// </summary>
public class UdpNetworkClient : MonoBehaviour
{
    // ==================== Inspector可配置参数 ====================

    [Header("Server")]
    [SerializeField] private string serverAddress = "127.0.0.1";  // 服务器IP，127.0.0.1表示本机
    [SerializeField] private int serverPort = 7777;                // 服务器端口，必须和服务器监听端口一致
    [SerializeField] private string playerName = "Player";         // 玩家名字，会在ClientHello中发给服务器
    [SerializeField] private bool sendHelloOnStart = true;         // 是否在Start时自动发送ClientHello

    [Header("Debug")]
    [SerializeField] private bool logJsonMessages = true;          // 是否在Console打印收发消息（调试用）

    // ==================== 内部状态 ====================

    /// <summary>
    /// C#原生的UDP客户端对象。
    /// using System.Net.Sockets 中的类，封装了底层的Socket操作。
    /// </summary>
    private UdpClient udpClient;

    /// <summary>
    /// 服务器分配给我们的玩家ID。
    /// 服务器收到ClientHello后会发回ServerWelcome，其中包含这个ID。
    /// 之后所有通信（如"玩家3移动到(x,y)"）都会用这个ID来标识身份。
    /// </summary>
    private int playerId;

    /// <summary>
    /// 公开只读属性，让其他脚本可以获取playerId。
    /// 例如TankControl.cs需要知道自己是哪个玩家，就从这里读。
    /// </summary>
    public int PlayerId => playerId;

    // ==================== Unity生命周期 ====================

    /// <summary>
    /// Unity脚本的Start()在第一次Update之前调用一次。
    /// 在这里打开网络连接并打招呼。
    /// </summary>
    private void Start()
    {
        // 第1步：创建UDP套接字
        OpenSocket();

        // 第2步：可选——自动向服务器打招呼
        if (sendHelloOnStart)
        {
            SendClientHello();
        }
    }

    /// <summary>
    /// Unity脚本的Update()每帧调用一次。
    /// 在这里轮询接收服务器发来的消息。
    ///
    /// 为什么放在Update而不是用协程/异步？
    /// - Unity的MonoBehaviour主线程模型下，同步轮询最简单直观
    /// - udpClient.Available > 0 检查是非阻塞的，不会卡住主线程
    /// - 对于入门项目来说，这是最易理解的方案
    /// </summary>
    private void Update()
    {
        ReceivePendingMessages();
    }

    /// <summary>
    /// 应用退出时自动调用，做清理工作。
    /// 如果不关闭Socket，端口可能被占用一段时间，下次启动会失败。
    /// </summary>
    private void OnApplicationQuit()
    {
        CloseSocket();
    }

    // ==================== 公共方法 ====================

    /// <summary>
    /// [ContextMenu]让你在Inspector中右键点击组件就能手动调用这个方法。
    /// 用于调试：在游戏运行中右键组件 → "Send ClientHello"。
    /// </summary>
    [ContextMenu("Send ClientHello")]
    public void SendClientHello()
    {
        // 先确保Socket是打开状态（防止脚本挂载时Start还没调，或之前关闭了）
        OpenSocket();

        // 构造消息对象
        ClientHelloMessage message = new ClientHelloMessage();
        message.type = "ClientHello";   // 消息类型——服务器根据这个字段决定如何处理
        message.name = playerName;       // 玩家名字

        // Unity的JsonUtility.ToJson()把C#对象序列化为JSON字符串
        // 例如：{"type":"ClientHello","name":"Player"}
        string json = JsonUtility.ToJson(message);

        // 发送
        SendJson(json);
    }

    // ==================== 网络核心方法 ====================

    /// <summary>
    /// 打开UDP套接字（Socket）。
    ///
    /// 什么是Socket（套接字）？
    /// - 套接字 = IP地址 + 端口号，是网络通信的"门"
    /// - 你可以把它想象成一部电话：你拿起电话（打开Socket），
    ///   拨号（Connect），然后就可以说话（Send）和听（Receive）
    ///
    /// UdpClient是C#对UDP Socket的封装，隐藏了底层的复杂性。
    /// </summary>
    private void OpenSocket()
    {
        // 如果已经打开了（udpClient != null），不要重复打开
        // 重复打开会创建多个Socket，造成端口冲突
        if (udpClient != null)
        {
            return;
        }

        try
        {
            // new UdpClient() —— 创建一个UDP Socket，系统自动分配一个本地端口
            // Connect(ip, port) —— 指定"默认目的地"，之后Send时不需要每次指定地址
            //   注意：UDP的Connect()和TCP不一样！UDP的Connect只记录默认目标，
            //   不会建立真正的连接，也不会三次握手。这只是方便我们写代码。
            udpClient = new UdpClient();
            udpClient.Connect(serverAddress, serverPort);
        }
        catch (Exception exception)
        {
            // 如果打开失败（比如端口被占用、网络不可用），打印错误日志
            Debug.LogError($"UDP client failed to open: {exception.Message}");
            CloseSocket();
        }
    }

    /// <summary>
    /// 把JSON字符串以UTF-8编码后通过UDP发送给服务器。
    ///
    /// 为什么用JSON？
    /// - JSON是文本格式，人类可读，方便调试
    /// - Unity自带JsonUtility，不需额外库
    /// - 在网络传输中使用UTF-8编码：每种文字（中文、英文）都被转成字节序列
    ///
    /// 数据流：C#对象 → JSON字符串 → UTF-8字节数组 → UDP发送
    /// 例：ClientHelloMessage → {"type":"ClientHello","name":"Player"} → [123, 34, 116, ...] → 网络
    /// </summary>
    /// <param name="json">要发送的JSON字符串</param>
    private void SendJson(string json)
    {
        // 防御检查：Socket没打开就不发送
        if (udpClient == null)
        {
            return;
        }

        // Encoding.UTF8.GetBytes() 把字符串转成byte[]
        // 为什么要转成byte[]？因为网络传输的基本单位是字节（byte），不是字符（char）
        // 一个中文字符在UTF-8里占3个字节，英文占1个字节
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        // udpClient.Send() 把字节数组发送出去
        // 因为之前Connect()设置了默认目的地，这里不用再写IP和端口
        udpClient.Send(bytes, bytes.Length);

        // 调试日志：在Unity Console中看到我们发了什么
        if (logJsonMessages)
        {
            Debug.Log($"UDP sent: {json}");
        }
    }

    /// <summary>
    /// 接收所有待处理的UDP消息。
    ///
    /// 关键概念 —— UDP是无连接的、基于数据报（Datagram）的协议：
    /// - 每条消息是独立的数据包（数据报），像寄信一样
    /// - 和TCP不同，TCP是"流"——像打电话，数据是连续的
    /// - 所以UDP天然有"消息边界"：你发10字节，对方就收到10字节（如果没丢包）
    ///
    /// udpClient.Available > 0 表示"操作系统的接收缓冲区里有数据等待读取"
    /// while循环确保一次处理完所有积压的消息
    /// </summary>
    private void ReceivePendingMessages()
    {
        if (udpClient == null)
        {
            return;
        }

        try
        {
            // Available属性：操作系统缓冲区中等待读取的字节数
            // 如果 > 0，说明有数据到了；如果 == 0，直接跳过，不会阻塞
            while (udpClient.Available > 0)
            {
                // IPEndPoint用于接收"发送方是谁"的信息
                // IPAddress.Any 表示"接受任何IP"
                // port 0 表示"不限制端口"
                // 虽然我们已经Connect了，但Receive还是能知道数据从哪来的
                IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);

                // Receive() 从缓冲区读取一个数据报
                // ref remoteEndPoint 会被填充为发送方的IP和端口
                // 返回的是byte[]——原始字节数据
                byte[] bytes = udpClient.Receive(ref remoteEndPoint);

                // 反向操作：字节 → 字符串
                string json = Encoding.UTF8.GetString(bytes);

                if (logJsonMessages)
                {
                    Debug.Log($"UDP received: {json}");
                }

                // 交给消息处理器
                HandleMessage(json);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"UDP receive failed: {exception.Message}");
        }
    }

    // ==================== 消息分发与处理 ====================

    /// <summary>
    /// 消息分发器（Message Dispatcher）。
    ///
    /// 接收原始JSON字符串，解析出type字段，然后根据type分发到对应的处理方法。
    /// 这是一个典型的"策略模式"或"消息分发"设计。
    ///
    /// 为什么用switch而不是一堆if-else？
    /// - 性能：switch在C#中编译为跳转表或字典查找，比逐个if快
    /// - 可读性：一目了然地看到所有支持的消息类型
    /// - 扩展性：新增消息类型只需加一个case
    ///
    /// MessageHeader是一个"轻量级解析"——只反序列化type字段，
    /// 不需要先把整条消息完全解析出来就能知道怎么处理。
    /// </summary>
    /// <param name="json">接收到的完整JSON字符串</param>
    private void HandleMessage(string json)
    {
        // 第一步：只解析type字段，判断消息类型
        // JsonUtility.FromJson<MessageHeader>() 只提取它认识的字段（type），忽略其他
        MessageHeader header = JsonUtility.FromJson<MessageHeader>(json);

        switch (header.type)
        {
            case "ServerWelcome":
                // 收到了服务器对我们ClientHello的回复
                // 完整解析为ServerWelcomeMessage获取playerId等信息
                HandleServerWelcome(json);
                break;

            // 未来可以扩展更多消息类型，例如：
            // case "PlayerJoined":
            //     HandlePlayerJoined(json);
            //     break;
            // case "PlayerMoved":
            //     HandlePlayerMoved(json);
            //     break;

            default:
                // 收到不认识的消息类型——打警告，不崩溃
                Debug.LogWarning($"UDP message ignored: unsupported type '{header.type}'.");
                break;
        }
    }

    /// <summary>
    /// 处理服务器的ServerWelcome消息。
    ///
    /// 这个消息是服务器对你ClientHello的应答：
    /// - playerId: 服务器分配给你的唯一编号（1, 2, 3...）
    /// - message: 服务器的问候语
    ///
    /// 之后你发给服务器的每条消息都应该带上这个playerId，
    /// 这样服务器才能知道"这条消息是谁发的"。
    /// </summary>
    private void HandleServerWelcome(string json)
    {
        // 完整反序列化ServerWelcomeMessage
        ServerWelcomeMessage welcome = JsonUtility.FromJson<ServerWelcomeMessage>(json);

        // 保存服务器分配给我们的ID
        playerId = welcome.playerId;

        Debug.Log($"Connected to server. playerId={playerId}, message={welcome.message}");
    }

    // ==================== 清理 ====================

    /// <summary>
    /// 关闭UDP Socket，释放资源。
    ///
    /// 为什么需要手动Close？
    /// - UdpClient占用了操作系统的端口资源
    /// - 不关闭的话，端口可能被"僵尸"占用（TIME_WAIT状态）
    /// - Close()后把udpClient设为null，这样OpenSocket()可以重新打开
    /// </summary>
    private void CloseSocket()
    {
        if (udpClient == null)
        {
            return;
        }

        udpClient.Close();
        udpClient = null;
    }
}

// ====================================================================
// 消息数据结构（Data Transfer Objects / DTOs）
// ====================================================================
// 这些类定义了客户端和服务器之间传输的数据格式。
// [Serializable]特性告诉Unity的JsonUtility这些类可以被序列化/反序列化。
// 字段名必须和JSON中的键名完全一致（区分大小写）。
//
// 为什么用单独的简单类而不是在方法里直接拼JSON？
// 1. 类型安全：编译时就能发现拼写错误
// 2. 可维护：所有消息格式集中定义，一目了然
// 3. 可扩展：增加字段时只需要改类定义
// ====================================================================

/// <summary>
/// 客户端发送给服务器的第一条消息——"你好，我来了"。
/// 服务器收到后会给客户端分配一个playerId，并回复ServerWelcome。
/// </summary>
[Serializable]
public class ClientHelloMessage
{
    /// <summary>消息类型标识，服务器用这个字段决定如何处理</summary>
    public string type;

    /// <summary>玩家的显示名称</summary>
    public string name;
}

/// <summary>
/// 消息头部——所有消息共有的字段。
/// 用于"轻量解析"：先读type决定如何处理，再按具体类型完整解析。
///
/// 这是一个常见的设计模式：消息头 + 消息体。
/// 类比：快递包裹的外包装标签（type）告诉你里面是什么，然后你再拆开（完整解析）。
/// </summary>
[Serializable]
public class MessageHeader
{
    /// <summary>消息类型，如 "ClientHello"、"ServerWelcome"</summary>
    public string type;
}

/// <summary>
/// 服务器对ClientHello的响应——"你好新玩家，你的ID是X"。
/// 客户端收到后就知道自己在服务器中的身份了。
/// </summary>
[Serializable]
public class ServerWelcomeMessage
{
    /// <summary>消息类型 = "ServerWelcome"</summary>
    public string type;

    /// <summary>服务器分配给该玩家的唯一数字ID</summary>
    public int playerId;

    /// <summary>服务器的欢迎文本</summary>
    public string message;
}
