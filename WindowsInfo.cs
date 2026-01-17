using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace OPFlashTool.Services
{
    public static class WindowsInfo
    {
        [DllImport("ntdll.dll", SetLastError = true)]
        private static extern int RtlGetVersion(ref RTL_OSVERSIONINFOW osVersionInfo);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RTL_OSVERSIONINFOW
        {
            public uint dwOSVersionInfoSize;
            public uint dwMajorVersion;
            public uint dwMinorVersion;
            public uint dwBuildNumber;
            public uint dwPlatformId;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;
        }

        public static Task<string> GetSystemInfoAsync()
        {
            return Task.Run(() => GetSystemInfo());
        }

        public static string GetSystemInfo()
        {
            var osvi = new RTL_OSVERSIONINFOW
            {
                dwOSVersionInfoSize = (uint)Marshal.SizeOf(typeof(RTL_OSVERSIONINFOW)),
                szCSDVersion = new string('\0', 128)
            };

            try
            {
                int result = RtlGetVersion(ref osvi);
                return result == 0
                    ? $"{GetWindowsVersion(osvi.dwMajorVersion, osvi.dwMinorVersion, osvi.dwBuildNumber)} {(Is64Bit() ? "64位" : "32位")}"
                    : "检测失败";
            }
            catch (Exception)
            {
                return "检测失败";
            }
        }

        public static bool Is64Bit()
        {
            return Environment.Is64BitOperatingSystem;
        }

        private static string GetWindowsVersion(uint major, uint minor, uint build)
        {
            switch (major)
            {
                case 10:
                    if (build >= 22000)
                        return "Windows 11";
                    else if (build >= 10240)
                        return "Windows 10";
                    else
                        return "Windows 10 预览版";
                case 6:
                    switch (minor)
                    {
                        case 0:
                            return "Windows Vista";
                        case 1:
                            return "Windows 7";
                        case 2:
                            return "Windows 8";
                        case 3:
                            return "Windows 8.1";
                        default:
                            return "未知NT系统";
                    }
                case 5:
                    switch (minor)
                    {
                        case 0:
                            return "Windows 2000";
                        case 1:
                            return "Windows XP";
                        case 2:
                            return "Windows Server 2003";
                        default:
                            return "Windows XP/Server 2003";
                    }
                default:
                    return "未知系统";
            }
        }
    }
}
