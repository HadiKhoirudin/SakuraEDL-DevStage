# MultiFlash Tool 快速参考

> 本文档提供 MultiFlash Tool 的快速使用指南

## 目录

- [快速开始](#快速开始)
- [平台选择](#平台选择)
- [常用操作](#常用操作)
- [快捷键](#快捷键)

---

## 快速开始

### 1. 环境准备

```
✅ Windows 10/11 64-bit
✅ .NET Framework 4.8
✅ 对应平台驱动
```

### 2. 驱动安装

| 平台 | 驱动名称 | 下载链接 |
|------|----------|----------|
| 高通 EDL | Qualcomm HS-USB QDLoader 9008 | [下载](https://qpsttool.com/qpst-tool-v2-7-496) |
| 联发科 | MediaTek PreLoader USB VCOM | [下载](https://spflashtool.com/) |
| 展讯 | SPRD USB Serial | 随 PAC 固件附带 |
| Fastboot | Google USB Driver | [下载](https://developer.android.com/studio/run/win-usb) |

### 3. 设备模式进入方法

| 平台 | 进入方法 |
|------|----------|
| **高通 EDL** | 关机 → 按住音量+和音量- → 连接USB |
| **MTK BROM** | 关机 → 按住音量- → 连接USB |
| **SPD 下载** | 关机 → 按住音量- → 连接USB |
| **Fastboot** | 关机 → 按住音量- + 电源键 |

---

## 平台选择

### 如何判断设备平台？

**方法1: 查看设备管理器**
- `Qualcomm HS-USB QDLoader 9008` → 高通 EDL
- `MediaTek PreLoader USB VCOM` → 联发科
- `SPRD USB Serial` → 展讯
- `Android Fastboot` → Fastboot

**方法2: 查看设备信息**
- 设置 → 关于手机 → 处理器信息
- 骁龙/Snapdragon → 高通
- Helio/天玑/Dimensity → 联发科
- 虎贲/展锐 → 展讯

---

## 常用操作

### 高通 EDL 模式

```
1. 设备进入 9008 模式
2. 选择 Loader 文件 (或使用云端匹配)
3. 选择操作:
   - 读取分区表
   - 备份分区
   - 刷写分区
   - 擦除分区
```

### 联发科 MTK 模式

```
1. 选择 DA 文件 (可选,有内置)
2. 点击"连接设备"
3. 设备进入 BROM 模式 (音量-连接)
4. 等待 DA 加载完成
5. 选择操作:
   - 读取分区表
   - 备份/刷写/擦除分区
```

### 展讯 SPD 模式

```
1. 选择芯片型号 (如 T760)
2. 加载 PAC 固件 或 手动选择 FDL
3. 点击"连接设备"
4. 设备进入下载模式
5. 选择操作
```

### Fastboot 模式

```
1. 设备进入 Fastboot 模式
2. 点击"刷新设备"
3. 选择操作:
   - 读取设备信息
   - 解锁 Bootloader
   - 刷写镜像
```

---

## 快捷键

| 快捷键 | 功能 |
|--------|------|
| `Ctrl+O` | 打开文件 |
| `Ctrl+S` | 保存日志 |
| `Ctrl+L` | 清除日志 |
| `F5` | 刷新设备 |
| `Esc` | 取消操作 |

---

## 状态指示

| 状态 | 含义 |
|------|------|
| 🟢 绿色 | 操作成功 |
| 🟡 黄色 | 等待/进行中 |
| 🔴 红色 | 错误/失败 |
| 🔵 蓝色 | 信息提示 |

---

## 日志级别

| 级别 | 说明 |
|------|------|
| `[INFO]` | 一般信息 |
| `[SUCCESS]` | 操作成功 |
| `[ERROR]` | 错误信息 |
| `[DEBUG]` | 调试信息 |
| `[HEX]` | 十六进制数据 |

---

## 相关文档

- [MTK 详细教程](MTK_GUIDE.md)
- [SPD 详细教程](SPD_GUIDE.md)
- [高通 EDL 教程](QUALCOMM_GUIDE.md)
- [Fastboot 教程](FASTBOOT_GUIDE.md)
- [常见问题](TROUBLESHOOTING.md)

---

<p align="center">
  <a href="../README.md">返回主页</a>
</p>
