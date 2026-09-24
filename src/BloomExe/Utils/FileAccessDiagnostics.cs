using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using SIL.IO;
using SIL.PlatformUtilities;

namespace Bloom.Utils
{
    /// <summary>
    /// Evidence about why Windows refused to let Bloom write a file even though the ACLs allow it and
    /// the ReadOnly attribute is off (BL-16915). Everything here is read-only and cheap, and the
    /// methods that touch the system never throw: a diagnostic that crashes hides the original error.
    /// </summary>
    public static class FileAccessDiagnostics
    {
        // FileAttributes values that .NET's enum does not name. Cloud sync providers (OneDrive,
        // Google Drive, Dropbox...) set these on placeholder files through the Cloud Files API.
        private const int kFileAttributeRecallOnOpen = 0x40000;
        private const int kFileAttributePinned = 0x80000;
        private const int kFileAttributeUnpinned = 0x100000;
        private const int kFileAttributeRecallOnDataAccess = 0x400000;

        private const string kCfaKeyPath =
            @"SOFTWARE\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access";
        private const string kCfaPolicyKeyPath =
            @"SOFTWARE\Policies\Microsoft\Windows Defender\Windows Defender Exploit Guard\Controlled Folder Access";
        private const string kCfaValueName = "EnableControlledFolderAccess";

        /// <summary>
        /// List every attribute that is set, including the cloud-file ones .NET prints only as numbers.
        /// </summary>
        public static string DescribeAttributes(FileAttributes attributes)
        {
            var names = new List<string>();
            var remaining = (int)attributes;
            void Take(int flag, string name)
            {
                if ((remaining & flag) == 0)
                    return;
                names.Add(name);
                remaining &= ~flag;
            }
            Take(kFileAttributeRecallOnOpen, "RecallOnOpen");
            Take(kFileAttributePinned, "Pinned");
            Take(kFileAttributeUnpinned, "Unpinned");
            Take(kFileAttributeRecallOnDataAccess, "RecallOnDataAccess");
            foreach (FileAttributes flag in Enum.GetValues(typeof(FileAttributes)))
                Take((int)flag, flag.ToString());
            if (remaining != 0)
                names.Add($"0x{remaining:X}");
            if (names.Count == 0)
                return "(none)";
            return string.Join(", ", names);
        }

        /// <summary>
        /// True if the attributes mark the item as managed by a cloud sync provider (a placeholder
        /// that may be downloaded on demand, or one the user pinned or unpinned).
        /// </summary>
        public static bool AttributesSuggestCloudFile(FileAttributes attributes)
        {
            const int cloudFlags =
                kFileAttributeRecallOnOpen
                | kFileAttributePinned
                | kFileAttributeUnpinned
                | kFileAttributeRecallOnDataAccess
                | (int)FileAttributes.Offline;
            return ((int)attributes & cloudFlags) != 0;
        }

        /// <summary>
        /// Return "provider (root folder)" for the innermost sync root that contains path, or null.
        /// Each root is a pair of provider name and root folder.
        /// </summary>
        public static string FindSyncRootContaining(
            string path,
            IEnumerable<KeyValuePair<string, string>> syncRoots
        )
        {
            var fullPath = Path.GetFullPath(path);
            string bestProvider = null;
            string bestRoot = null;
            foreach (var root in syncRoots)
            {
                if (string.IsNullOrWhiteSpace(root.Value))
                    continue;
                string rootPath;
                try
                {
                    rootPath = Path.GetFullPath(root.Value)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch (Exception)
                {
                    continue; // a malformed registry or environment value must not hide the rest
                }
                var contains =
                    fullPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase)
                    || fullPath.StartsWith(
                        rootPath + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase
                    );
                if (contains && (bestRoot == null || rootPath.Length > bestRoot.Length))
                {
                    bestProvider = root.Key;
                    bestRoot = rootPath;
                }
            }
            return bestRoot == null ? null : $"{bestProvider} ({bestRoot})";
        }

