================================================================================
高通 (Qualcomm) EDL 刷写模块 - 完整版
================================================================================

模块结构:
---------
Qualcomm/
├── Common/                     公共组件
│   ├── SerialPortManager.cs    串口管理器 (线程安全)
│   └── SparseStream.cs         Sparse 镜像透明流
│
├── Database/                   数据库
│   └── QualcommDatabase.cs     芯片识别库 (MSM ID, 厂商, PK Hash)
│
├── Models/                     数据模型
│   └── PartitionInfo.cs        分区信息模型
│
├── Protocol/                   协议实现
│   ├── SaharaProtocol.cs       Sahara 协议 (完整版 V1/V2/V3)
│   └── FirehoseClient.cs       Firehose 协议 (完整版)
│
└── Services/                   服务层
    └── QualcommService.cs      高层 API (整合 Sahara + Firehose)


协议说明:
---------

### Sahara 协议 (SaharaProtocol.cs)
第一阶段引导协议，用于上传 Programmer/Loader 到设备 RAM。

支持的功能:
- Hello 握手 (支持 V1/V2/V3 协议版本)
- 芯片信息读取 (Serial, HWID, PK Hash)
- 32 位和 64 位数据传输
- 命令模式 (读取设备信息)
- 传输模式 (上传 Programmer)
- 状态机重置和硬重置

命令 ID:
  0x01 - Hello
  0x02 - HelloResponse  
  0x03 - ReadData (32位)
  0x04 - EndImageTransfer
  0x05 - Done
  0x07 - Reset
  0x0B - CommandReady
  0x0C - SwitchMode
  0x0D - Execute
  0x12 - ReadData64 (64位)


### Firehose 协议 (FirehoseClient.cs)
XML 格式的刷写协议，用于读写分区、设备控制。

支持的功能:
- 设备配置 (UFS/eMMC)
- VIP 认证 (OPPO/Realme/OnePlus)
- GPT 分区表读取和解析
- 分区读取/写入/擦除
- 动态伪装策略 (绕过 VIP 限制)
- A/B Slot 切换
- GPT 修复
- 设备重启/关机/EDL 模式

XML 命令:
  <configure>    - 配置设备
  <read>         - 读取扇区
  <program>      - 写入扇区
  <erase>        - 擦除分区
  <power>        - 电源控制 (reset/off/edl)
  <setactiveslot> - 设置 A/B Slot
  <fixgpt>       - 修复 GPT
  <nop>          - 心跳/测试


使用示例:
---------
```csharp
using LoveAlways.Qualcomm.Services;

// 创建服务 (带日志和进度回调)
var service = new QualcommService(
    msg => Console.WriteLine(msg),
    (current, total) => Console.WriteLine($"进度: {current}/{total}")
);

// 连接设备
bool connected = await service.ConnectAsync(
    "COM3", 
    @"C:\Programmer\prog_firehose.elf",
    "ufs"  // 或 "emmc"
);

if (connected)
{
    // 显示芯片信息
    var info = service.ChipInfo;
    Console.WriteLine($"芯片: {info.ChipName}");
    Console.WriteLine($"厂商: {info.Vendor}");
    Console.WriteLine($"VIP: {service.IsVipDevice}");
    
    // 读取分区表
    var partitions = await service.ReadAllGptAsync();
    foreach (var p in partitions)
    {
        Console.WriteLine($"[LUN{p.Lun}] {p.Name}: {p.FormattedSize}");
    }
    
    // 读取分区
    await service.ReadPartitionAsync("boot", @"C:\backup\boot.img");
    
    // 刷写分区
    await service.WritePartitionAsync("boot", @"C:\flash\boot.img");
    
    // 擦除分区
    await service.ErasePartitionAsync("userdata");
    
    // 设置 A/B Slot
    await service.SetActiveSlotAsync("a");
    
    // 修复 GPT
    await service.FixGptAsync();
    
    // 重启
    await service.RebootAsync();
}

service.Dispose();
```


命名空间:
---------
- LoveAlways.Qualcomm.Common      串口管理
- LoveAlways.Qualcomm.Database    芯片数据库
- LoveAlways.Qualcomm.Models      数据模型
- LoveAlways.Qualcomm.Protocol    协议实现
- LoveAlways.Qualcomm.Services    服务层 API


支持的芯片:
-----------
- Snapdragon 2xx: MSM8909 (210)
- Snapdragon 4xx: MSM8916 (410), MSM8953 (625), SDM450
- Snapdragon 6xx: SDM630, SDM636, SDM660, SM6150 (675)
- Snapdragon 7xx: SDM670, SDM710, SM7150 (730), SM7250 (765G)
- Snapdragon 8xx: MSM8974 (800) ~ SM8750 (8 Elite)


VIP 设备:
---------
以下厂商设备需要特殊认证:
- OPPO (需要 digest.bin + signature.bin)
- OnePlus
- Realme

认证文件放置在 Programmer 同目录下:
  prog_firehose.elf
  digest.bin
  signature.bin


依赖:
-----
- .NET Framework 4.8
- System.IO.Ports (内置)


注意事项:
---------
1. Sahara 握手时不要清空缓冲区 (discardBuffer = false)
2. VIP 认证需要正确的 Programmer 目录和认证文件
3. 扇区大小: UFS = 4096, eMMC = 512
4. 批量刷写时建议使用 CancellationToken 支持取消
5. 每次操作后检查返回值以确保成功


错误处理:
---------
Sahara 常见错误:
- HashTableAuthFailure: Loader 签名不匹配设备
- HashVerificationFailure: Loader 被篡改
- HashTableNotFound: Loader 未签名

Firehose 常见错误:
- 认证失败: 需要 VIP 认证
- 分区未找到: 分区名拼写错误或不存在
- 写保护: 设备处于写保护状态
- 超时: USB 连接不稳定


更新日志:
---------
v1.0 - 完整 Sahara/Firehose 协议实现
     - 支持 V1/V2/V3 协议版本
     - VIP 认证 (OPPO/Realme/OnePlus)
     - 动态伪装策略
     - A/B Slot 支持
     - GPT 修复
