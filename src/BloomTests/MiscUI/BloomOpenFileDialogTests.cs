using System;
using System.IO;
using System.Windows.Forms;
using Bloom;
using Bloom.MiscUI;
using Bloom.Utils;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.MiscUI
{
    /// <summary>
    /// Covers the answer an e2e test can arm for the next file, folder or Save As dialog
    /// (e2e/nextFileToChoose, E2eDialogAnswer). Only the armed path is testable here: an unarmed
    /// dialog opens a real one.
    /// </summary>
    [TestFixture]
    public class BloomOpenFileDialogTests
    {
        /// <summary>
        /// Both the armed path and --e2e live in statics that outlast a test; clear them so no
        /// later fixture finds a chooser already answered or Bloom still in e2e mode.
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            // Set, not Take: Take leaves the path alone when Bloom is not in e2e mode.
            E2eDialogAnswer.SetNextPathToChoose(null);
            Program.ParseStartupPortArguments(Array.Empty<string>(), out _);
        }

        [Test]
        public void ShowDialog_ArmedUnderE2e_ReturnsOkWithThatFileAndDisarms()
        {
            Program.ParseStartupPortArguments(new[] { "--e2e" }, out var errorMessage);
            Assert.That(errorMessage, Is.Null, "Sanity check.");
            using (var file = TempFile.WithExtension(".mp4"))
            using (var dialog = new BloomOpenFileDialog())
            {
                dialog.Filter = "Video files|*.mp4;*.webm";
                E2eDialogAnswer.SetNextPathToChoose(file.Path);

                Assert.That(dialog.ShowDialog(), Is.EqualTo(DialogResult.OK));
                Assert.That(dialog.FileName, Is.EqualTo(file.Path));
                Assert.That(dialog.FileNames, Is.EqualTo(new[] { file.Path }));

                Assert.That(
                    E2eDialogAnswer.TryTakeNextPathToChoose(out _),
                    Is.False,
                    "The answer is for one chooser only; the next one must show a dialog."
                );
            }
        }

        [Test]
        public void TryTakeNextPath_ArmedButNotE2e_IsIgnoredAndKept()
        {
            Program.ParseStartupPortArguments(Array.Empty<string>(), out _);
            Assert.That(Program.RunningE2eTests, Is.False, "Sanity check.");
            E2eDialogAnswer.SetNextPathToChoose(@"C:\somewhere\file.txt");

            Assert.That(E2eDialogAnswer.TryTakeNextPathToChoose(out var path), Is.False);
            Assert.That(path, Is.Null);
        }

        [Test]
        public void ShowDialog_ArmedWithMissingFile_Throws()
        {
            Program.ParseStartupPortArguments(new[] { "--e2e" }, out _);
            using (var dialog = new BloomOpenFileDialog())
            {
                E2eDialogAnswer.SetNextPathToChoose(
                    Path.Combine(Path.GetTempPath(), "BloomOpenFileDialogTests-missing.mp4")
                );
                Assert.Throws<FileNotFoundException>(() => dialog.ShowDialog());
            }
        }

        [Test]
        public void ShowDialog_ArmedWithFileTheFilterRefuses_Throws()
        {
            Program.ParseStartupPortArguments(new[] { "--e2e" }, out _);
            using (var file = TempFile.WithExtension(".txt"))
            using (var dialog = new BloomOpenFileDialog())
            {
                dialog.Filter = "Video files|*.mp4;*.webm";
                E2eDialogAnswer.SetNextPathToChoose(file.Path);
                Assert.Throws<ArgumentException>(() => dialog.ShowDialog());
            }
        }

        [Test]
        public void SaveAs_ArmedUnderE2e_ReturnsThatPathAndDisarms()
        {
            Program.ParseStartupPortArguments(new[] { "--e2e" }, out _);
            using (var folder = new TemporaryFolder("BloomOpenFileDialogTests"))
            {
                var path = Path.Combine(folder.Path, "book.pdf");
                Assert.That(File.Exists(path), Is.False, "Sanity check.");
                E2eDialogAnswer.SetNextPathToChoose(path);

                Assert.That(
                    MiscUtils.GetOutputFilePathOutsideCollectionFolder(
                        Path.Combine(folder.Path, "suggested.pdf"),
                        "PDF|*.pdf"
                    ),
                    Is.EqualTo(path)
                );
                Assert.That(
                    E2eDialogAnswer.TryTakeNextPathToChoose(out _),
                    Is.False,
                    "The answer is for one dialog only; the next one must show a dialog."
                );
                Assert.That(
                    E2eDialogAnswer.LastSuggestedFileName,
                    Is.EqualTo("suggested.pdf"),
                    "A test reads the name that the dialog would have suggested from here."
                );
            }
        }

        [Test]
        public void SaveAs_ArmedWithPathTheFilterRefuses_Throws()
        {
            Program.ParseStartupPortArguments(new[] { "--e2e" }, out _);
            using (var folder = new TemporaryFolder("BloomOpenFileDialogTests"))
            {
                E2eDialogAnswer.SetNextPathToChoose(Path.Combine(folder.Path, "book.epub"));
                Assert.Throws<ArgumentException>(() =>
                    MiscUtils.GetOutputFilePathOutsideCollectionFolder(
                        Path.Combine(folder.Path, "suggested.pdf"),
                        "PDF|*.pdf"
                    )
                );
            }
        }

        [Test]
        public void SaveAs_ArmedWithPathWithoutDrive_Throws()
        {
            Program.ParseStartupPortArguments(new[] { "--e2e" }, out _);
            var path = @"\Exports\book.pdf";
            Assert.That(Path.IsPathRooted(path), Is.True, "Sanity check: rooted, but not full.");
            E2eDialogAnswer.SetNextPathToChoose(path);
            Assert.Throws<ArgumentException>(() =>
                MiscUtils.GetOutputFilePathOutsideCollectionFolder(
                    Path.Combine(Path.GetTempPath(), "suggested.pdf"),
                    "PDF|*.pdf"
                )
            );
        }

        [Test]
        public void SaveAs_ArmedWithFileThatExists_Throws()
        {
            Program.ParseStartupPortArguments(new[] { "--e2e" }, out _);
            using (var file = TempFile.WithExtension(".pdf"))
            {
                Assert.That(File.Exists(file.Path), Is.True, "Sanity check.");
                E2eDialogAnswer.SetNextPathToChoose(file.Path);
                Assert.Throws<IOException>(() =>
                    MiscUtils.GetOutputFilePathOutsideCollectionFolder(
                        Path.Combine(Path.GetDirectoryName(file.Path), "suggested.pdf"),
                        "PDF|*.pdf"
                    )
                );
            }
        }

        [Test]
        public void ChooseFolder_ArmedUnderE2e_ReturnsThatFolder()
        {
            Program.ParseStartupPortArguments(new[] { "--e2e" }, out _);
            using (var folder = new TemporaryFolder("BloomOpenFileDialogTests"))
            {
                E2eDialogAnswer.SetNextPathToChoose(folder.Path);
                Assert.That(BloomFolderChooser.ChooseFolder(null), Is.EqualTo(folder.Path));
            }
        }
    }
}
