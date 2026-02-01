// ============================================================================
// CloudLoaderIntegration - cloud Loader Automatic matching集成示例
// 展示如何在 Form1.cs 中替换 PAK 资源为Cloud-based automatic matching
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using LoveAlways.Qualcomm.Database;
using LoveAlways.Qualcomm.UI;
using System;
using System.Drawing;
using System.Threading.Tasks;

namespace LoveAlways.Qualcomm.Services
{
    /// <summary>
    /// cloud Loader 集成帮助类
    /// 在 Form1.cs 中Use此类来实现Cloud-based automatic matching
    /// </summary>
    public static class CloudLoaderIntegration
    {
        /// <summary>
        /// 初始化cloud服务
        /// 在 Form1 构造函数中调用
        /// </summary>
        public static void Initialize(Action<string> log, Action<string> logDetail)
        {
            var service = CloudLoaderService.Instance;
            service.SetLogger(log, logDetail);

            // Config (可选)
            // service.ApiBase = "https://api.xiriacg.top/api";  // 生产环境
            // service.EnableCache = true;
            // service.TimeoutSeconds = 15;
        }

        /// <summary>
        /// Cloud-based automatic matchingconnect
        /// 替代原Have的 PAK 资源choose方式
        /// </summary>
        /// <param name="controller">Qualcomm控制器</param>
        /// <param name="deviceInfo">device information (Sahara Handshake后Get)</param>
        /// <param name="storageType">storageType</param>
        /// <param name="log">log回调</param>
        /// <returns>connectresult</returns>
        public static async Task<bool> ConnectWithCloudMatchAsync(
            QualcommUIController controller,
            SaharaDeviceInfo deviceInfo,
            string storageType,
            Action<string, Color> log)
        {
            var cloudService = CloudLoaderService.Instance;

            // 1. cloud matching
            log("[cloud] Matching Loader...", Color.Cyan);

            var result = await cloudService.MatchLoaderAsync(
                deviceInfo.MsmId,
                deviceInfo.PkHash,
                deviceInfo.OemId,
                storageType
            );

            if (result != null && result.Data != null)
            {
                // 2. match successfull，Usecloud Loader
                log($"[cloud] match successfull: {result.Filename}", Color.Green);
                log($"[cloud] Manufacturer: {result.Vendor}, Chip: {result.Chip}", Color.Blue);
                log($"[cloud] Confidence: {result.Confidence}%, Matching Type: {result.MatchType}", Color.Blue);

                // 3. 根据authTypechooseconnect方式
                string authMode = result.AuthType?.ToLower() switch
                {
                    "miauth" => "xiaomi",
                    "demacia" => "oneplus",
                    "vip" => "vip",
                    _ => "none"
                };

                // 4. connectdevice
                bool success = await controller.ConnectWithLoaderDataAsync(
                    storageType,
                    result.Data,
                    result.Filename,
                    authMode
                );

                // 5. 上报devicelog
                cloudService.ReportDeviceLog(
                    deviceInfo.MsmId,
                    deviceInfo.PkHash,
                    deviceInfo.OemId,
                    storageType,
                    success ? "success" : "failed"
                );

                return success;
            }
            else
            {
                // 6. Cloud no matches，回退 toLocal PAK
                log("[cloud] Not found match loader, try local resources...", Color.Yellow);

                // 上报未match
                cloudService.ReportDeviceLog(
                    deviceInfo.MsmId,
                    deviceInfo.PkHash,
                    deviceInfo.OemId,
                    storageType,
                    "not_found"
                );

                return await FallbackToLocalPakAsync(controller, deviceInfo, storageType, log);
            }
        }

