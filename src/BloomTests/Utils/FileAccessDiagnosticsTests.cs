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

        /// <summary>
        /// Calls the real Cloud Files API. A temporary folder is not under any sync root, so the API
        /// must answer "none" with a failure code rather than throw or invent a provider.
        /// </summary>
        [Test]
        public void GetCloudFilesSyncProvider_TempFolder_ReturnsNoneWithResultCode()
        {
            using (var folder = new TemporaryFolder("FileAccessDiagnosticsTests_CloudFiles"))
            {
                var provider = FileAccessDiagnostics.GetCloudFilesSyncProvider(
                    folder.Path,
                    out _,
                    out var result
                );

                Assert.That(provider, Is.Null);
                Assert.That(
                    result,
                    Does.StartWith("0x8"),
                    "the API should report why there is no sync root"
                );
            }
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
        public void GetLikelyCause_EachKindOfEvidence_NamesItsSuspect()
        {
            Assert.That(
                FileAccessDiagnostics.GetLikelyCause(null, null, null, 1),
                Does.Contain("Controlled Folder Access")
            );
            Assert.That(
                FileAccessDiagnostics.GetLikelyCause(null, null, "Dropbox", 0),
                Does.Contain("Dropbox")
            );
            Assert.That(
                FileAccessDiagnostics.GetLikelyCause(null, null, null, 0, 0x1),
                Does.Contain("cloud sync program"),
                "a Cloud Files placeholder"
            );
            Assert.That(
                FileAccessDiagnostics.GetLikelyCause((FileAttributes)0x400000, null, null, 0),
                Does.Contain("cloud sync program"),
                "the RecallOnDataAccess attribute"
            );
        }

        [Test]
        public void FileIsPresent_MissingFileOrFolder_False()
        {
            using (var folder = new TemporaryFolder("FileAccessDiagnosticsTests_Present"))
            {
                Assert.That(
                    FileAccessDiagnostics.FileIsPresent(Path.Combine(folder.Path, "nothing.png")),
                    Is.False
                );
                Assert.That(
                    FileAccessDiagnostics.FileIsPresent(Path.Combine(folder.Path, "no", "x.png")),
                    Is.False,
                    "a missing folder is not an access problem"
                );
                Assert.That(FileAccessDiagnostics.FileIsPresent(folder.Path), Is.False);
            }
        }

        [Test]
        public void Collect_ExistingFile_RunsEveryProbeWithoutError()
        {
            using (var folder = new TemporaryFolder("FileAccessDiagnosticsTests"))
            {
                var path = Path.Combine(folder.Path, "x.png");
                File.WriteAllText(path, "x");

                var result = FileAccessDiagnostics.Collect(path, out _);

                // Every probe ran against a real file; none fell into the catch-all.
                Assert.That(result, Does.Contain("opening the file read-only succeeded"));
                Assert.That(result, Does.Not.Contain("Caught exception"));
            }
        }

        /// <summary>
        /// A directory where the QR code png should be makes the write fail with access denied, as on
        /// the BL-16915 reporter's machine, and leaves no QR code file. That must be reported, not
        /// thrown, so the book can be selected. The badge still references the QR code file; a broken
        /// image makes the problem visible.
        /// </summary>
        [Test]
        public void UpdateQrCode_CannotWriteAndNoQrCodeFile_ReportsNonFatalProblemAndKeepsQrCodeImage()
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
                    Is.EqualTo(1),
                    "the badge should still reference the QR code file"
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
