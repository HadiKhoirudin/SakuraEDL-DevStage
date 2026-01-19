# LoveAlways - 多模式刷机工具

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows-blue?style=flat-square" alt="Platform">
  <img src="https://img.shields.io/badge/.NET-Framework%204.8-purple?style=flat-square" alt=".NET">
  <img src="https://img.shields.io/badge/License-CC%20BY--NC--SA%204.0-lightgrey?style=flat-square" alt="License">
</p>

## 📖 项目简介

LoveAlways 是一款功能强大的多模式刷机工具，支持 **高通 EDL (9008)** 和 **Fastboot** 两种刷机模式，提供多厂商设备的刷写、分区管理、云端 OTA 解析和设备信息读取功能。

## ✨ 主要功能

### 🔌 连接与协议
- **Sahara 协议** - 自动握手、引导加载、芯片信息读取
- **Firehose 协议** - XML 命令交互、分区读写、擦除操作
- **多 LUN 支持** - 支持 UFS 设备的多 LUN 管理

### 🔐 厂商认证
| 厂商 | 认证方式 | 状态 |
|------|----------|------|
| **OnePlus** | Demacia + SetProjModel | ✅ 完整支持 |
| **OPPO/Realme** | VIP 认证 (Digest/Signature) | ✅ 完整支持 |
| **Xiaomi** | MiAuth 认证 | ✅ 完整支持 |
| **Lenovo** | 标准模式 | ✅ 完整支持 |
| **ZTE/nubia** | 标准模式 | ✅ 完整支持 |

### 📊 设备信息解析
- **芯片信息** - MSM ID、OEM ID、PK Hash、序列号
- **build.prop 解析** - 从 EROFS/EXT4 文件系统自动提取
- **LP Metadata 解析** - Super 分区逻辑卷元数据解析
- **多厂商适配** - OPLUS、Xiaomi、Lenovo、ZTE 专用策略

### 🛠️ EDL 分区操作
- **读取分区表** (GPT) - 支持 VIP 伪装模式
- **分区回读** - 支持进度显示和超时保护
- **分区写入** - 支持 Sparse 镜像自动检测
- **分区擦除** - 安全擦除指定分区
- **XML 生成** - 自动生成 rawprogram/patch XML

### ⚡ Fastboot 模式
- **设备连接** - 自动检测 Fastboot 设备，支持多设备管理
- **分区读取** - 读取设备分区表和设备信息
- **分区刷写** - 支持本地镜像和 Payload 刷写
- **分区擦除** - 安全擦除选定分区
- **Sparse 镜像** - 自动检测和处理 Sparse 格式镜像
- **刷机脚本** - 支持 flash-all.bat 脚本解析和执行

### ☁️ 云端 OTA 支持
- **远程 Payload 解析** - 直接解析云端 OTA 链接中的 payload.bin
- **HTTP Range 请求** - 高效获取远程文件片段，无需下载完整包
- **流式刷写** - 边下载边刷写，节省本地存储空间
- **分区提取** - 支持从云端 OTA 直接提取分区镜像
- **实时速度显示** - 分别显示下载速度和 Fastboot 通讯速度

### 📊 双进度条显示
- **总进度条** (长) - 显示整体操作进度，基于已完成的分区数量
- **子进度条** (短) - 显示当前操作的实时进度 (下载/提取/刷写)

## 🖥️ 系统要求

- **操作系统**: Windows 10/11 (64-bit)
- **运行时**: .NET Framework 4.8
- **驱动**: Qualcomm HS-USB QDLoader 9008 驱动

## 📁 项目结构

```
LoveAlways/
├── Qualcomm/                   # 高通 EDL 模式
│   ├── Authentication/         # 厂商认证策略
│   │   ├── OnePlusAuthStrategy.cs
│   │   ├── XiaomiAuthStrategy.cs
│   │   └── IAuthStrategy.cs
│   ├── Common/                 # 通用工具类
│   │   ├── GptParser.cs
│   │   └── SparseImageHelper.cs
│   ├── Database/               # 设备数据库
│   │   └── QualcommDatabase.cs
│   ├── Models/                 # 数据模型
│   │   └── PartitionInfo.cs
│   ├── Protocol/               # 通信协议
│   │   ├── SaharaProtocol.cs
│   │   └── FirehoseClient.cs
│   ├── Services/               # 业务服务
│   │   ├── QualcommService.cs
│   │   └── DeviceInfoService.cs
│   └── UI/                     # UI 控制器
│       └── QualcommUIController.cs
├── Fastboot/                   # Fastboot 模式
│   ├── Common/                 # 通用工具
│   │   ├── BatScriptParser.cs  # 刷机脚本解析
│   │   └── FastbootCommand.cs  # 命令定义
│   ├── Image/                  # 镜像处理
│   │   └── SparseImage.cs      # Sparse 镜像解析
│   ├── Models/                 # 数据模型
│   │   └── FastbootDeviceInfo.cs
│   ├── Payload/                # Payload 解析
│   │   ├── PayloadParser.cs    # 本地 Payload 解析
│   │   ├── PayloadService.cs   # 本地 Payload 服务
│   │   └── RemotePayloadService.cs  # 云端 Payload 服务
│   ├── Protocol/               # 通信协议
│   │   ├── FastbootClient.cs   # Fastboot 客户端
│   │   └── FastbootProtocol.cs # 协议实现
│   ├── Services/               # 业务服务
│   │   ├── FastbootService.cs  # Fastboot 核心服务
│   │   └── FastbootNativeService.cs
│   ├── Transport/              # 传输层
│   │   ├── IFastbootTransport.cs
│   │   └── UsbTransport.cs
│   └── UI/                     # UI 控制器
│       └── FastbootUIController.cs
├── Form1.cs                    # 主窗体
├── Program.cs                  # 程序入口
└── README.md                   # 本文档
```

