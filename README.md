<p align="center">
  <img src="https://github.com/xiriovo/SakuraEDL/blob/v2.0.0/assets/logo.jpg" alt="MultiFlash Tool Logo" width="128">
</p>

# MultiFlash Tool

**An open-source, multi-functional Android flashing tool**

Supports Qualcomm EDL (9008), MediaTek (MTK), Spreadtrum (SPD/Unisoc), and Fastboot modes.

[![License: CC BY-NC-SA 4.0](https://img.shields.io/badge/License-CC%20BY--NC--SA%204.0-lightgrey.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-4.8-blue.svg)](https://dotnet.microsoft.com/)
[![GitHub Stars](https://img.shields.io/github/stars/xiriovo/MultiFlash-Tool)](https://github.com/xiriovo/MultiFlash-Tool/stargazers)
[![GitHub Forks](https://img.shields.io/github/forks/xiriovo/MultiFlash-Tool)](https://github.com/xiriovo/MultiFlash-Tool/network/members)
[![GitHub Release](https://img.shields.io/github/v/release/xiriovo/MultiFlash-Tool)](https://github.com/xiriovo/MultiFlash-Tool/releases)

[Quick Reference](docs/QUICK_REFERENCE.md)

---

## 🎯 Project Highlights

| 🚀 **Multi-Platform Support** | ⚡ **Dual Protocol Engines** | 🛠️ **Comprehensive Features** | ☁️ **Cloud Matching** |
|:---:|:---:|:---:|:---:|
| Qualcomm + MTK + Spreadtrum | XFlash + XML Protocol | Flash + Unbrick + Decrypt | Auto Loader Matching |

## 📸 Screenshoot

[![image.png](https://i.postimg.cc/tCPTj0xB/image.png)](https://postimg.cc/svjyYNKW)

---

## ✨ Features

### 🆕 v3.0 New Features
- **Note From Me (Hadi Khoirudin, S.Kom) - 02/02/2026**
  - This is forked version is not ready for production and I'm just tring to translate them & create small fix as I can / available
  - Use this as an learning resources

#### 🔧 Full MediaTek (MTK) Support
- **BROM/Preloader Mode Flashing**
  - Automatic BROM and Preloader mode detection
  - Smart DA (Download Agent) loading
  - Support for separated DA1 + DA2 files
- **Dual Protocol Engines**
  - XFlash binary protocol (referencing mtkclient)
  - XML V6 protocol (new device compatible)
  - Automatic protocol selection and fallback
- **CRC32 Checksum Support**
  - Data transfer integrity verification
  - Compatible with mtkclient
- **Vulnerability Exploitation**
  - Carbonara exploit (DA1 level)
  - AllinoneSignature exploit (DA2 level)
  - Automatic detection and execution

#### 📱 Spreadtrum (SPD/Unisoc) Support
- **FDL Download Protocol**
  - Automatic FDL1/FDL2 downloading
  - HDLC frame encoding
  - Dynamic baud rate switching
- **PAC Firmware Parsing**
  - Automatic PAC package parsing
  - Extract FDL and partition images
- **Signature Bypass (T760/T770)**
  - `custom_exec_no_verify` mechanism
  - Supports flashing unsigned FDLs
- **Chip Database**
  - SC9863A, T606, T610, T618
  - T700, T760 ✓Verified, T770
  - Automatic address configuration

#### ☁️ Cloud Loader Matching (Qualcomm)
- **Automatic Matching**
  - Auto-fetch Loader based on Chip ID
  - No local PAK resource pack needed
- **API Integration**
  - Cloud Loader database
  - Real-time update support

### 📊 Protocol Comparison

| Feature | XML Protocol | XFlash Protocol |
|------|:--------:|:-----------:|
| Partition Table Read | ✅ | ✅ |
| Partition Read | ✅ | ✅ |
| Partition Write | ✅ | ✅ |
| CRC32 Checksum | ❌ | ✅ |
| Compatibility | New Devices | All Devices |

### Core Features

#### 📱 Qualcomm EDL (9008) Mode
- Sahara V2/V3 protocol support
- Enhanced Firehose protocol flashing
- GPT partition table backup/restore
- Automatic storage type detection (eMMC/UFS/NAND)
- OFP/OZIP/OPS firmware decryption
- Smart key brute-force (50+ key sets)

#### ⚡ Fastboot Enhanced
- Partition read/write operations
- OEM unlock/relock
- Device information query
- Custom command execution

#### 🔧 MediaTek (MTK)
- BROM/Preloader mode
- XFlash + XML dual protocols
- DA auto-loading
- Vulnerability exploits (Carbonara/AllinoneSignature)

#### 📱 Spreadtrum (SPD/Unisoc)
- FDL1/FDL2 downloading
- PAC firmware parsing
- T760/T770 signature bypass

#### 📦 Firmware Tools
- Payload.bin extraction
- Super partition merging
- Sparse/Raw image conversion
- rawprogram XML parsing

---

## 📋 System Requirements

### Minimum Configuration
- **OS**: Windows 10 (64-bit) or higher
- **Runtime**: .NET Framework 4.8
- **RAM**: 4GB
- **Storage**: 500MB free space

### Driver Requirements
| Platform | Driver | Purpose |
|------|------|------|
| Qualcomm | Qualcomm HS-USB | 9008 mode |
| MediaTek | MediaTek PreLoader | BROM mode |
| Spreadtrum | SPRD USB | Download mode |
| Generic | ADB/Fastboot | Debug mode |

---

## 🚀 Quick Start

### Installation Steps

1. **Download the Program**
   - Download the latest version from [Releases](https://github.com/xiriovo/MultiFlash-Tool/releases)
   - Extract to any directory (recommended English path)

2. **Install Drivers**
   - Install the corresponding drivers for your device platform

3. **Run the Program**
MultiFlash.exe

### Usage Examples

#### 🔧 MediaTek (MTK) Flashing

1. Select DA file (or use built-in DA)
2. Power off device, hold volume key and connect USB
3. Program automatically completes:
- BROM handshake
- DA loading (XFlash/XML protocol)
- Partition table reading
4. Select partitions for read/write/erase

#### 📱 Spreadtrum (SPD) Flashing

1. Select chip model (e.g., T760)
2. Load PAC firmware or manually select FDL files
3. Boot device into download mode
4. Click "Read Partition Table"
5. Select partitions for flashing

#### 🔐 Qualcomm EDL Mode

1. Boot device into 9008 mode
2. Select Programmer file (.mbn/.elf)
3. Select firmware package or partition images
4. Click "Start Flashing"

---

## 🛠️ Tech Stack

- **Runtime**: .NET Framework 4.8
- **UI Framework**: AntdUI
- **MTK Protocol**: Referencing [mtkclient](https://github.com/bkerler/mtkclient)
- **SPD Protocol**: Referencing [spd_dump](https://github.com/ArtRichards/spd_dump)

### Project Structure
```
MultiFlash-Tool/
├── MediaTek/ # 🆕 MediaTek Module
│ ├── Protocol/
│ │ ├── brom_client.cs # BROM Client
│ │ ├── xml_da_client.cs # XML V6 Protocol
│ │ ├── xflash_client.cs # XFlash Binary Protocol
│ │ └── xflash_commands.cs # XFlash Command Codes
│ ├── Common/
│ │ ├── mtk_crc32.cs # CRC32 Checksum
│ │ └── mtk_checksum.cs # Data Packing
│ ├── Services/
│ │ └── mediatek_service.cs # MTK Service
│ ├── Exploit/
│ │ ├── carbonara_exploit.cs
│ │ └── AllinoneSignatureExploit.cs
│ └── Database/
│ └── mtk_chip_database.cs
├── Spreadtrum/ # 🆕 Spreadtrum Module
│ ├── Protocol/
│ │ ├── fdl_client.cs # FDL Client
│ │ ├── hdlc_protocol.cs # HDLC Encoding
│ │ └── bsl_commands.cs # BSL Commands
│ ├── Services/
│ │ └── spreadtrum_service.cs
│ └── Database/
│ └── sprd_fdl_database.cs
├── Qualcomm/ # Qualcomm Module
│ ├── SaharaProtocol.cs
│ ├── FirehoseProtocol.cs
│ └── Services/
│ └── cloud_loader_integration.cs # Cloud Matching
├── Fastboot/ # Fastboot Module
├── Authentication/ # Authentication Policies
├── Services/ # Common Services
└── Localization/ # Multi-language
```
---

## 📊 Supported Chips

### MediaTek (MTK)
| Chip | HW Code | Exploit | Status |
|------|---------|------|------|
| MT6765 | 0x0766 | Carbonara | ✅ |
| MT6768 | 0x0788 | Carbonara | ✅ |
| MT6781 | 0x0813 | AllinoneSignature | ✅ |
| MT6833 | 0x0816 | AllinoneSignature | ✅ |
| MT6853 | 0x0788 | Carbonara | ✅ |

### Spreadtrum (SPD/Unisoc)
| Chip | exec_addr | Status |
|------|-----------|------|
| SC9863A | 0x5500 | ✅ |
| T606/T610/T618 | 0x5500 | ✅ |
| T700 | 0x65012f48 | ✅ |
| T760 | 0x65012f48 | ✅ Verified |
| T770 | 0x65012f48 | ✅ |

### Qualcomm
- SDM Series (660, 710, 845, 855, 865, 888)
- SM Series (8150, 8250, 8350, 8450, 8550)
- Cloud auto-matching for Loaders

---

## ❓ FAQ

### MTK device not recognized?
- Confirm MediaTek PreLoader driver is installed
- Try holding volume down while connecting after power off
- Check if the device supports BROM mode

### SPD device signature verification failed?
- Confirm `custom_exec_no_verify_XXXXXXXX.bin` file exists
- Check if FDL address configuration is correct
- T760/T770 require specific exploit files

### XFlash protocol failed?
- Program will automatically fall back to XML protocol
- Check if DA files are complete
- Check logs for error details

---

## 📄 License

This project uses a **Non-Commercial License** - see the [LICENSE](LICENSE) file for details.

- ✅ Permitted for personal learning and research
- ✅ Permitted to modify and distribute (must keep same license)
- ❌ Prohibited for any commercial use
- ❌ Prohibited from sale or use for profit

---

## 📧 Contact

### Community
- **QQ Group**: [MultiFlash TOOL](https://qm.qq.com/q/z3iVnkm22c)
- **Telegram**: [OPFlashTool](https://t.me/OPFlashTool)
- **Discord**: [Join Server](https://discord.gg/multiflash)

### Developer
- **GitHub**: [@xiriovo](https://github.com/xiriovo)
- **Email**: 1708298587@qq.com

---

## 🙏 Acknowledgments

- [mtkclient](https://github.com/bkerler/mtkclient) - MTK protocol reference
- [spd_dump](https://github.com/ArtRichards/spd_dump) - SPD protocol reference
- [edl](https://github.com/bkerler/edl) - Qualcomm EDL reference

---

<p align="center">
  Made with ❤️ by MultiFlash Tool Team<br>
  Copyright © 2025-2026 MultiFlash Tool. All rights reserved.
</p>
