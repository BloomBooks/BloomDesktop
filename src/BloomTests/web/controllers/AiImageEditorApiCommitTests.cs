using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using Bloom;
using Bloom.Api;
using Bloom.Book;
using Bloom.web.controllers;
using BloomTests.Book;
using Moq;
using NUnit.Framework;
using SIL.IO;

namespace BloomTests.web.controllers
{
    /// <summary>
    /// Tests of the AI image editor's commit endpoint as a whole, run against a book whose
    /// storage is mocked and with no page open in the Edit tab, so every slot is applied
    /// in C#.
    /// </summary>
    [TestFixture]
    public class AiImageEditorApiCommitTests : BookTestsBase
    {
        private const string kSessionToken = "test-session";

        protected override string GetTestFolderName() => "AiImageEditorApiCommitTests";

        // Writes a small real PNG, since the commit decodes and re-encodes the new picture.
        private static void MakePng(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var bitmap = new Bitmap(10, 10))
            {
                RobustImageIO.SaveImage(bitmap, path, ImageFormat.Png);
            }
        }

        // The commit endpoint accepts only the session that a launch started for the
        // selected book. A launch needs the Edit tab, so set the session up directly.
        private static void StartSession(AiImageEditorApi api, string bookFolderPath)
        {
            SetPrivateField(api, "_sessionToken", kSessionToken);
            SetPrivateField(api, "_sessionBookFolderPath", bookFolderPath);
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            var field = target
                .GetType()
                .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"setup: AiImageEditorApi should have a {name} field");
            field.SetValue(target, value);
        }

        [Test]
        public void HandleCommit_ReplacesAnOffPagePicture_KeepsTheFileItReplaced()
        {
            // The superseded file has the "ai-image" name that a previous commit gives a
            // picture, so it is the kind of file that the page's undo history may need back.
            SetDom(
                @"<div class='bloom-page' id='page1'><div class='marginBox'>
                    <div class='bloom-imageContainer'><img src='ai-image1.png'/></div>
                  </div></div>"
            );
            var book = CreateBook();
            var oldPicture = Path.Combine(book.FolderPath, "ai-image1.png");
            MakePng(oldPicture);
            MakePng(
                Path.Combine(
                    book.FolderPath,
                    AiImageEditorApi.kWorkingFolderName,
                    "history",
                    "result1.png"
                )
            );

            var selection = new Mock<BookSelection>();
            selection.Setup(s => s.CurrentSelection).Returns(book);
            var api = new AiImageEditorApi(selection.Object);
            StartSession(api, book.FolderPath);

            var requestInfo = new PretendRequestInfo(
                BloomServer.ServerUrlWithBloomPrefixEndingInSlash
                    + "api/aiImageEditor/commit?session="
                    + kSessionToken,
                HttpMethods.Post
            );
            requestInfo.SetPostJson(
                "{\"replacements\":[{\"incomingId\":\"page1:0\",\"resultId\":\"result1\"}]}"
            );

            api.HandleCommit(new ApiRequest(requestInfo, _collectionSettings, book));

            var newSrc = book
                .OurHtmlDom.SelectSingleNode("//div[@id='page1']//img")
                .GetAttribute("src");
            Assert.That(
                newSrc,
                Is.Not.EqualTo("ai-image1.png"),
                $"the commit should have repointed the slot; it replied {requestInfo.ReplyContents}"
            );
            Assert.That(
                File.Exists(Path.Combine(book.FolderPath, newSrc)),
                Is.True,
                "the new picture should be in the book folder"
            );
            Assert.That(
                File.Exists(oldPicture),
                Is.True,
                "a commit must not delete the picture it replaced, because an undo may need it"
            );
        }
    }
}
