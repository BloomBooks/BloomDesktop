using System;
using System.Collections.Generic;
using System.IO;

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

    internal class RabWorkspacePaths
    {
        internal static string GetAsciiWorkRoot()
        {
            return Path.Combine(Path.GetTempPath(), "Bloom");
        }

        public RabWorkspacePaths(string collectionRoot, string bloomOwnedRabRoot = null)
        {
            CollectionRoot = collectionRoot;
            RabRoot = Path.Combine(collectionRoot, "Bloom App Data");
            BloomPubRoot = Path.Combine(RabRoot, "bloompubs");
            BuildRoot = Path.Combine(RabRoot, "build");
            ApkRoot = Path.Combine(RabRoot, "apk");
            // Work around RAB mishandling non-ASCII apk.output paths by staging the APK in a
            // Bloom-owned ASCII-only temp folder, then moving it back into the collection after build.
            // See https://github.com/sillsdev/app-builders/issues/2186.
            AsciiApkOutputRoot = Path.Combine(
                GetAsciiWorkRoot(),
                "RabApkOutput",
                Guid.NewGuid().ToString("N")
            );
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
        public string RabRoot { get; }
        public string BloomPubRoot { get; }
        public string BuildRoot { get; }
        public string ApkRoot { get; }
        public string AsciiApkOutputRoot { get; }
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
