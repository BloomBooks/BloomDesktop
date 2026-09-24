using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private const string kSyncRootManagerKeyPath =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager";

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
        /// The sync roots registered with Windows (by OneDrive, Google Drive, Dropbox, iCloud...),
        /// plus the OneDrive folders named by environment variables. Never throws.
        /// </summary>
        public static List<KeyValuePair<string, string>> GetSyncRoots()
        {
            var result = new List<KeyValuePair<string, string>>();
            if (!Platform.IsWindows)
                return result;
            try
            {
                using (var manager = Registry.LocalMachine.OpenSubKey(kSyncRootManagerKeyPath))
                {
                    if (manager != null)
                    {
                        foreach (var providerKeyName in manager.GetSubKeyNames())
                        {
                            // Key names look like "OneDrive!S-1-5-21-...!Personal".
                            var provider = providerKeyName.Split('!')[0];
                            using (
                                var userRoots = manager.OpenSubKey(
                                    providerKeyName + @"\UserSyncRoots"
                                )
                            )
                            {
                                if (userRoots == null)
                                    continue;
                                foreach (var valueName in userRoots.GetValueNames())
                                {
                                    if (userRoots.GetValue(valueName) is string rootPath)
                                        result.Add(
                                            new KeyValuePair<string, string>(provider, rootPath)
                                        );
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception) { }
            foreach (var variable in new[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
            {
                var value = Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(new KeyValuePair<string, string>(variable, value));
            }
            return result;
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
            int? controlledFolderAccess
        )
        {
            if (controlledFolderAccess == 1)
                return "Windows Security's Controlled Folder Access is turned on; it may be blocking Bloom from changing files in this folder.";
            if (
                syncRoot != null
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
        /// Gather the evidence for path from the running system and return the likely cause (or null)
        /// and a multi-line report of everything found. Never throws.
        /// </summary>
        public static string Collect(string path, out string likelyCause)
        {
            var bldr = new StringBuilder();
            likelyCause = null;
            try
            {
                var exists = RobustFile.Exists(path);
                bldr.AppendLine($"file exists: {exists}");
                FileAttributes? fileAttributes = null;
                if (exists)
                {
                    fileAttributes = TryGetAttributes(path);
                    bldr.AppendLine(
                        $"file attributes: {(fileAttributes.HasValue ? DescribeAttributes(fileAttributes.Value) : "could not be read")}"
                    );
                    bldr.AppendLine($"opening the file read-only {TryOpenForRead(path)}");
                }
                var folder = Path.GetDirectoryName(path);
                var folderAttributes = TryGetAttributes(folder);
                bldr.AppendLine(
                    $"folder attributes: {(folderAttributes.HasValue ? DescribeAttributes(folderAttributes.Value) : "could not be read")}"
                );
                var syncRoot = FindSyncRootContaining(path, GetSyncRoots());
                bldr.AppendLine($"sync provider: {syncRoot ?? "none found"}");
                var cfa = ReadControlledFolderAccessSetting();
                bldr.AppendLine($"Controlled Folder Access: {DescribeControlledFolderAccess(cfa)}");
                likelyCause = GetLikelyCause(fileAttributes, folderAttributes, syncRoot, cfa);
            }
            catch (Exception e)
            {
                bldr.AppendLine($"Caught exception {e} while collecting file access evidence");
            }
            return bldr.ToString();
        }

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
