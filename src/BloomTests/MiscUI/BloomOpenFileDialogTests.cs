using System;
using System.IO;
using System.Windows.Forms;
using Bloom;
using Bloom.MiscUI;
using NUnit.Framework;
using SIL.IO;
using SIL.TestUtilities;

namespace BloomTests.MiscUI
{
    /// <summary>
    /// Covers the answer an e2e test can arm for the next file chooser (e2e/nextFileToChoose).
    /// Only the armed path is testable here: an unarmed ShowDialog opens a real dialog.
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
            BloomOpenFileDialog.SetNextPathToChooseInE2eTests(null);
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
                BloomOpenFileDialog.SetNextPathToChooseInE2eTests(file.Path);

                Assert.That(dialog.ShowDialog(), Is.EqualTo(DialogResult.OK));
                Assert.That(dialog.FileName, Is.EqualTo(file.Path));
                Assert.That(dialog.FileNames, Is.EqualTo(new[] { file.Path }));

                Assert.That(
                    BloomOpenFileDialog.TryTakeNextPathToChooseInE2eTests(out _),
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
            BloomOpenFileDialog.SetNextPathToChooseInE2eTests(@"C:\somewhere\file.txt");

            Assert.That(
                BloomOpenFileDialog.TryTakeNextPathToChooseInE2eTests(out var path),
                Is.False
            );
            Assert.That(path, Is.Null);
        }

        [Test]
        public void ShowDialog_ArmedWithMissingFile_Throws()
        {
            Program.ParseStartupPortArguments(new[] { "--e2e" }, out _);
            using (var dialog = new BloomOpenFileDialog())
            {
                BloomOpenFileDialog.SetNextPathToChooseInE2eTests(
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
                BloomOpenFileDialog.SetNextPathToChooseInE2eTests(file.Path);
                Assert.Throws<ArgumentException>(() => dialog.ShowDialog());
            }
        }

        [Test]
        public void ChooseFolder_ArmedUnderE2e_ReturnsThatFolder()
        {
            Program.ParseStartupPortArguments(new[] { "--e2e" }, out _);
            using (var folder = new TemporaryFolder("BloomOpenFileDialogTests"))
            {
                BloomOpenFileDialog.SetNextPathToChooseInE2eTests(folder.Path);
                Assert.That(BloomFolderChooser.ChooseFolder(null), Is.EqualTo(folder.Path));
            }
        }
    }
}