## 🚀 快速开始

### 1. 准备工作
1. 安装 Qualcomm USB 驱动
2. 下载对应设备的 Programmer 文件 (通常为 `prog_firehose_*.elf` 或 `prog_firehose_*.mbn`)

### 2. 连接设备
1. 将设备进入 EDL 模式 (9008 模式)
2. 打开 LoveAlways，软件会自动检测设备
3. 选择对应的 Programmer 文件

### 3. EDL 模式操作
- **读取分区表**: 点击「读取分区」获取设备分区列表
- **回读分区**: 勾选需要回读的分区，点击「回读分区」
- **写入分区**: 选择镜像文件，点击「写入分区」
- **擦除分区**: 勾选需要擦除的分区，点击「擦除」

### 4. Fastboot 模式操作
1. 将设备进入 Fastboot 模式 (通常是 音量下 + 电源键)
2. 切换到 Fastboot 页面，软件会自动检测设备
3. 基本操作：
   - **读取分区**: 点击「读取分区」获取设备分区和信息
   - **本地刷写**: 选择本地镜像文件进行刷写
   - **Payload 刷写**: 加载 payload.bin 后选择分区刷写
   - **云端刷写**: 输入 OTA 链接，直接从云端下载并刷写

### 5. 云端 OTA 使用
1. 在 Fastboot 页面的文本框中输入 OTA 链接
2. 点击「云端解析」或按 Enter 键
3. 等待解析完成后，勾选需要的分区
4. 选择操作：
   - **提取分区**: 下载并保存分区镜像到本地
   - **写入分区**: 边下载边刷写到设备 (需要连接 Fastboot 设备)

## ⚠️ 注意事项

> **警告**: 刷机操作有风险，可能导致设备变砖。请在操作前：
> 1. 备份重要数据
> 2. 确保电池电量充足 (>50%)
> 3. 使用正确的固件和 Programmer
> 4. 不要在刷写过程中断开连接

## 🔧 故障排除

### EDL 模式问题
| 问题 | 可能原因 | 解决方案 |
|------|----------|----------|
| 检测不到设备 | 驱动未安装 | 安装 Qualcomm USB 驱动 |
| 认证失败 | Programmer 不匹配 | 使用正确的 Programmer 文件 |
| 写入被拒绝 | 未通过 VIP 认证 | 选择正确的 Digest/Signature 文件 |
| 解析超时 | 设备响应慢 | 等待或重新连接设备 |

### Fastboot 模式问题
| 问题 | 可能原因 | 解决方案 |
|------|----------|----------|
| 检测不到设备 | ADB 驱动未安装 | 安装 Google USB 驱动或厂商驱动 |
| 刷写失败 | Bootloader 已锁定 | 先解锁 Bootloader |
| 云端解析失败 | 链接已过期 | 获取新的 OTA 链接 |
| 下载速度慢 | 网络问题 | 检查网络连接或使用代理 |
| 刷写速度显示 0 | USB 2.0 连接 | 使用 USB 3.0 接口 |

## 📝 更新日志

### v1.2.0 (2026-01)
- ✅ **云端 OTA 支持** - 直接解析远程 OTA 链接中的 payload.bin
- ✅ **流式刷写** - 边下载边刷写，节省本地存储空间
- ✅ **双进度条优化** - 总进度条显示整体进度，子进度条显示当前操作进度
- ✅ **速度显示增强** - 分别显示下载速度和 Fastboot 通讯速度
- ✅ **分区搜索** - Fastboot 页面支持分区快速搜索和定位

### v1.1.0 (2025)
- ✅ **Fastboot 模式** - 完整的 Fastboot 刷机支持
- ✅ **Payload 解析** - 支持本地和云端 payload.bin 解析
- ✅ **分区提取** - 从 Payload 提取指定分区镜像
- ✅ **刷机脚本** - 支持 flash-all.bat 脚本解析和执行
- ✅ **Sparse 镜像** - 自动检测和处理 Sparse 格式

### v1.0.0 (2025)
- ✅ 支持 OnePlus 设备 Demacia 认证 + Token 写入
- ✅ 支持 OPPO/Realme VIP 认证
- ✅ 支持 Xiaomi MiAuth 认证
- ✅ 多厂商 build.prop 自动解析
- ✅ GPT 分区表完整解析 (支持大分区表)
- ✅ 60 秒总超时保护防止卡死
- ✅ 实时进度条更新

---

## 📄 许可协议

本项目采用 **CC BY-NC-SA 4.0** 许可协议（知识共享 署名-非商业性使用-相同方式共享 4.0 国际）

[![License: CC BY-NC-SA 4.0](https://img.shields.io/badge/License-CC%20BY--NC--SA%204.0-lightgrey.svg)](https://creativecommons.org/licenses/by-nc-sa/4.0/)

### ✅ 您可以
- **共享** — 在任何媒介以任何形式复制、发行本作品
- **演绎** — 修改、转换或以本作品为基础进行创作

### 📋 惟须遵守
- **署名** — 您必须给出适当的署名，提供指向本许可协议的链接
- **非商业性使用** — 您不得将本作品用于商业目的
- **相同方式共享** — 如果您基于本作品进行创作，必须使用相同的许可协议分发

### ⚠️ 免责声明
本软件按"原样"提供，不提供任何保证。刷机操作有风险，用户自行承担所有风险。作者不对设备损坏或数据丢失负责。

### 📬 联系方式
如需商业授权或有其他问题，请联系：**QQ 1708298587**

---

<p align="center">
  <sub>Made with ❤️ for the Android community</sub>
</p>