        /// <summary>
        /// The OneDrive folders named by environment variables, for sync clients that do not register
        /// with the Cloud Files API. Never throws.
        /// </summary>
        public static List<KeyValuePair<string, string>> GetSyncRootsFromEnvironment()
        {
            var result = new List<KeyValuePair<string, string>>();
            foreach (var variable in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
            {
                var value = Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(new KeyValuePair<string, string>(variable, value));
            }
            return result;
        }

        // CF_SYNC_ROOT_INFO_CLASS.CF_SYNC_ROOT_INFO_PROVIDER
        private const int kCfSyncRootInfoProvider = 2;

        // CF_SYNC_ROOT_PROVIDER_INFO: a status, then two WCHAR[CF_MAX_PROVIDER_NAME_LENGTH + 1] arrays.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CfSyncRootProviderInfo
        {
            public uint ProviderStatus;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string ProviderName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string ProviderVersion;
        }

        [DllImport("cldapi.dll", CharSet = CharSet.Unicode)]
        private static extern int CfGetSyncRootInfoByPath(
            string filePath,
            int infoClass,
            out CfSyncRootProviderInfo infoBuffer,
            uint infoBufferLength,
            out uint returnedLength
        );

        [DllImport("cldapi.dll")]
        private static extern uint CfGetPlaceholderStateFromAttributeTag(
            uint fileAttributes,
            uint reparseTag
        );

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Win32FindData
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint Reserved0; // the reparse tag, when FileAttributes has ReparsePoint
            public uint Reserved1;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string FileName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string AlternateFileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstFileW(string fileName, out Win32FindData findData);

        [DllImport("kernel32.dll")]
        private static extern bool FindClose(IntPtr findHandle);

        /// <summary>
        /// Ask the Windows Cloud Files API which sync provider (OneDrive, Google Drive, Dropbox,
        /// iCloud...) owns folderPath. Returns the provider's name, or null if none does. version gets
        /// its version and hresult the API's answer, so the report can say why nothing was found.
        /// Never throws.
        /// </summary>
        public static string GetCloudFilesSyncProvider(
            string folderPath,
            out string version,
            out string hresult
        )
        {
            version = null;
            hresult = null;
            if (!Platform.IsWindows)
                return null;
            try
            {
                var result = CfGetSyncRootInfoByPath(
                    folderPath,
                    kCfSyncRootInfoProvider,
                    out var info,
                    (uint)Marshal.SizeOf<CfSyncRootProviderInfo>(),
                    out _
                );
                hresult = $"0x{result:X8}";
                if (result != 0)
                    return null;
                version = info.ProviderVersion;
                return info.ProviderName;
            }
            catch (Exception e)
            {
                hresult = e.GetType().Name; // e.g. cldapi.dll missing before Windows 10 1709
                return null;
            }
        }

        /// <summary>
        /// Describe a CF_PLACEHOLDER_STATE value in words.
        /// </summary>
        public static string DescribePlaceholderState(uint state)
        {
            if (state == 0xFFFFFFFF)
                return "invalid";
            if (state == 0)
                return "not a placeholder";
            var names = new List<string>();
            void Add(uint flag, string name)
            {
                if ((state & flag) != 0)
                    names.Add(name);
            }
            Add(0x1, "placeholder");
            Add(0x2, "sync root");
            Add(0x4, "essential properties present");
            Add(0x8, "in sync");
            Add(0x10, "partial");
            Add(0x20, "partially on disk");
            var unknown = state & ~0x3Fu;
            if (unknown != 0)
                names.Add($"0x{unknown:X}");
            return string.Join(", ", names);
        }

        /// <summary>
        /// The Cloud Files placeholder state of path, found from its directory entry so that no handle
        /// to the file is needed (opening it may be what Windows is refusing). Null if it can't be read.
        /// Never throws.
        /// </summary>
        public static uint? GetPlaceholderState(string path)
        {
            if (!Platform.IsWindows)
                return null;
            try
            {
                var handle = FindFirstFileW(path, out var data);
                if (handle == new IntPtr(-1))
                    return null;
                FindClose(handle);
                var reparseTag =
                    (data.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0
                        ? data.Reserved0
                        : 0;
                return CfGetPlaceholderStateFromAttributeTag(data.FileAttributes, reparseTag);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The EnableControlledFolderAccess setting of Windows Defender: the group policy value if one
        /// is set, else the local one; null if neither can be read. Never throws.
        /// </summary>
        public static int? ReadControlledFolderAccessSetting()
        {
            if (!Platform.IsWindows)
                return null;
            return ReadHklmInt(kCfaPolicyKeyPath, kCfaValueName)
                ?? ReadHklmInt(kCfaKeyPath, kCfaValueName);
        }

        private static int? ReadHklmInt(string keyPath, string valueName)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    if (key?.GetValue(valueName) is int value)
                        return value;
                }
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>
        /// Describe an EnableControlledFolderAccess value in words.
        /// </summary>
        public static string DescribeControlledFolderAccess(int? setting)
        {
            switch (setting)
            {
                case null:
                    return "could not be read";
                case 0:
                    return "off";
                case 1:
                    return "on (blocks untrusted apps from changing files in protected folders)";
                case 2:
                    return "audit only";
                case 3:
                    return "blocks disk modification only";
                case 4:
                    return "audits disk modification only";
                default:
                    return $"unknown value {setting}";
            }
        }

        /// <summary>
        /// Describe the real-time protection bits of a SecurityCenter2 AntivirusProduct productState.
        /// </summary>
        public static string DescribeAntivirusProductState(int productState)
        {
            var protection = (productState >> 8) & 0xFF;
            var definitions = productState & 0xFF;
            string protectionText;
            switch (protection)
            {
                case 0x10:
                case 0x11:
                    protectionText = "real-time protection on";
                    break;
                case 0x00:
                case 0x01:
                    protectionText = "real-time protection off or snoozed";
                    break;
                default:
                    protectionText = $"real-time protection state 0x{protection:X2}";
                    break;
            }
            return protectionText
                + (definitions == 0 ? ", definitions up to date" : ", definitions out of date");
        }

        /// <summary>
        /// Try to open the file for reading only, sharing everything. If even this fails with access
        /// denied, the file is probably delete-pending or held by a filter driver; if it succeeds, only
        /// writing is being refused. Never throws.
        /// </summary>
        public static string TryOpenForRead(string path)
        {
            try
            {
                // One attempt, no retries: we want to know what happens right now.
                // robustfile-hook: allow FileStream
                using (
                    new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete
                    )
                ) { }
                return "succeeded";
            }
            catch (Exception e)
            {
                return $"failed: {e.GetType().Name} (HResult 0x{e.HResult:X8}) {e.Message}";
            }
        }

        /// <summary>
        /// A sentence naming the likely cause of a failure to write path, or null when the evidence
        /// points at nothing in particular. We only name a suspect we have evidence for.
        /// </summary>
        public static string GetLikelyCause(
            FileAttributes? fileAttributes,
            FileAttributes? folderAttributes,
            string syncRoot,
            int? controlledFolderAccess,
            uint? placeholderState = null,
            bool avastActiveAndUnderDocuments = false
        )
        {
            if (controlledFolderAccess == 1)
                return "Windows Security's Controlled Folder Access is turned on; it may be blocking Bloom from changing files in this folder.";
            // Every BL-3227-style report we have (BL-16507, BL-16915, BL-16919) had Avast's real-time
            // protection on and the book under Documents, which Avast's Ransomware Shield protects.
            if (avastActiveAndUnderDocuments)
                return "Avast's Ransomware Shield may be blocking Bloom. You can allow Bloom in Avast under Protection > Ransomware Shield.";
            if (
                syncRoot != null
                || (placeholderState.HasValue && IsPlaceholder(placeholderState.Value))
                || (fileAttributes.HasValue && AttributesSuggestCloudFile(fileAttributes.Value))
                || (folderAttributes.HasValue && AttributesSuggestCloudFile(folderAttributes.Value))
            )
            {
                var provider = syncRoot == null ? "a cloud sync program" : syncRoot;
                return $"This file is in a folder managed by {provider}, which may be holding it while it syncs.";
            }
            return null;
        }

        /// <summary>
        /// The display names of the antivirus products Windows Security Center reports with real-time
        /// protection on. Empty if none, or if they can't be read. Never throws.
        /// </summary>
        public static List<string> GetActiveAntivirusNames()
        {
            var result = new List<string>();
            if (!Platform.IsWindows)
                return result;
            try
            {
                using (
                    var searcher = new System.Management.ManagementObjectSearcher(
                        @"root\SecurityCenter2",
                        "SELECT displayName, productState FROM AntivirusProduct"
                    )
                )
                {
                    foreach (var instance in searcher.Get())
                    {
                        if (
                            instance["productState"] is uint state
                            && IsRealTimeProtectionOn((int)state)
                            && instance["displayName"] is string name
                        )
                            result.Add(name);
                    }
                }
            }
            catch (Exception) { }
            return result;
        }

        /// <summary>
        /// True if a SecurityCenter2 productState says real-time protection is on.
        /// </summary>
        public static bool IsRealTimeProtectionOn(int productState)
        {
            var protection = (productState >> 8) & 0xFF;
            return protection == 0x10 || protection == 0x11;
        }

        private static bool IsPlaceholder(uint placeholderState) =>
            placeholderState != 0xFFFFFFFF && (placeholderState & 0x1) != 0;

        /// <summary>
        /// Gather the evidence for path from the running system and return the likely cause (or null)
        /// and a multi-line report of everything found. Never throws.
        /// </summary>
        public static string Collect(string path, out string likelyCause)
        {
            var bldr = new StringBuilder();
            likelyCause = null;
            try
            {
                var exists = FileIsPresent(path, out var existence);
                bldr.AppendLine($"file exists: {existence}");
                FileAttributes? fileAttributes = null;
                if (exists)
                {
                    fileAttributes = TryGetAttributes(path);
                    bldr.AppendLine(
                        $"file attributes: {(fileAttributes.HasValue ? DescribeAttributes(fileAttributes.Value) : "could not be read")}"
                    );
                    bldr.AppendLine($"opening the file read-only {TryOpenForRead(path)}");
                }
                uint? placeholderState = exists ? GetPlaceholderState(path) : null;
                if (exists)
                    bldr.AppendLine(
                        $"cloud placeholder state: {(placeholderState.HasValue ? DescribePlaceholderState(placeholderState.Value) : "could not be read")}"
                    );
                var folder = Path.GetDirectoryName(path);
                var folderAttributes = TryGetAttributes(folder);
                bldr.AppendLine(
                    $"folder attributes: {(folderAttributes.HasValue ? DescribeAttributes(folderAttributes.Value) : "could not be read")}"
                );
                var syncRoot = GetCloudFilesSyncProvider(
                    folder,
                    out var providerVersion,
                    out var cloudFilesResult
                );
                bldr.AppendLine(
                    $"Cloud Files sync provider: {(syncRoot == null ? "none" : $"{syncRoot} version {providerVersion}")} (result {cloudFilesResult})"
                );
                var environmentSyncRoot = FindSyncRootContaining(
                    path,
                    GetSyncRootsFromEnvironment()
                );
                bldr.AppendLine(
                    $"OneDrive folder from environment: {environmentSyncRoot ?? "none containing this file"}"
                );
                syncRoot = syncRoot ?? environmentSyncRoot;
                var cfa = ReadControlledFolderAccessSetting();
                bldr.AppendLine($"Controlled Folder Access: {DescribeControlledFolderAccess(cfa)}");
                var activeAntivirus = GetActiveAntivirusNames();
                bldr.AppendLine(
                    $"antivirus with real-time protection on: {(activeAntivirus.Count == 0 ? "none found" : string.Join(", ", activeAntivirus))}"
                );
                var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var underDocuments =
                    !string.IsNullOrEmpty(documents)
                    && FindSyncRootContaining(
                        path,
                        new[] { new KeyValuePair<string, string>("Documents", documents) }
                    ) != null;
                bldr.AppendLine($"under Documents: {underDocuments}");
                var avastActive = activeAntivirus.Any(name =>
                    name.IndexOf("Avast", StringComparison.OrdinalIgnoreCase) >= 0
                );
                likelyCause = GetLikelyCause(
                    fileAttributes,
                    folderAttributes,
                    syncRoot,
                    cfa,
                    placeholderState,
                    avastActive && underDocuments
                );
            }
            catch (Exception e)
            {
                bldr.AppendLine($"Caught exception {e} while collecting file access evidence");
            }
            return bldr.ToString();
        }

        /// <summary>
        /// Whether path names a file, including one Bloom is not allowed to see. RobustFile.Exists answers
        /// false for both "not there" and "not allowed", and only the second is what we are trying to
        /// diagnose, so tell them apart by why reading the attributes fails. description says which
        /// case it was. Never throws.
        /// </summary>
        public static bool FileIsPresent(string path, out string description)
        {
            if (!Platform.IsWindows)
            {
                var exists = RobustFile.Exists(path);
                description = exists.ToString();
                return exists;
            }
            try
            {
                // One direct call: RobustFile would retry "not found" for seconds.
                var attributes = GetFileAttributesW(path);
                if (attributes != kInvalidFileAttributes)
                {
                    var isFile = (attributes & (uint)FileAttributes.Directory) == 0;
                    description = isFile ? "True" : "False (it is a folder)";
                    return isFile;
                }
                var error = Marshal.GetLastWin32Error();
                if (error == kErrorFileNotFound || error == kErrorPathNotFound)
                {
                    description = "False";
                    return false;
                }
                description = $"probably (its attributes can't be read: Windows error {error})";
                return true;
            }
            catch (Exception e)
            {
                description = $"unknown ({e.GetType().Name})";
                return RobustFile.Exists(path);
            }
        }

        private const uint kInvalidFileAttributes = 0xFFFFFFFF;
        private const int kErrorFileNotFound = 2;
        private const int kErrorPathNotFound = 3;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFileAttributesW(string fileName);

        /// <summary>
        /// As above, without the description.
        /// </summary>
        public static bool FileIsPresent(string path) => FileIsPresent(path, out _);

        private static FileAttributes? TryGetAttributes(string path)
        {
            try
            {
                return RobustFile.GetAttributes(path);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
