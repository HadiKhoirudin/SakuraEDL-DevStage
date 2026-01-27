# 高通 EDL (9008) 刷机教程

> 本文档详细介绍如何使用 MultiFlash Tool 对高通设备进行 EDL 模式刷机操作

## 目录

- [概述](#概述)
- [支持的芯片](#支持的芯片)
- [准备工作](#准备工作)
- [基础操作](#基础操作)
- [云端 Loader 匹配](#云端-loader-匹配)
- [固件解密](#固件解密)
- [高级功能](#高级功能)
- [常见问题](#常见问题)

---

## 概述

MultiFlash Tool 支持高通 (Qualcomm) 设备的 EDL (Emergency Download) 模式刷机，包括：

- **Sahara 协议**: V2/V3 版本支持
- **Firehose 协议**: XML 命令刷写
- **云端 Loader**: 自动匹配 Programmer
- **固件解密**: OFP/OZIP/OPS 格式支持

### 工作流程

```
设备 ──► 9008 模式 ──► Sahara 握手 ──► Loader 加载 ──► Firehose 刷写
           │               │               │              │
           └─ EDL 模式     └─ 协议协商      └─ 云端匹配    └─ 分区操作
```

---

## 支持的芯片

### 骁龙 (Snapdragon) 系列

**骁龙 600 系列:**
| 芯片 | 代号 | 状态 |
|------|------|------|
| SDM660 | Starlord | ✅ |
| SDM665 | Trinket | ✅ |
| SDM670 | Warlock | ✅ |
| SDM690 | Lagoon | ✅ |

**骁龙 700 系列:**
| 芯片 | 代号 | 状态 |
|------|------|------|
| SDM710 | Warlock | ✅ |
| SDM720G | Atoll | ✅ |
| SDM750G | Lito | ✅ |
| SDM778G | Yupik | ✅ |

**骁龙 800 系列:**
| 芯片 | 代号 | 状态 |
|------|------|------|
| SDM845 | Sdm845 | ✅ |
| SDM855 | Msmnile | ✅ |
| SDM865 | Kona | ✅ |
| SDM888 | Lahaina | ✅ |

**骁龙 8 Gen 系列:**
| 芯片 | 代号 | 状态 |
|------|------|------|
| SM8450 | Waipio | ✅ |
| SM8475 | Waipio | ✅ |
| SM8550 | Kalama | ✅ |
| SM8650 | Pineapple | ✅ |

---

## 准备工作

### 1. 安装驱动

**Qualcomm HS-USB QDLoader 9008 驱动**

1. 下载 QPST 或独立驱动包
2. 安装 Qualcomm USB 驱动
3. 设备进入 9008 模式验证

**验证驱动安装:**
```
设备管理器 → 端口 (COM & LPT)
应显示: Qualcomm HS-USB QDLoader 9008 (COM*)
```

### 2. 获取 Loader (可选)

程序支持云端自动匹配，也可手动指定：

| Loader 类型 | 扩展名 | 说明 |
|-------------|--------|------|
| Programmer | .mbn/.elf | 标准 Firehose Loader |
| Signed Loader | .mbn | 签名版本 |

### 3. 设备进入 9008 模式

**方法一: 按键组合**
1. 完全关机
2. 同时按住 **音量+** 和 **音量-**
3. 连接 USB 数据线
4. 等待设备被识别

**方法二: ADB 命令**
```bash
adb reboot edl
```

**方法三: Fastboot 命令**
```bash
fastboot oem edl
# 或
fastboot reboot emergency
```

**方法四: 测试点短接**
- 部分设备有 EDL 测试点
- 短接测试点后连接 USB

---

## 基础操作

### 连接设备

1. 打开 MultiFlash Tool
2. 切换到 **Qualcomm** 标签页
3. 设备进入 9008 模式
4. 程序自动检测设备

**连接成功显示:**
```
[INFO] 检测到 9008 端口: COM12
[INFO] Sahara 握手中...
[SUCCESS] Sahara 连接成功
[INFO] 芯片 ID: 0x007150E1
[INFO] 正在匹配 Loader...
[SUCCESS] Loader 加载完成
```

### 读取分区表

1. 连接成功后
2. 点击 **"读取分区表"**
3. 显示 GPT 分区信息

**分区表示例:**
```
┌────────────┬────────────────┬──────────────┐
│ 分区名称   │ 起始扇区       │ 大小         │
├────────────┼────────────────┼──────────────┤
│ boot       │ 0x00010000     │ 64 MB        │
│ recovery   │ 0x00020000     │ 64 MB        │
│ system     │ 0x00030000     │ 4 GB         │
│ vendor     │ 0x00430000     │ 1 GB         │
│ ...        │ ...            │ ...          │
└────────────┴────────────────┴──────────────┘
```

### 备份分区

1. 选择要备份的分区
2. 点击 **"读取分区"**
3. 选择保存位置
4. 等待备份完成

**建议备份的分区:**
- `boot` - 启动镜像
- `recovery` - 恢复模式
- `modem` - 基带固件
- `persist` - 校准数据 (重要!)
- `config` - 设备配置
- `fsg` - 基带文件系统

### 刷写分区

1. 选择要刷写的分区
2. 点击 **"写入分区"**
3. 选择镜像文件
4. 确认刷写

**支持的镜像格式:**
- Raw 镜像 (`.img`, `.bin`)
- Sparse 镜像 (`.img`)
- 程序自动检测和转换

### 擦除分区

1. 选择要擦除的分区
2. 点击 **"擦除分区"**
3. 确认擦除

**危险分区警告:**
```
⚠️ 以下分区擦除后可能导致设备变砖:
- modemst1/modemst2 - 基带校准
- fsc/fsg - 基带文件
- persist - 设备校准
- config - 设备配置
```

---

## 云端 Loader 匹配

### 自动匹配

程序支持根据芯片 ID 自动从云端获取匹配的 Loader：

1. 设备连接后自动识别芯片
2. 查询云端数据库
3. 下载并加载匹配的 Loader

**匹配流程:**
```
芯片 ID → 云端查询 → 下载 Loader → 验证 → 加载
```

### 手动指定 Loader

如需使用特定 Loader：

1. 点击 **"选择 Loader"**
2. 选择 .mbn 或 .elf 文件
3. 重新连接设备

### 云端数据库

云端 Loader 数据库持续更新：

| 功能 | 说明 |
|------|------|
| 自动匹配 | 根据芯片 ID 匹配 |
| 版本管理 | 支持多版本 Loader |
| 实时更新 | 新设备支持及时添加 |

---

## 固件解密

### 支持的加密格式

| 格式 | 厂商 | 说明 |
|------|------|------|
| OFP | OPPO/一加 | OPPO 固件包 |
| OZIP | OPPO/一加 | 压缩固件包 |
| OPS | OPPO/一加 | 分区镜像 |

### OFP 解密

1. 点击 **"解密固件"**
2. 选择 OFP 文件
3. 等待解密完成
4. 输出解密后的镜像

**解密流程:**
```
OFP 文件 → 密钥匹配 → AES 解密 → 解压缩 → 输出镜像
```

### 智能密钥爆破

程序内置 50+ 组密钥，自动尝试匹配：

```
[INFO] 正在解密 OFP 文件...
[INFO] 尝试密钥 1/50...
[INFO] 尝试密钥 2/50...
[SUCCESS] 密钥匹配成功
[INFO] 正在解密...
[SUCCESS] 解密完成
```

---

## 高级功能

### Sahara 协议

Sahara 是高通设备的初始化协议：

**协议版本:**
| 版本 | 特性 |
|------|------|
| V2 | 基础功能 |
| V3 | 增强安全 |

**握手流程:**
```
1. 设备发送 HELLO
2. 主机响应 HELLO_RESP
3. 设备请求 Loader
4. 主机发送 Loader
5. 设备执行 Loader
```

### Firehose 协议

Firehose 是主要的刷写协议，使用 XML 命令：

**命令示例:**
```xml
<?xml version="1.0"?>
<data>
  <program SECTOR_SIZE_IN_BYTES="512"
           num_partition_sectors="131072"
           physical_partition_number="0"
           start_sector="2048" />
</data>
```

**支持的操作:**
| 命令 | 说明 |
|------|------|
| program | 刷写分区 |
| read | 读取分区 |
| erase | 擦除分区 |
| patch | 修补数据 |
| configure | 配置参数 |

### 存储类型检测

程序自动检测存储类型：

| 类型 | 说明 |
|------|------|
| eMMC | 嵌入式多媒体卡 |
| UFS | 通用闪存存储 |
| NAND | 裸 NAND 闪存 |

### 一键刷机

支持从 rawprogram XML 一键刷写：

1. 选择 rawprogram.xml 文件
2. 程序解析刷写配置
3. 自动刷写所有分区

---

## 常见问题

### Q: 设备无法进入 9008 模式?

**A:** 尝试以下方法:
1. 完全关机后尝试按键组合
2. 尝试 ADB/Fastboot 命令
3. 查找设备的 EDL 测试点
4. 检查 USB 连接

### Q: Sahara 握手失败?

**A:** 可能的原因:
1. 驱动未正确安装
2. USB 通信问题
3. 设备状态异常

**解决方案:**
- 重新安装驱动
- 尝试不同 USB 端口
- 重新进入 9008 模式

### Q: Loader 加载失败?

**A:**
1. 检查 Loader 文件是否完整
2. 确认 Loader 与芯片匹配
3. 尝试使用云端匹配
4. 查看详细错误日志

### Q: 刷写速度很慢?

**A:**
1. 检查 USB 连接 (建议 USB 3.0)
2. 避免使用 USB Hub
3. 关闭杀毒软件
4. 检查系统资源

### Q: 设备变砖怎么办?

**A:**
1. 尝试重新进入 9008 模式
2. 使用备份恢复关键分区
3. 刷写完整线刷包
4. 联系售后服务

### Q: 如何备份完整固件?

**A:**
1. 读取分区表
2. 逐个备份所有分区
3. 特别注意备份:
   - persist
   - modemst1/modemst2
   - fsg
   - config

---

## 参考资料

- [edl](https://github.com/bkerler/edl) - Qualcomm EDL 参考实现
- [QPST](https://qpsttool.com/) - 高通官方工具
- [Qualcomm 文档](https://www.qualcomm.com/) - 官方技术资源

---

<p align="center">
  <a href="QUICK_REFERENCE.md">快速参考</a> |
  <a href="TROUBLESHOOTING.md">常见问题</a> |
  <a href="../README.md">返回主页</a>
</p>