        /// <summary>
        /// 回退 toLocal PAK 资源
        /// </summary>
        private static async Task<bool> FallbackToLocalPakAsync(
            QualcommUIController controller,
            SaharaDeviceInfo deviceInfo,
            string storageType,
            Action<string, Color> log)
        {
            // Check Local PAK yesnoAvailable
            if (!EdlLoaderDatabase.IsPakAvailable())
            {
                log("[Local] edl_loaders.pak does not exist", Color.Red);
                return false;
            }

            // try按 HW ID (MSM ID) match
            var loaders = EdlLoaderDatabase.GetByChip(deviceInfo.MsmId);
            if (loaders.Length > 0)
            {
                var loader = loaders[0];
                var data = EdlLoaderDatabase.LoadLoader(loader.Id);

                if (data != null)
                {
                    log($"[Local] Use: {loader.Name}", Color.Cyan);
                    return await controller.ConnectWithLoaderDataAsync(
                        storageType,
                        data,
                        loader.Name,
                        loader.AuthMode ?? "none"
                    );
                }
            }

            log("[Local] Try to find matches loader", Color.Red);
            return false;
        }
    }

    /// <summary>
    /// Sahara device information (fromHandshakeProtocolGet)
    /// </summary>
    public class SaharaDeviceInfo
    {
        public string MsmId { get; set; }       // 如 "009600E1"
        public string PkHash { get; set; }      // 64 字符
        public string OemId { get; set; }       // 如 "0x0001"
        public string HwId { get; set; }
        public string Serial { get; set; }
        public bool IsUfs { get; set; }
    }
}

/*
================================================================================
                            Form1.cs 集成示例
================================================================================

1. 在 Form1.cs 顶部添加引用：
   using LoveAlways.Qualcomm.Services;
   using LoveAlways.Qualcomm.Integration;

2. 在 Form1 构造函数中初始化cloud服务：
   
   public Form1()
   {
       InitializeComponent();
       
       // 初始化cloud Loader 服务
       CloudLoaderIntegration.Initialize(
           msg => AppendLog(msg, Color.Blue),
           msg => AppendLog(msg, Color.Gray)
       );
   }

3. 修改connect方法，添加Cloud-based automatic matching选Item：

   private async Task<bool> ConnectQualcommDeviceAsync()
   {
       // Check yesnoEnableCloud-based automatic matching
       if (checkbox_CloudMatch.Checked)  // 添加一个复选框控制
       {
           // 先Getdevice information (Sahara Handshake)
           var deviceInfo = await GetSaharaDeviceInfoAsync();
           
           if (deviceInfo != null)
           {
               return await CloudLoaderIntegration.ConnectWithCloudMatchAsync(
                   _qualcommController,
                   deviceInfo,
                   _storageType,
                   (msg, color) => AppendLog(msg, color)
               );
           }
       }
       
       // 原Have的 PAK 资源chooselogic
       return await ConnectWithSelected LoaderAsync();
   }

4. or者更简单的方式，直接Use CloudLoaderService：

   private async Task<bool> ConnectWithAutoMatchAsync()
   {
       var cloud = CloudLoaderService.Instance;
       
       // Getdevice information后调用
       var result = await cloud.MatchLoaderAsync(
           deviceInfo.MsmId,
           deviceInfo.PkHash,
           deviceInfo.OemId,
           "ufs"
       );
       
       if (result?.Data != null)
       {
           AppendLog($"cloud matching: {result.Filename}", Color.Green);
           return await _qualcommController.ConnectWithLoaderDataAsync(
               "ufs", result.Data, result.Filename, "none");
       }
       
       return false;
   }

================================================================================
                          Delete PAK 资源相关代码
================================================================================

如果完全Usecloudmatch，可以Delete以下file/代码：

1. Deletefile:
   - edl_loaders.pak (约 50-100MB)
   
2. 可选保留以下代码作为离线回退:
   - Qualcomm/Database/edl_loader_database.cs (保留元数据，Delete PAK loadlogic)
   
3. 修改 Form1.cs:
   - Delete PAK Available性Check 相关代码
   - Delete EDL Loader 下拉列表构建代码 (or改为fromcloudGet列表)
   - 将connectlogic改为cloud优先

================================================================================
*/
