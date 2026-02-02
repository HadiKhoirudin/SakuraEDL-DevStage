using LoveAlways.Spreadtrum.Resources;
using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LoveAlways
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            // Check command line arguments
            if (args.Length > 0)
            {
                if (ProcessCommandLine(args))
                    return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Show splash form, then start main form after it closes
            using (var splash = new SplashForm())
            {
                splash.ShowDialog();
            }

            Application.Run(new Form1());
        }

        /// <summary>
        /// Process command line arguments
        /// </summary>
        private static bool ProcessCommandLine(string[] args)
        {
            if (args[0] == "--build-pak" && args.Length >= 3)
            {
                // 构建统一资源包 (SPAK v2)
                string sourceDir = args[1];
                string outputPath = args[2];
                bool compress = args.Length < 4 || args[3] != "--no-compress";

                Console.WriteLine("=== Build SPD Resource Pack (SPAK v2) ===");
                Console.WriteLine("Source Directory: " + sourceDir);
                Console.WriteLine("Output File: " + outputPath);
                Console.WriteLine("Compression: " + (compress ? "Yes" : "No"));
                Console.WriteLine();

                try
                {
                    SprdPakManager.BuildPak(sourceDir, outputPath, compress);
                    Console.WriteLine("Build Success!");

                    if (File.Exists(outputPath))
                    {
                        var info = new FileInfo(outputPath);
                        Console.WriteLine("File Size: " + FormatSize(info.Length));
                    }

                    // Load and show statistics
                    if (SprdPakManager.LoadPak(outputPath))
                    {
                        Console.WriteLine("Entry Count: " + SprdPakManager.EntryCount);
                        Console.WriteLine("Chip List: " + string.Join(", ", SprdPakManager.GetChipNames()));
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Build Failed: " + ex.Message);
                    return true;
                }
            }
            else if (args[0] == "--build-fdl-pak" && args.Length >= 3)
            {
                // 构建 FDL 资源包 (旧格式，向后兼容)
                string sourceDir = args[1];
                string outputPath = args[2];
                bool compress = args.Length < 4 || args[3] != "--no-compress";

                Console.WriteLine("=== Build FDL Resource Pack (FPAK) ===");
                Console.WriteLine("Source Directory: " + sourceDir);
                Console.WriteLine("Output File: " + outputPath);
                Console.WriteLine();

                try
                {
                    FdlPakManager.BuildPak(sourceDir, outputPath, compress);
                    Console.WriteLine("Build Success!");

                    if (File.Exists(outputPath))
                    {
                        var info = new FileInfo(outputPath);
                        Console.WriteLine("File Size: " + FormatSize(info.Length));
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Build Failed: " + ex.Message);
                    return true;
                }
            }
            else if (args[0] == "--extract-pak" && args.Length >= 3)
            {
                // 解包资源包
                string pakPath = args[1];
                string outputDir = args[2];

                Console.WriteLine("=== Extract Resource Pack ===");
                Console.WriteLine("Resource Pack: " + pakPath);
                Console.WriteLine("Output Directory: " + outputDir);
                Console.WriteLine();

                try
                {
                    // 根据文件头判断格式
                    using (var fs = new FileStream(pakPath, FileMode.Open, FileAccess.Read))
                    {
                        var magic = new byte[4];
                        fs.Read(magic, 0, 4);
                        var magicStr = System.Text.Encoding.ASCII.GetString(magic);

                        if (magicStr == "SPAK")
                        {
                            Console.WriteLine("Format: SPAK v2");
                            SprdPakManager.ExtractPak(pakPath, outputDir);
                        }
                        else if (BitConverter.ToUInt32(magic, 0) == 0x4B415046) // "FPAK"
                        {
                            Console.WriteLine("Format: FPAK (FDL)");
                            FdlPakManager.ExtractPak(pakPath, outputDir);
                        }
                        else
                        {
                            Console.WriteLine("Error: Unknown resource pack format");
                            return true;
                        }
                    }

                    Console.WriteLine("Extraction Success!");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Extraction Failed: " + ex.Message);
                    return true;
                }
            }
            else if (args[0] == "--export-index")
            {
                // 导出 FDL 索引
                string outputPath = args.Length >= 2 ? args[1] : "fdl_index.json";
                string format = args.Length >= 3 ? args[2] : "json";

                Console.WriteLine("=== Export FDL Index ===");
                Console.WriteLine("Output File: " + outputPath);
                Console.WriteLine();

                try
                {
                    FdlIndex.InitializeFromDatabase();

                    if (format.ToLower() == "csv")
                    {
                        FdlIndex.ExportCsv(outputPath);
                    }
                    else
                    {
                        FdlIndex.ExportIndex(outputPath);
                    }

                    // Show statistics
                    var stats = FdlIndex.GetStatistics();
                    Console.WriteLine(stats.ToString());

                    Console.WriteLine("Export Success!");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Export Failed: " + ex.Message);
                    return true;
                }
            }
            else if (args[0] == "--list-devices")
            {
                // 列出设备
                string filter = args.Length >= 2 ? args[1] : null;

                Console.WriteLine("=== FDL Device List ===");
                Console.WriteLine();

                FdlIndex.InitializeFromDatabase();

                FdlIndex.FdlIndexEntry[] entries;
                if (!string.IsNullOrEmpty(filter))
                {
                    entries = FdlIndex.Search(filter);
                    Console.WriteLine($"Search: {filter}");
                }
                else
                {
                    entries = FdlIndex.GetAllEntries();
                }

                Console.WriteLine($"Total {entries.Length} devices");
                Console.WriteLine();
                Console.WriteLine("{0,-12} {1,-20} {2,-12} {3,-12} {4,-12}",
                    "Chip", "Model", "Brand", "FDL1 Addr", "FDL2 Addr");
                Console.WriteLine(new string('-', 70));

                foreach (var entry in entries.Take(100))
                {
                    Console.WriteLine("{0,-12} {1,-20} {2,-12} 0x{3:X8} 0x{4:X8}",
                        entry.ChipName,
                        entry.DeviceModel.Length > 18 ? entry.DeviceModel.Substring(0, 18) + ".." : entry.DeviceModel,
                        entry.Brand,
                        entry.Fdl1Address,
                        entry.Fdl2Address);
                }

                if (entries.Length > 100)
                {
                    Console.WriteLine($"... more {entries.Length - 100} devices");
                }

                return true;
            }
            else if (args[0] == "--help" || args[0] == "-h")
            {
                ShowHelp();
                return true;
            }

            return false;
        }

        private static void ShowHelp()
        {
            Console.WriteLine("LoveAlways - Spreadtrum/Qualcomm Multi-Flash Tool");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  MultiFlash.exe                        Start GUI");
            Console.WriteLine();
            Console.WriteLine("Resource Pack Commands:");
            Console.WriteLine("  --build-pak <source_dir> <output_file> [--no-compress]");
            Console.WriteLine("      Build unified resource pack (SPAK v2)");
            Console.WriteLine();
            Console.WriteLine("  --build-fdl-pak <source_dir> <output_file> [--no-compress]");
            Console.WriteLine("      Build FDL resource pack (FPAK)");
            Console.WriteLine();
            Console.WriteLine("  --extract-pak <pak_file> <output_dir>");
            Console.WriteLine("      Extract resource pack");
            Console.WriteLine();
            Console.WriteLine("Index Commands:");
            Console.WriteLine("  --export-index [output_file] [json|csv]");
            Console.WriteLine("      Export FDL device index");
            Console.WriteLine();
            Console.WriteLine("  --list-devices [search_term]");
            Console.WriteLine("      List/Search supported devices");
            Console.WriteLine();
            Console.WriteLine("  --help");
            Console.WriteLine("      Show help");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  MultiFlash.exe --build-pak SprdResources\\sprd_fdls SprdResources\\sprd.pak");
            Console.WriteLine("  MultiFlash.exe --export-index fdl_index.json");
            Console.WriteLine("  MultiFlash.exe --list-devices Samsung");
            Console.WriteLine("  MultiFlash.exe --list-devices SC8541E");
        }

        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return string.Format("{0:0.##} {1}", size, sizes[order]);
        }
    }
}
