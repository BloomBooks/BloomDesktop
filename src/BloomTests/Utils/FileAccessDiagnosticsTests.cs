using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
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
        public void FindSyncRootContaining_NestedRoots_ReturnsInnermost()
        {
            var roots = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("OneDrive", @"C:\Users\HP\OneDrive"),
                new KeyValuePair<string, string>("Other", @"C:\Users\HP\OneDrive\Shared"),
            };

            var result = FileAccessDiagnostics.FindSyncRootContaining(
                @"C:\Users\HP\OneDrive\Shared\book\license.png",
                roots
            );

            Assert.That(result, Is.EqualTo(@"Other (C:\Users\HP\OneDrive\Shared)"));
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
        /// the BL-16915 reporter's machine, and leaves no QR code file. That must be reported, not
        /// thrown, so the book can be selected, and the badge must drop its QR code rather than show
        /// a broken image.
        /// </summary>
        [Test]
        public void UpdateQrCode_CannotWriteAndNoQrCodeFile_ReportsNonFatalProblemAndShowsNoQrCode()
        {
            using (var folder = new TemporaryFolder("FileAccessDiagnosticsTests_Qr"))
            {
                var blocker = Path.Combine(folder.Path, "lang-qr-code.png");
                Directory.CreateDirectory(blocker);
                var dom = new HtmlDom(
                    @"<html><head></head><body><div class='bloom-page'>
						<div class='bloom-branding-wrapper'><a href='https://bloomlibrary.org/language:old'>
							<img class='branding' src='made-with-bloom-badge-text.svg'/>
							<img class='bloom-qrcode' src='lang-qr-code.png'/>
						</a></div>
					</div></body></html>"
                );
                Assert.That(
                    dom.SafeSelectNodes("//img[contains(@class,'bloom-qrcode')]").Length,
                    Is.EqualTo(1),
                    "sanity check: the badge starts with a QR code image"
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
                    Is.EqualTo(0),
                    "without any QR code file the badge should show no QR code"
                );
            }
        }

        /// <summary>
        /// When an existing QR code file can't be overwritten, the badge keeps using it: the QR code
        /// only changes with the collection's primary language, so the old file is almost always right.
        /// </summary>
        [Test]
        public void UpdateQrCode_CannotOverwriteExistingQrCodeFile_KeepsExistingQrCode()
        {
            using (var folder = new TemporaryFolder("FileAccessDiagnosticsTests_QrExisting"))
            {
                var qrPath = Path.Combine(folder.Path, "lang-qr-code.png");
                File.WriteAllBytes(qrPath, new byte[] { 1, 2, 3 });
                var denyWrite = new FileSystemAccessRule(
                    WindowsIdentity.GetCurrent().User,
                    FileSystemRights.WriteData,
                    AccessControlType.Deny
                );
                var fileInfo = new FileInfo(qrPath);
                var security = fileInfo.GetAccessControl();
                security.AddAccessRule(denyWrite);
                fileInfo.SetAccessControl(security);
                try
                {
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

                    var qrImages = dom.SafeSelectNodes("//img[contains(@class,'bloom-qrcode')]");
                    Assert.That(
                        qrImages.Length,
                        Is.EqualTo(1),
                        "the badge should keep its QR code"
                    );
                    Assert.That(qrImages[0].GetAttribute("src"), Is.EqualTo("lang-qr-code.png"));
                    Assert.That(
                        File.ReadAllBytes(qrPath),
                        Is.EqualTo(new byte[] { 1, 2, 3 }),
                        "sanity check: the write really was refused"
                    );
                }
                finally
                {
                    security.RemoveAccessRule(denyWrite);
                    fileInfo.SetAccessControl(security);
                }
            }
        }
    }
}
