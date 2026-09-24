using System.Collections.Generic;
using System.IO;
using Bloom;
using Bloom.Book;
using Bloom.Utils;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.Utils
{
    [TestFixture]
    public class FileAccessDiagnosticsTests
    {
        [Test]
        public void DescribeAttributes_CloudPlaceholderFlags_NamesThemInsteadOfNumbers()
        {
            // RecallOnDataAccess (0x400000) and Unpinned (0x100000) have no names in .NET's enum.
            var attributes =
                FileAttributes.Archive | (FileAttributes)0x400000 | (FileAttributes)0x100000;
            Assert.That(
                attributes.ToString(),
                Does.Not.Contain("RecallOnDataAccess"),
                "sanity check"
            );

            var result = FileAccessDiagnostics.DescribeAttributes(attributes);

            Assert.That(result, Is.EqualTo("Unpinned, RecallOnDataAccess, Archive"));
        }

        [Test]
        public void DescribeAttributes_None_SaysNone()
        {
            Assert.That(FileAccessDiagnostics.DescribeAttributes(0), Is.EqualTo("(none)"));
        }

        [Test]
        public void AttributesSuggestCloudFile_ArchiveOnly_False_RecallOnOpen_True()
        {
            Assert.That(
                FileAccessDiagnostics.AttributesSuggestCloudFile(FileAttributes.Archive),
                Is.False
            );
            Assert.That(
                FileAccessDiagnostics.AttributesSuggestCloudFile((FileAttributes)0x40000),
                Is.True
            );
            Assert.That(
                FileAccessDiagnostics.AttributesSuggestCloudFile(FileAttributes.Offline),
                Is.True
            );
        }

        [Test]
        public void FindSyncRootContaining_PathUnderRoot_ReturnsProviderAndRoot()
        {
            var roots = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("GoogleDrive", @"G:\My Drive"),
                new KeyValuePair<string, string>("OneDrive", @"C:\Users\HP\OneDrive\"),
            };

            var result = FileAccessDiagnostics.FindSyncRootContaining(
                @"C:\Users\HP\onedrive\Documents\Bloom\book\license.png",
                roots
            );

            Assert.That(result, Is.EqualTo(@"OneDrive (C:\Users\HP\OneDrive)"));
        }

        [Test]
        public void FindSyncRootContaining_SiblingWithSharedPrefix_ReturnsNull()
        {
            var roots = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("OneDrive", @"C:\Users\HP\OneDrive"),
            };

            var result = FileAccessDiagnostics.FindSyncRootContaining(
                @"C:\Users\HP\OneDriveBackup\license.png",
                roots
            );

            Assert.That(result, Is.Null);
        }

        [Test]
        public void DescribeAntivirusProductState_ValuesFromBL16915Report()
        {
            // Avast was active; Defender was passive because Avast had taken over.
            Assert.That(
                FileAccessDiagnostics.DescribeAntivirusProductState(266240),
                Is.EqualTo("real-time protection on, definitions up to date")
            );
            Assert.That(
                FileAccessDiagnostics.DescribeAntivirusProductState(393472),
                Is.EqualTo("real-time protection off or snoozed, definitions up to date")
            );
        }

        [Test]
        public void GetLikelyCause_NoEvidence_ReturnsNull()
        {
            Assert.That(
                FileAccessDiagnostics.GetLikelyCause(
                    FileAttributes.Archive,
                    FileAttributes.Directory,
                    null,
                    0
                ),
                Is.Null
            );
            Assert.That(FileAccessDiagnostics.GetLikelyCause(null, null, null, null), Is.Null);
            // Audit mode does not block anything.
            Assert.That(FileAccessDiagnostics.GetLikelyCause(null, null, null, 2), Is.Null);
        }

        [Test]
        public void GetLikelyCause_ControlledFolderAccessOn_NamesIt()
        {
            Assert.That(
                FileAccessDiagnostics.GetLikelyCause(FileAttributes.Archive, null, null, 1),
                Does.Contain("Controlled Folder Access")
            );
        }

        [Test]
        public void GetLikelyCause_SyncRoot_NamesProvider()
        {
            Assert.That(
                FileAccessDiagnostics.GetLikelyCause(
                    null,
                    null,
                    @"OneDrive (C:\Users\HP\OneDrive)",
                    0
                ),
                Does.Contain("OneDrive")
            );
            Assert.That(
                FileAccessDiagnostics.GetLikelyCause((FileAttributes)0x400000, null, null, 0),
                Does.Contain("cloud sync program")
            );
        }

        [Test]
        public void Collect_ExistingFile_ReportsExistenceAttributesAndReadOpen()
        {
            using (var folder = new TemporaryFolder("FileAccessDiagnosticsTests"))
            {
                var path = Path.Combine(folder.Path, "x.png");
                File.WriteAllText(path, "x");

                var result = FileAccessDiagnostics.Collect(path, out _);

                Assert.That(result, Does.Contain("file exists: True"));
                Assert.That(result, Does.Contain("file attributes: Archive"));
                Assert.That(result, Does.Contain("opening the file read-only succeeded"));
                Assert.That(result, Does.Contain("Controlled Folder Access: "));
                Assert.That(result, Does.Contain("sync provider: "));
            }
        }

        /// <summary>
        /// A directory where the QR code png should be makes the write fail with access denied, as on
        /// the BL-16915 reporter's machine. That must be reported, not thrown, so the book can be selected.
        /// </summary>
        [Test]
        public void UpdateQrCode_CannotWriteQrCodeFile_ReportsNonFatalProblem()
        {
            using (var folder = new TemporaryFolder("FileAccessDiagnosticsTests_Qr"))
            {
                var blocker = Path.Combine(folder.Path, "lang-qr-code.png");
                Directory.CreateDirectory(blocker);
                var dom = new HtmlDom(
                    @"<html><head></head><body><div class='bloom-page'>
						<div class='bloom-branding-wrapper'><a><img class='branding' src='made-with-bloom-badge.svg'/></a></div>
					</div></body></html>"
                );

                using (new NonFatalProblem.ExpectedByUnitTest())
                {
                    BookStorage.UpdateQrCode(
                        dom,
                        true,
                        "xyz",
                        "More {0} books",
                        "Xyz",
                        folder.Path
                    );
                }

                Assert.That(
                    dom.SafeSelectNodes("//img[contains(@class,'bloom-qrcode')]").Length,
                    Is.EqualTo(1),
                    "the page should still reference the QR code image"
                );
            }
        }
    }
}
