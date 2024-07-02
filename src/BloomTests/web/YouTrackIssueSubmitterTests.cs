using System.IO;
using Bloom;
using NUnit.Framework;

namespace BloomTests.web
{
    [TestFixture]
    public class YouTrackIssueSubmitterTests
    {
        [Test]
        public void CanSubmitToYouTrack()
        {
            var tempfile1 = Path.GetTempFileName();
            string tempfile2 = null;
            try
            {
                var submitter = new YouTrackIssueSubmitter("AUT");

                // Create a file to be attached to the issue as it it created.
                File.WriteAllLines(tempfile1, new[] { @"This is a test.  This is only a test." });
                var filename1 = Path.GetFileName(tempfile1);
                var info1 = new FileInfo(tempfile1);
                submitter.AddAttachmentWhenWeHaveAnIssue(tempfile1);

                var issueId = submitter.SubmitToYouTrack(
                    "Test submission to YouTrack",
                    kLongDescription
                );
                try
                {
                    Assert.That(issueId, Does.StartWith("AUT-"));

                    var initialFiles = submitter.GetAttachmentDataForIssue(issueId);
                    Assert.That(
                        initialFiles,
                        Is.Not.Null,
                        "attachment data successfully fetched after creation"
                    );
                    Assert.That(
                        initialFiles.Count,
                        Is.EqualTo(1),
                        "unit test issue starts up with one file attached"
                    );
                    Assert.That(
                        initialFiles.ContainsKey(filename1),
                        Is.True,
                        "Initial file is attached"
                    );
                    Assert.That(
                        initialFiles[filename1],
                        Is.EqualTo(info1.Length),
                        "first attached file has right size to begin"
                    );

                    // Create file named after the issue and attach it to the issue.
                    var filename2 = issueId + ".tmp";
                    tempfile2 = Path.Combine(Path.GetTempPath(), filename2);
                    File.WriteAllLines(
                        tempfile2,
                        new[] { @"This is the second test file, named after the issue.", issueId }
                    );
                    var info2 = new FileInfo(tempfile2);
                    var added = submitter.AttachFileToExistingIssueFullyVisibile(
                        issueId,
                        tempfile2
                    );
                    Assert.That(added, Is.True, "file added to issue");

                    var finalFiles = submitter.GetAttachmentDataForIssue(issueId);
                    Assert.That(
                        finalFiles,
                        Is.Not.Null,
                        "attachment data successfully fetched at the end"
                    );
                    Assert.That(
                        finalFiles.Count,
                        Is.EqualTo(2),
                        "unit test issue ends up with two files attached"
                    );
                    Assert.That(
                        finalFiles.ContainsKey(filename1),
                        Is.True,
                        "Initial file is still attached"
                    );
                    Assert.That(
                        finalFiles[filename1],
                        Is.EqualTo(info1.Length),
                        "first attached file has right size at end"
                    );
                    Assert.That(
                        finalFiles.ContainsKey(filename2),
                        Is.True,
                        "Second file was attached"
                    );
                    Assert.That(
                        finalFiles[filename2],
                        Is.EqualTo(info2.Length),
                        "second attached file has right size at end"
                    );

                    // for now we're just testing that we can add a comment without an error. It would be difficult to read the comment back since it is restricted.
                    Assert.IsTrue(submitter.AddCommentToIssue(issueId, "This is a test comment."));
                }
                finally
                {
                    // no need to keep adding test issues indefinitely: clean up after ourselves by deleting the new issue.
                    var deleted = submitter.DeleteIssue(issueId);
                    Assert.That(deleted, Is.True, "unit test issue deleted");
                }
            }
            finally
            {
                // clean up the disk after ourselves.
                File.Delete(tempfile1);
                if (tempfile2 != null)
                    File.Delete(tempfile2);
            }
        }

