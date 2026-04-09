using System;
using System.Collections.Generic;
using System.IO;
namespace Bloom.Publish.Rab
{
    // Passive DTOs and path helpers live here so RabProjectService can focus on orchestration.
    public class RabProjectStatus
    {
        public bool RabInstalled { get; set; }
        public bool ProjectExists { get; set; }
        public bool ApkExists { get; set; }
        public bool BuildNeeded { get; set; }
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

    public class RabPrepareStepStatus
    {
        public string Id { get; set; }
        public bool Complete { get; set; }
    }

    public class RabBookSizeEstimate
    {
        public string BookId { get; set; }
        public string FolderPath { get; set; }
        public string Title { get; set; }
        public long SizeBytes { get; set; }
        public bool IsActual { get; set; }
    }

    public class RabAppSizeEstimates
    {
        public RabBookSizeEstimate[] Books { get; set; } = Array.Empty<RabBookSizeEstimate>();
        public long EstimatedAppOverheadBytes { get; set; }
        public long MaxAppSizeBytes { get; set; }
    }

    public class RabTrackedBookInfo
    {
        public string BookId { get; set; }
        public string FolderPath { get; set; }
        public string Title { get; set; }
    }

    internal class RabSetupState
    {
        public string AppDefPath { get; set; }
        public string ProjectName { get; set; }
        public string KeystorePath { get; set; }
        public string KeystorePassword { get; set; }
        public string KeyAlias { get; set; }
        public string AliasPassword { get; set; }
        public List<RabBookPublishInfo> Books { get; set; } = new List<RabBookPublishInfo>();
        public string LastBuiltInputSignature { get; set; }
    }

    internal class RabProjectSupportFiles
    {
        public string AboutTextPath { get; set; }
        public string[] LauncherIconPaths { get; set; } = Array.Empty<string>();
    }

    public class RabIconChoice
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public string IconPath { get; set; }
    }

    internal class RabWorkspacePaths
    {
        public RabWorkspacePaths(string collectionRoot)
        {
            CollectionRoot = collectionRoot;
            RabRoot = Path.Combine(collectionRoot, "app configuration");
            BloomPubRoot = Path.Combine(RabRoot, "bloompubs");
            BuildRoot = Path.Combine(RabRoot, "build");
            ApkRoot = Path.Combine(RabRoot, "apk");
            KeystoreRoot = Path.Combine(RabRoot, "keystore");
            ProjectAssetsRoot = Path.Combine(RabRoot, "project-assets");
            LauncherIconRoot = Path.Combine(ProjectAssetsRoot, "launcher-icons");
            AboutTextPath = Path.Combine(ProjectAssetsRoot, "about.txt");
            SetupStatePath = Path.Combine(RabRoot, "setup.json");
        }

        public string CollectionRoot { get; }
        public string RabRoot { get; }
        public string BloomPubRoot { get; }
        public string BuildRoot { get; }
        public string ApkRoot { get; }
        public string KeystoreRoot { get; }
        public string ProjectAssetsRoot { get; }
        public string LauncherIconRoot { get; }
        public string AboutTextPath { get; }
        public string SetupStatePath { get; }
    }
}
