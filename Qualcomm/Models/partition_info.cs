// ============================================================================
// LoveAlways - Partition Info Model
// Partition Info Model - GPT Partition Data Structure
// ============================================================================
// Module: Qualcomm.Models
// Function: Stores partition LUN, name, size, sectors, etc.
// ============================================================================

// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// Eng Translation by iReverse - HadiKIT - Hadi Khoirudin, S.Kom.
// ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++


using System;
using System.ComponentModel;
using System.IO;

namespace LoveAlways.Qualcomm.Models
{
    /// <summary>
    /// Partition info model
    /// </summary>
    public class PartitionInfo : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected void OnPropertyChanged(string name)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(name));
        }

        /// <summary>
        /// LUN (Logical Unit Number)
        /// </summary>
        public int Lun { get; set; }

        /// <summary>
        /// Partition Name
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Start Sector
        /// </summary>
        public long StartSector { get; set; }

        /// <summary>
        /// Number of Sectors
        /// </summary>
        public long NumSectors { get; set; }

        /// <summary>
        /// Sector Size (usually 512 or 4096)
        /// </summary>
        public int SectorSize { get; set; }

        /// <summary>
        /// Partition Size (Bytes)
        /// </summary>
        public long Size
        {
            get { return NumSectors * SectorSize; }
        }

        /// <summary>
        /// Partition Type GUID
        /// </summary>
        public string TypeGuid { get; set; }

        /// <summary>
        /// Partition Unique GUID
        /// </summary>
        public string UniqueGuid { get; set; }

        /// <summary>
        /// Partition Attributes
        /// </summary>
        public ulong Attributes { get; set; }

        /// <summary>
        /// GPT Entry Index (used for patch operations)
        /// </summary>
        public int EntryIndex { get; set; } = -1;

        /// <summary>
        /// GPT Entries Start Sector (usually 2)
        /// </summary>
        public long GptEntriesStartSector { get; set; } = 2;

        private bool _isSelected;
        /// <summary>
        /// Whether selected (for UI)
        /// </summary>
        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged("IsSelected");
                }
            }
        }

        private string _customFilePath = "";
        /// <summary>
        /// Custom flash file path
        /// </summary>
        public string CustomFilePath
        {
            get { return _customFilePath; }
            set
            {
                if (_customFilePath != value)
                {
                    _customFilePath = value ?? "";
                    OnPropertyChanged("CustomFilePath");
                    OnPropertyChanged("CustomFileName");
                    OnPropertyChanged("HasCustomFile");
                }
            }
        }

        /// <summary>
        /// Custom file name
        /// </summary>
        public string CustomFileName
        {
            get { return string.IsNullOrEmpty(_customFilePath) ? "" : Path.GetFileName(_customFilePath); }
        }

        /// <summary>
        /// Whether there is a custom file
        /// </summary>
        public bool HasCustomFile
        {
            get { return !string.IsNullOrEmpty(_customFilePath); }
        }

        /// <summary>
        /// End Sector
        /// </summary>
        public long EndSector
        {
            get { return StartSector + NumSectors - 1; }
        }

        /// <summary>
        /// Formatted size string (KB if < 1MB, GB if >= 1GB)
        /// </summary>
        public string FormattedSize
        {
            get
            {
                var size = Size;
                if (size >= 1024L * 1024 * 1024)
                    return string.Format("{0:F2} GB", size / (1024.0 * 1024 * 1024));
                if (size >= 1024 * 1024)
                    return string.Format("{0:F2} MB", size / (1024.0 * 1024));
                if (size >= 1024)
                    return string.Format("{0:F0} KB", size / 1024.0);
                return string.Format("{0} B", size);
            }
        }

        /// <summary>
        /// Location info
        /// </summary>
        public string Location
        {
            get { return string.Format("0x{0:X} - 0x{1:X}", StartSector, EndSector); }
        }

        public PartitionInfo()
        {
            Name = "";
            SectorSize = 512;
            TypeGuid = "";
            UniqueGuid = "";
        }

        public override string ToString()
        {
            return string.Format("[LUN{0}] {1}: {2} ({3} - {4})", Lun, Name, FormattedSize, StartSector, EndSector);
        }
    }

    /// <summary>
    /// Flash partition info (used for flash operations)
    /// </summary>
    public class FlashPartitionInfo
    {
        public string Lun { get; set; }
        public string Name { get; set; }
        public string StartSector { get; set; }
        public long NumSectors { get; set; }
        public string Filename { get; set; }
        public long FileOffset { get; set; }
        public bool IsSparse { get; set; }

        public FlashPartitionInfo()
        {
            Lun = "0";
            Name = "";
            StartSector = "0";
            Filename = "";
        }

        public FlashPartitionInfo(string lun, string name, string start, long sectors, string filename = "", long offset = 0)
        {
            Lun = lun;
            Name = name;
            StartSector = start;
            NumSectors = sectors;
            Filename = filename ?? "";
            FileOffset = offset;
        }
    }
}
