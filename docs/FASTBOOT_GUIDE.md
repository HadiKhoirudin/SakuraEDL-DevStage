# Fastboot 刷机教程

> 本文档详细介绍如何使用 MultiFlash Tool 的 Fastboot 功能

## 目录

- [概述](#概述)
- [准备工作](#准备工作)
- [基础操作](#基础操作)
- [Bootloader 解锁](#bootloader-解锁)
- [分区操作](#分区操作)
- [高级功能](#高级功能)
- [常见问题](#常见问题)

---

## 概述

Fastboot 是 Android 设备的标准刷机模式，MultiFlash Tool 提供增强的 Fastboot 功能：

- **设备检测**: 自动检测 Fastboot 设备
- **分区管理**: 读取、刷写、擦除分区
- **Bootloader**: 解锁/重锁操作
- **设备信息**: 读取详细设备信息
- **Payload 支持**: 直接刷写 payload.bin

### 工作流程

```
设备 ──► Fastboot 模式 ──► 连接检测 ──► 分区操作
           │                  │            │
           └─ 按键/ADB        └─ USB       └─ flash/erase
```

---

## 准备工作

### 1. 安装驱动

**Google USB Driver**

1. 下载 [Google USB Driver](https://developer.android.com/studio/run/win-usb)
2. 解压并安装驱动
3. 或通过 Android SDK 安装

**验证驱动安装:**
```
设备管理器 → Android Device
应显示: Android Bootloader Interface
```

### 2. 设备进入 Fastboot 模式

**方法一: 按键组合**

| 品牌 | 按键组合 |
|------|----------|
| 小米/红米 | 音量- + 电源 |
| OPPO/一加 | 音量- + 电源 |
| 三星 | 音量- + Bixby + 电源 |
| Google | 音量- + 电源 |
| 华为 | 音量- + 电源 |

**方法二: ADB 命令**
```bash
adb reboot bootloader
```

**方法三: 恢复模式**
- 部分设备可从恢复模式进入

---

## 基础操作

### 检测设备

1. 打开 MultiFlash Tool
2. 切换到 **Fastboot** 标签页
3. 设备进入 Fastboot 模式
4. 点击 **"刷新设备"**

**检测成功显示:**
```
[SUCCESS] 检测到 Fastboot 设备
[INFO] 序列号: abc123def456
[INFO] 产品: Device_Name
[INFO] Bootloader: locked/unlocked
```

### 读取设备信息

点击 **"读取信息"** 获取详细设备信息：

```
┌─────────────────────────────────────┐
│ 设备信息                            │
├─────────────────────────────────────┤
│ 序列号: abc123def456                │
│ 产品名: device_name                 │
│ 变体: variant                       │
│ Bootloader: locked                  │
│ 安全状态: secure                    │
│ 防回滚版本: 1                       │
│ 基带版本: 1.0.0                     │
└─────────────────────────────────────┘
```

### 设备变量查询

常用 Fastboot 变量：

| 变量 | 说明 |
|------|------|
| `product` | 产品名称 |
| `serialno` | 序列号 |
| `secure` | 安全启动状态 |
| `unlocked` | 解锁状态 |
| `variant` | 设备变体 |
| `current-slot` | 当前槽位 (A/B) |
| `slot-count` | 槽位数量 |

---

## Bootloader 解锁

### 解锁前准备

⚠️ **重要提示:**
1. 解锁会清除所有数据
2. 部分品牌需要申请解锁码
3. 解锁可能影响保修

### 通用解锁步骤

1. 开启 OEM 解锁
   ```
   设置 → 关于手机 → 连续点击版本号 → 开发者选项 → OEM 解锁
   ```

2. 设备进入 Fastboot 模式

3. 执行解锁命令
   ```
   点击 "解锁 Bootloader" 按钮
   ```

4. 设备上确认解锁

### 品牌特定说明

**小米/红米:**
- 需要绑定账号并等待
- 使用官方解锁工具或本工具

**OPPO/一加:**
- 需要申请深度测试
- 或使用第三方方法

**Google Pixel:**
- 直接支持 OEM 解锁
- 无需额外申请

### 重新锁定

```
点击 "锁定 Bootloader" 按钮
```

⚠️ 锁定前确保系统完整，否则可能变砖

---

## 分区操作

### 刷写分区

1. 选择要刷写的分区
2. 点击 **"刷写分区"**
3. 选择镜像文件
4. 等待刷写完成

**常用分区:**

| 分区 | 说明 | 文件 |
|------|------|------|
| boot | 启动镜像 | boot.img |
| recovery | 恢复模式 | recovery.img |
| system | 系统分区 | system.img |
| vendor | 供应商分区 | vendor.img |
| vbmeta | 验证启动 | vbmeta.img |

### A/B 分区

新设备使用 A/B 分区方案：

```
boot_a / boot_b
system_a / system_b
vendor_a / vendor_b
```

**切换槽位:**
```
点击 "切换槽位" 按钮
```

**刷写到特定槽位:**
```
fastboot flash boot_a boot.img
fastboot flash boot_b boot.img
```

### 擦除分区

1. 选择要擦除的分区
2. 点击 **"擦除分区"**
3. 确认操作

**常用擦除:**
- `userdata` - 清除用户数据
- `cache` - 清除缓存
- `frp` - 清除恢复出厂保护

### 格式化数据

```
点击 "格式化 Data" 按钮
```

这会清除加密的用户数据分区。

---

## 高级功能

### Payload.bin 刷写

支持直接刷写 OTA payload.bin：

1. 点击 **"加载 Payload"**
2. 选择 payload.bin 文件
3. 程序解析并显示分区列表
4. 选择要刷写的分区
5. 点击 **"刷写选中"**

**Payload 结构:**
```
payload.bin
├── boot.img
├── system.img
├── vendor.img
├── ...
└── metadata
```

### 自定义命令

执行自定义 Fastboot 命令：

1. 在命令输入框输入命令
2. 点击 **"执行"**

**常用命令:**
```bash
# 查看所有变量
getvar all

# 重启到系统
reboot

# 重启到恢复模式
reboot recovery

# 重启到 Fastboot
reboot bootloader

# 临时启动镜像
boot boot.img
```

### OEM 命令

设备特定的 OEM 命令：

| 命令 | 说明 |
|------|------|
| `oem unlock` | 解锁 Bootloader |
| `oem lock` | 锁定 Bootloader |
| `oem device-info` | 设备信息 |
| `oem get_unlock_ability` | 解锁能力 |

### 临时启动

不刷写直接启动镜像：

```
点击 "临时启动" → 选择 boot.img
```

用于测试自定义内核或恢复镜像。

---

## 常见问题

### Q: 设备未检测到?

**A:** 检查以下项目:
1. 驱动是否正确安装
2. USB 连接是否正常
3. 设备是否在 Fastboot 模式
4. 尝试不同 USB 端口

### Q: 命令执行失败?

**A:** 可能的原因:
1. Bootloader 已锁定
2. 分区不存在
3. 镜像文件问题

**解决方案:**
- 检查 Bootloader 状态
- 确认分区名称正确
- 验证镜像文件完整

### Q: 解锁后无法开机?

**A:**
1. 解锁会清除数据，这是正常的
2. 等待设备完成初始化
3. 如果卡在开机画面，尝试恢复出厂设置

### Q: 刷写失败 "FAILED (remote)"?

**A:** 常见原因:
1. Bootloader 锁定: `FAILED (remote: Device is locked)`
2. 验证失败: `FAILED (remote: Verify failed)`
3. 空间不足: `FAILED (remote: Not enough space)`

### Q: 如何修复变砖设备?

**A:**
1. 尝试进入 Fastboot 模式
2. 刷写原厂镜像
3. 如果无法进入 Fastboot，尝试 EDL 模式
4. 联系售后服务

### Q: A/B 分区如何操作?

**A:**
1. 查询当前槽位: `getvar current-slot`
2. 刷写到两个槽位确保完整
3. 可手动切换槽位测试

---

## 命令参考

### 基础命令

```bash
# 设备信息
fastboot devices
fastboot getvar all

# 刷写
fastboot flash <分区> <文件>
fastboot flash boot boot.img

# 擦除
fastboot erase <分区>
fastboot erase userdata

# 重启
fastboot reboot
fastboot reboot bootloader
fastboot reboot recovery
```

### 高级命令

```bash
# 解锁/锁定
fastboot oem unlock
fastboot oem lock
fastboot flashing unlock
fastboot flashing lock

# A/B 槽位
fastboot set_active a
fastboot set_active b

# 临时启动
fastboot boot boot.img

# 更新
fastboot update update.zip
fastboot flashall
```

---

## 参考资料

- [Android Fastboot 文档](https://source.android.com/docs/core/architecture/bootloader)
- [Google USB Driver](https://developer.android.com/studio/run/win-usb)
- [Platform Tools](https://developer.android.com/studio/releases/platform-tools)

---

<p align="center">
  <a href="QUICK_REFERENCE.md">快速参考</a> |
  <a href="TROUBLESHOOTING.md">常见问题</a> |
  <a href="../README.md">返回主页</a>
</p>
