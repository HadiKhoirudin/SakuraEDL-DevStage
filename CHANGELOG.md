# Changelog
All notable changes to this project will be documented in this file.

## [3.0.0] - 2026-01-27

### 🆕 新增功能

#### 联发科 (MTK) 全面支持
- **BROM/Preloader 模式刷机**
  - 自动检测 BROM 和 Preloader 模式
  - DA (Download Agent) 智能加载
  - 支持分离式 DA1 + DA2 文件

- **XFlash 二进制协议** (参考 mtkclient)
  - 命令码: `READ_DATA (0x010005)`, `WRITE_DATA (0x010004)` 等
  - CRC32 校验和支持
  - 自动存储类型检测 (eMMC/UFS/NAND)

- **XML V6 协议**
  - 兼容新设备
  - 自动协议选择和回退

- **漏洞利用**
  - Carbonara 漏洞 (DA1 级别)
  - AllinoneSignature 漏洞 (DA2 级别)
  - 自动检测芯片支持情况

#### 展讯 (SPD/Unisoc) 支持
- **FDL 下载协议**
  - FDL1/FDL2 自动下载和执行
  - HDLC 帧编码/解码
  - 动态波特率切换 (115200 → 921600)

- **PAC 固件解析**
  - 自动解析 PAC 包结构
  - 提取 FDL 和分区镜像
  - 读取地址配置

- **签名绕过机制**
  - T760/T770 `custom_exec_no_verify` 支持
  - exec_addr: 0x65012f48
  - 支持刷写未签名 FDL

- **芯片数据库**
  - SC9863A, T606, T610, T618
  - T700, T760 (已验证), T770
  - 自动 FDL1/FDL2 地址配置

#### 云端 Loader 匹配 (高通)
- 根据芯片 ID 自动获取 Loader
- 云端数据库实时更新
- 移除本地 PAK 资源依赖

### 🔧 改进

#### MTK 模块
- 优化日志输出，添加日志级别控制
- 进度条集成时间显示 (已用/剩余)
- 数据传输速度计算和显示
- 设备连接时显示协议类型 (XFlash/XML)

#### SPD 模块
- 改进端口检测逻辑
- FDL 执行验证增强
- 端口重连机制优化
- T760 地址配置修复

#### 通用
- 统一的错误处理机制
- 改进的设备断开检测
- 日志系统优化

### 📁 新增文件

```
MediaTek/
├── Protocol/
│   ├── xflash_client.cs      # XFlash 二进制协议
│   └── xflash_commands.cs    # XFlash 命令码定义
├── Common/
│   └── mtk_crc32.cs          # CRC32 校验工具
└── ...

Spreadtrum/
├── Database/
│   └── sprd_fdl_database.cs  # 芯片 FDL 数据库
└── ...
```

### 🐛 修复

- 修复 MTK 分区读取进度显示问题
- 修复 SPD T760 默认地址配置错误
- 修复跨模块日志污染问题
- 修复端口重连后的通信问题

---

## [2.0.0] - 2025-12-13

### 新增功能

- OFP/OZIP/OPS 固件解密
- 智能一键刷机
- 设备信息读取
- 多模式认证策略 (标准/VIP/小米)
- 分区风险提示

### 改进

- Sahara V2/V3 协议支持
- Firehose 协议增强
- 日志系统优化

---

## [1.0.0] - 2025-10-01

### 初始版本

- 高通 EDL (9008) 模式支持
- Fastboot 模式支持
- Payload.bin 提取
- GPT 分区表管理