        [Test]
        public void CanSubmitToYouTrackASecondTime()
        {
            // This test is much simpler: we just want to verify that the static HttpClient works for the
            // second submission.
            var submitter = new YouTrackIssueSubmitter("AUT");
            var issueId = submitter.SubmitToYouTrack(
                "Another test submission to YouTrack",
                kLongDescription
            );
            Assert.That(issueId, Does.StartWith("AUT-"));

            var initialFiles = submitter.GetAttachmentDataForIssue(issueId);
            Assert.That(
                initialFiles,
                Is.Not.Null,
                "attachment data successfully fetched after creation"
            );
            Assert.That(
                initialFiles.Count,
                Is.EqualTo(0),
                "unit test issue starts up with one file attached"
            );

            // no need to keep adding test issues indefinitely: clean up after ourselves by deleting the new issue.
            var deleted = submitter.DeleteIssue(issueId);
            Assert.That(deleted, Is.True, "unit test issue deleted");
        }

        // A real description, minus most of the log file.  User and machine name changed to protect the innocent.
        // Removing the last line of this description allows BloomTrackSharp 2020.1 to succeed, but it fails with
        // the description as it is.
        private const string kLongDescription =
            @"### Problem Description
I tried to import a picture and it gave me an error.

How much: 1 (It happens sometimes)

Error Report from Anonymous, Person (somewhere/org somebody) on 4/21/2020 7:24:45 PM UTC

#### Exception Details
Bloom had a problem importing this picture.
Bloom was not able to prepare that picture for including in the book. We'd like to investigate, so if possible, would you please email it to issues@bloomlibrary.org?
Fight the Virus all_Page_22.png

Msg: Bloom was not able to prepare that picture for including in the book. We'd like to investigate, so if possible, would you please email it to issues@bloomlibrary.org?
Fight the Virus all_Page_22.png
Class: System.ApplicationException
Source: Bloom
Assembly: Bloom, Version=4.7.108.0, Culture=neutral, PublicKeyToken=null
Stack:    at Bloom.ImageProcessing.ImageUtils.ProcessAndSaveImageIntoFolder(PalasoImage imageInfo, String bookFolderPath, Boolean isSameFile) in C:\BuildAgent\work\36d5c051eb50ceb8\src\BloomExe\ImageProcessing\ImageUtils.cs:line 152
   at Bloom.Edit.PageEditingModel.ChangePicture(String bookFolderPath, SafeXmlElement imgOrDivWithBackgroundImage, PalasoImage imageInfo, IProgress progress) in C:\BuildAgent\work\36d5c051eb50ceb8\src\BloomExe\Edit\PageEditingModel.cs:line 24
   at Bloom.Edit.EditingModel.ChangePicture(GeckoHtmlElement img, PalasoImage imageInfo, IProgress progress) in C:\BuildAgent\work\36d5c051eb50ceb8\src\BloomExe\Edit\EditingModel.cs:line 1121
Thread: 
Thread UI culture: en-US
Exception: System.ApplicationException
**Inner Exception:
Msg: Parameter is not valid.
Class: System.ArgumentException
Source: System.Drawing
Assembly: System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
Stack:    at System.Drawing.Bitmap..ctor(Int32 width, Int32 height, PixelFormat format)
   at System.Drawing.Bitmap..ctor(Image original, Int32 width, Int32 height)
   at System.Drawing.Bitmap..ctor(Image original)
   at Bloom.ImageProcessing.ImageUtils.ShouldChangeFormatToJpeg(Image image) in C:\BuildAgent\work\36d5c051eb50ceb8\src\BloomExe\ImageProcessing\ImageUtils.cs:line 309
   at Bloom.ImageProcessing.ImageUtils.ProcessAndSaveImageIntoFolder(PalasoImage imageInfo, String bookFolderPath, Boolean isSameFile) in C:\BuildAgent\work\36d5c051eb50ceb8\src\BloomExe\ImageProcessing\ImageUtils.cs:line 83
Thread: 
Thread UI culture: en-US
Exception: System.ArgumentException

#### Error Reporting Properties
**Version: Version 4.7.108 Beta**
CommandLine: ""C:\Users\Admin\AppData\Local\BloomBeta\app-4.7.108\BloomBeta.exe""
CurrentDirectory: C:\Users\Admin\AppData\Local\BloomBeta\app-4.7.108
MachineName: WINDOWS-10
OSVersion: Windows 10 
DotNetVersion: 4.0.30319.42000
WorkingSet: 40247296
UserDomainName: WINDOWS-10
UserName: Admin
Culture: en-US

#### Log

Tuesday, April 21, 2020
2:42:26 PM    App Launched with [""C:\Users\Admin\AppData\Local\BloomBeta\app-4.7.108\BloomBeta.exe"" ]
2:42:39 PM    Server will use http://localhost:8089/
2:42:39 PM    Selecting Tab Page: _collectionTab
2:42:40 PM    switched page in workspace: Memory Use (32-bit process): private 146,280K, virtual 697,984K, physical 185,156K, managed heap 43,395K,
        peak virtual 719,676K, peak physical 211,936K; system virtual 2,097,024K, system physical (RAM) 8,269,640K
2:42:40 PM    Entered Collections Tab
2:42:40 PM    switched page in workspace: Memory Use (32-bit process): private 146,448K, virtual 699,648K, physical 185,404K, managed heap 43,399K,
        peak virtual 719,676K, peak physical 211,936K; system virtual 2,097,024K, system physical (RAM) 8,269,640K
2:42:40 PM    Entered Collections Tab
2:42:41 PM    HtmlDom.ValidateBook(C:\Users\Admin\Documents\Bloom\COVID-19\Fight the Virus!\Fight the Virus!.htm): No Errors
2:42:41 PM    BookStorage Loading Dom from C:\Users\Admin\Documents\Bloom\COVID-19\Fight the Virus!\Fight the Virus!.htm
2:42:42 PM    HtmlDom.ValidateBook(C:\Users\Admin\Documents\Bloom\COVID-19\Fight the Virus!\Fight the Virus!.htm): No Errors
2:42:42 PM    BookStorage Loading Dom from C:\Users\Admin\Documents\Bloom\COVID-19\Fight the Virus!\Fight the Virus!.htm
2:42:43 PM    HtmlDom.ValidateBook(C:\Users\Admin\Documents\Bloom\COVID-19\Kamakhuwa ke bulwale bwa Korona\Kamakhuwa ke bulwale bwa Korona.htm): No Errors
2:42:43 PM    BookStorage Loading Dom from C:\Users\Admin\Documents\Bloom\COVID-19\Kamakhuwa ke bulwale bwa Korona\Kamakhuwa ke bulwale bwa Korona.htm
2:42:44 PM    HtmlDom.ValidateBook(C:\Users\Admin\Documents\Bloom\COVID-19\What you need to know about the CORONAVIRUS or COV\What you need to know about the CORONAVIRUS or COV.htm): No Errors
2:42:44 PM    BookStorage Loading Dom from C:\Users\Admin\Documents\Bloom\COVID-19\What you need to know about the CORONAVIRUS or COV\What you need to know about the CORONAVIRUS or COV.htm
2:42:44 PM    HtmlDom.ValidateBook(C:\Users\Admin\Documents\Bloom\COVID-19\Wĩtheme Kovid-19\Wĩtheme Kovid-19.htm): No Errors
2:42:44 PM    BookStorage Loading Dom from C:\Users\Admin\Documents\Bloom\COVID-19\Wĩtheme Kovid-19\Wĩtheme Kovid-19.htm
2:42:44 PM    Selecting Tab Page: _editTab
2:42:44 PM    switched page in workspace: Memory Use (32-bit process): private 203,864K, virtual 797,136K, physical 251,676K, managed heap 49,253K,
        peak virtual 813,876K, peak physical 275,120K; system virtual 2,097,024K, system physical (RAM) 8,269,640K
2:42:44 PM    Entered Edit Tab
2:42:45 PM    HtmlDom.ValidateBook(C:\Users\Admin\Documents\Bloom\COVID-19\Fight the Virus!\Fight the Virus!.htm): No Errors
2:42:45 PM    switched page in edit: Memory Use (32-bit process): private 202,444K, virtual 798,360K, physical 252,756K, managed heap 50,726K,
        peak virtual 813,876K, peak physical 275,120K; system virtual 2,097,024K, system physical (RAM) 8,269,640K
2:43:01 PM    Created image copy: C:\Users\Admin\Documents\Bloom\COVID-19\Fight the Virus!\FTV_p21_Fam_Doc1.png
2:43:02 PM
";
    }
}
