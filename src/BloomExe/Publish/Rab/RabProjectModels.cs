using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using SIL.IO;

namespace Bloom.Publish.Rab
{
    // Passive DTOs and path helpers live here so RabProjectService can focus on orchestration.
    /// <summary>
    /// Summarizes the current App Builder workspace state that the browser UI needs to render actions and status.
    /// </summary>
    public class RabProjectStatus
    {
        public bool RabInstalled { get; set; }
        public bool ProjectExists { get; set; }
        public bool ApkExists { get; set; }
        public bool BuildNeeded { get; set; }
        public string UserDownloadsDirectory { get; set; }
        public string AppDefPath { get; set; }
        public string AppName { get; set; }
        public string ApkPath { get; set; }
        public long ApkSizeBytes { get; set; }
        public string RabRoot { get; set; }
        public string[] TrackedBookTitles { get; set; } = Array.Empty<string>();
        public RabTrackedBookInfo[] TrackedBooks { get; set; } = Array.Empty<RabTrackedBookInfo>();
        public RabPrepareStepStatus[] PrepareSteps { get; set; } =
            Array.Empty<RabPrepareStepStatus>();
    }

    /// <summary>
    /// Reports completion for each prepare checklist step shown in the Apps screen.
    /// </summary>
    public class RabPrepareStepStatus
    {
        public string Id { get; set; }
        public bool Complete { get; set; }
        public string IncompleteTooltip { get; set; }
        public string CompleteTooltip { get; set; }
    }

    /// <summary>
    /// Captures the size of one book's contribution to the generated app.
    /// </summary>
    public class RabBookSizeEstimate
    {
        public string BookId { get; set; }
        public string FolderPath { get; set; }
        public string Title { get; set; }
        public long SizeBytes { get; set; }
        public bool IsActual { get; set; }
    }

    /// <summary>
    /// Captures the book-by-book and overall app size estimates shown in the Apps screen.
    /// </summary>
    public class RabAppSizeEstimates
    {
        public RabBookSizeEstimate[] Books { get; set; } = Array.Empty<RabBookSizeEstimate>();
        public long EstimatedAppOverheadBytes { get; set; }
        public long MaxAppSizeBytes { get; set; }
    }

    /// <summary>
    /// Identifies a collection book that should be included in the Reading App Builder project.
    /// </summary>
    public class RabTrackedBookInfo
    {
        public string BookId { get; set; }
        public string FolderPath { get; set; }
        public string Title { get; set; }
    }

    internal class RabPrepareState
    {
        public string AppDefPath { get; set; }
        public string ProjectName { get; set; }
        public string KeystorePath { get; set; }
        public string KeystorePassword { get; set; }
        public string KeyAlias { get; set; }
        public string AliasPassword { get; set; }
        public List<RabBookPublishInfo> Books { get; set; } = new List<RabBookPublishInfo>();
        public string LastBuiltInputSignature { get; set; }
        public string LastBuiltApkPath { get; set; }
        public string LastBuiltPackageName { get; set; }
    }

    internal class RabSharedSigningState
    {
        public string KeystorePath { get; set; }
        public string KeystorePassword { get; set; }
        public string KeyAlias { get; set; }
        public string AliasPassword { get; set; }
    }

    internal class RabProjectSupportFiles
    {
        public string AboutTextPath { get; set; }
        public string[] LauncherIconPaths { get; set; } = Array.Empty<string>();
    }

    internal class RabAppFontDefinition
    {
        public string FamilyName { get; set; }
        public string FontName { get; set; }
        public string DisplayName { get; set; }
        public string FileName { get; set; }
        public string Format { get; set; }
        public string Weight { get; set; }
        public string Style { get; set; }
    }

    /// <summary>
    /// Describes one icon choice that Bloom can offer for a Reading App Builder app.
    /// </summary>
    public class RabIconChoice
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public string IconPath { get; set; }
    }

    /// <summary>
    /// Reading App Builder still has trouble with some non-ASCII paths on Windows, including
    /// paths rooted under a collection folder whose name or parent folders contain characters
    /// outside basic ASCII. This helper centralizes Bloom's workaround for that limitation by
    /// resolving an ASCII-safe alias for a collection path when possible and then deriving the
    /// RAB work/output folders from that safe path.
    /// </summary>
    internal static class RabSafePathPolicy
    {
#if !__MonoCS__
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetShortPathName(
            string longPath,
            StringBuilder shortPath,
            int shortPathBufferLength
        );
#endif

        internal static bool IsAscii(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && path.All(ch => ch <= 0x7F);
        }

        internal static string GetCollectionWorkRoot(
            string collectionRoot,
            Func<string, string> shortPathResolver = null
        )
        {
            var safeCollectionRoot = ResolveSafePath(collectionRoot, shortPathResolver);
            if (string.IsNullOrWhiteSpace(safeCollectionRoot))
                throw new ApplicationException(
                    $"The path '{collectionRoot}' contains non-standard characters. Rename the collection to only include standard characters."
                );

            return Path.Combine(safeCollectionRoot, "Bloom App Data", "RabWork");
        }

        internal static string ResolveSafePath(
            string path,
            Func<string, string> shortPathResolver = null
        )
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            var fullPath = Path.GetFullPath(path);
            if (IsAscii(fullPath))
                return fullPath;

            shortPathResolver ??= TryGetShortPath;
            var shortPath = shortPathResolver(fullPath);
            return !string.IsNullOrWhiteSpace(shortPath) && IsAscii(shortPath) ? shortPath : null;
        }

        private static string TryGetShortPath(string path)
        {
            if (!OperatingSystem.IsWindows())
                return null;

            var fullPath = Path.GetFullPath(path);
            var existingPath = fullPath;
            var suffixSegments = new Stack<string>();

            while (!string.IsNullOrWhiteSpace(existingPath))
            {
                if (Directory.Exists(existingPath) || RobustFile.Exists(existingPath))
                    break;

                var parent = Path.GetDirectoryName(existingPath);
                if (
                    string.IsNullOrWhiteSpace(parent)
                    || string.Equals(parent, existingPath, StringComparison.Ordinal)
                )
                    return null;

                suffixSegments.Push(Path.GetFileName(existingPath));
                existingPath = parent;
            }

#if !__MonoCS__
            var buffer = new StringBuilder(512);
            var resultLength = GetShortPathName(existingPath, buffer, buffer.Capacity);
            if (resultLength > buffer.Capacity)
            {
                buffer = new StringBuilder(resultLength);
                resultLength = GetShortPathName(existingPath, buffer, buffer.Capacity);
            }

            if (resultLength <= 0)
                return null;

            var shortPath = buffer.ToString();
            while (suffixSegments.Count > 0)
                shortPath = Path.Combine(shortPath, suffixSegments.Pop());

            return shortPath;
#else
            return null;
#endif
        }
    }

    internal class RabWorkspacePaths
    {
        internal static string GetSafeWorkRoot(string collectionRoot)
        {
            return RabSafePathPolicy.GetCollectionWorkRoot(collectionRoot);
        }

        public RabWorkspacePaths(string collectionRoot, string bloomOwnedRabRoot = null)
        {
            CollectionRoot = collectionRoot;
            SafeCollectionRoot = RabSafePathPolicy.ResolveSafePath(collectionRoot);
            SafeWorkRoot = GetSafeWorkRoot(collectionRoot);
            RabRoot = Path.Combine(collectionRoot, "Bloom App Data");
            SafeRabRoot = Path.Combine(SafeCollectionRoot, "Bloom App Data");
            BloomPubRoot = Path.Combine(RabRoot, "bloompubs");
            BuildRoot = Path.Combine(RabRoot, "build");
            ApkRoot = Path.Combine(RabRoot, "apk");
            SafeApkRoot = Path.Combine(SafeRabRoot, "apk");
            BloomOwnedRabRoot = string.IsNullOrWhiteSpace(bloomOwnedRabRoot)
                ? RabRoot
                : bloomOwnedRabRoot;
            KeystoreRoot = Path.Combine(BloomOwnedRabRoot, "keystore");
            SharedKeystorePath = Path.Combine(KeystoreRoot, "bloom-app-builder.keystore");
            SharedSigningStatePath = Path.Combine(KeystoreRoot, "signing.json");
            ProjectAssetsRoot = Path.Combine(RabRoot, "project-assets");
            LauncherIconRoot = Path.Combine(ProjectAssetsRoot, "launcher-icons");
            AboutTextPath = Path.Combine(ProjectAssetsRoot, "about.txt");
            PrepareStatePath = Path.Combine(RabRoot, "prepare.json");
        }

        public string CollectionRoot { get; }
        public string SafeCollectionRoot { get; }
        public string SafeWorkRoot { get; }
        public string RabRoot { get; }
        public string SafeRabRoot { get; }
        public string BloomPubRoot { get; }
        public string BuildRoot { get; }
        public string ApkRoot { get; }
        public string SafeApkRoot { get; }
        public string BloomOwnedRabRoot { get; }
        public string KeystoreRoot { get; }
        public string SharedKeystorePath { get; }
        public string SharedSigningStatePath { get; }
        public string ProjectAssetsRoot { get; }
        public string LauncherIconRoot { get; }
        public string AboutTextPath { get; }
        public string PrepareStatePath { get; }
    }
}
