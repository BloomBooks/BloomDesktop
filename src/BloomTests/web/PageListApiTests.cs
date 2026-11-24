using Bloom.Book;
using Bloom.SafeXml;
using Bloom.web;
using Moq;
using NUnit.Framework;

namespace BloomTests.web
{
    [TestFixture]
    public class PageListApiTests
    {
        /// <summary>
        /// Builds a one-page DOM containing a single img with the given (already URL-encoded)
        /// src, runs MarkPicturesToBeServedSmall over it, and returns the resulting src.
        /// </summary>
        private static string GetThumbnailSrc(string originalSrc)
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                $"<div class='bloom-page'><div class='marginBox'><img src='{originalSrc}'/></div></div>"
            );
            var pageElement = (SafeXmlElement)dom.FirstChild;
            // Sanity check: we really did get the src we meant to start from.
            var img = (SafeXmlElement)pageElement.SafeSelectNodes(".//img")[0];
            Assert.AreEqual(
                originalSrc,
                img.GetAttribute("src"),
                "Setup failed: the img did not start out with the src we specified."
            );

            PageListApi.MarkPicturesToBeServedSmall(pageElement);

            return ((SafeXmlElement)pageElement.SafeSelectNodes(".//img")[0]).GetAttribute("src");
        }

        [Test]
        public void MarkPicturesToBeServedSmall_SimpleName_AddsThumbnailQuery()
        {
            Assert.AreEqual("picture.jpg?thumbnail=1", GetThumbnailSrc("picture.jpg"));
        }

        // BL-16658: the src of a thumbnail image must stay URL-encoded. If the encoding is lost,
        // characters like # and ? in the file name make the browser request the wrong thing
        // (or nothing at all), and the page list shows a broken image.
        [Test]
        public void MarkPicturesToBeServedSmall_NameWithSpecialCharacters_KeepsPathEncoded()
        {
            // This is the encoding of the real file name: This Image !@#$%^&()2.jpg
            const string encodedName = "This%20Image%20!%40%23%24%25%5e%26()2.jpg";

            var result = GetThumbnailSrc(encodedName);

            Assert.AreEqual(
                encodedName + "?thumbnail=1",
                result,
                "The path must remain URL-encoded and the query must remain literal."
            );
        }

        [Test]
        public void MarkPicturesToBeServedSmall_ExistingQuery_AppendsThumbnailAndKeepsPathEncoded()
        {
            const string encodedName = "This%20Image%20!%40%23%24%25%5e%26()2.jpg";

            var result = GetThumbnailSrc(encodedName + "?optional=true");

            Assert.AreEqual(encodedName + "?optional=true&thumbnail=1", result);
        }

        // A src that is genuinely a path, not a bare file name, must keep its separators usable.
        // "/api/" srcs are skipped earlier, but other rooted srcs (e.g. branding) are not.
        [Test]
        public void MarkPicturesToBeServedSmall_RootedPathSrc_KeepsSeparatorsUnescaped()
        {
            Assert.AreEqual(
                "/bloom/branding/Some-Brand/logo.png?thumbnail=1",
                GetThumbnailSrc("/bloom/branding/Some-Brand/logo.png"),
                "Path separators must not be escaped to %2f, or the server can't resolve the src."
            );
        }

        [Test]
        public void MarkPicturesToBeServedSmall_ApiUrl_LeftAlone()
        {
            const string brandingUrl = "/bloom/api/branding/image?id=cover-lower-left.png";

            Assert.AreEqual(brandingUrl, GetThumbnailSrc(brandingUrl));
        }

        [Test]
        public void MarkPicturesToBeServedSmall_BackgroundImage_KeepsPathEncoded()
        {
            const string encodedName = "This%20Image%20!%40%23%24%25%5e%26()2.jpg";
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                $"<div class='bloom-page'><div class='marginBox' style=\"background-image:url('{encodedName}')\"/></div>"
            );
            var pageElement = (SafeXmlElement)dom.FirstChild;

            PageListApi.MarkPicturesToBeServedSmall(pageElement);

            var marginBox = (SafeXmlElement)
                pageElement.SafeSelectNodes(".//*[contains(@style,'background-image')]")[0];
            Assert.AreEqual(
                $"background-image:url('{encodedName}?thumbnail=1')",
                marginBox.GetAttribute("style")
            );
        }

        /// <summary>
        /// The content GetReadOnlyPageContent makes for a one-page DOM holding an img and a
        /// background image, both named picture.jpg.
        /// </summary>
        private static string GetPageContent(bool fullSizePictures)
        {
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(
                "<div class='bloom-page' id='page1'><div class='marginBox' style=\"background-image:url('picture.jpg')\"><img src='picture.jpg'/></div></div>"
            );
            var page = new Mock<IPage>();
            page.Setup(p => p.GetDivNodeForThisPage()).Returns((SafeXmlElement)dom.FirstChild);
            return PageListApi.GetReadOnlyPageContent(page.Object, fullSizePictures);
        }

        [Test]
        public void GetReadOnlyPageContent_ForThePageList_MarksPicturesAsThumbnails()
        {
            var content = GetPageContent(false);

            Assert.That(content, Does.Contain("src=\"picture.jpg?thumbnail=1\""));
            Assert.That(content, Does.Contain("url('picture.jpg?thumbnail=1')"));
        }

        [Test]
        public void GetReadOnlyPageContent_FullSize_LeavesPicturesAlone()
        {
            var content = GetPageContent(true);

            Assert.That(content, Does.Contain("src=\"picture.jpg\""));
            Assert.That(content, Does.Contain("url('picture.jpg')"));
            Assert.That(content, Does.Not.Contain("thumbnail=1"));
        }

        /// <summary>
        /// A state machine editing page "a", with actions that do nothing.
        /// </summary>
        private static EditingStateMachine MakeStateMachineEditingPageA()
        {
            var machine = new EditingStateMachine(_ => { }, (_, __) => { }, () => true, () => { });
            machine.ToNavigating("a");
            machine.ToEditing("a");
            Assert.That(
                machine.CurrentState,
                Is.EqualTo(State.Editing),
                "Setup failed: the state machine should be editing page a."
            );
            return machine;
        }

        [Test]
        public void IsOnTheWayToPage_ClickTaken_True()
        {
            var machine = MakeStateMachineEditingPageA();

            var outcome = machine.SaveThenNavigate(null, () => "b", _ => { });

            Assert.That(outcome, Is.EqualTo(SaveOutcome.Saved));
            Assert.That(PageListApi.IsOnTheWayToPage(machine, "b"), Is.True);
        }

        // A click on page c that arrives while the Edit tab is still loading page b is declined by the
        // state machine: until the click is remembered (see the next test) the tab is on its way to a
        // page, but not to c.
        [Test]
        public void IsOnTheWayToPage_ClickArrivesWhileLoadingAnotherPage_False()
        {
            var machine = MakeStateMachineEditingPageA();
            machine.SaveThenNavigate(null, () => "b", _ => { });
            Assert.That(
                PageListApi.IsOnTheWayToPage(machine, "b"),
                Is.True,
                "Setup failed: the state machine should be loading page b."
            );

            var outcome = machine.SaveThenNavigate(null, () => "c", _ => { });

            Assert.That(outcome, Is.EqualTo(SaveOutcome.Declined));
            Assert.That(PageListApi.IsOnTheWayToPage(machine, "c"), Is.False);
        }

        // The page click handler remembers a click declined that way (GoToOnceLoaded), and the tab
        // goes there once page b has loaded, so the reply must say the click is opening page c: the
        // All Pages view keeps its wait cursor until c opens.
        [Test]
        public void IsOnTheWayToPage_ClickRememberedWhileLoadingAnotherPage_True()
        {
            var machine = MakeStateMachineEditingPageA();
            machine.SaveThenNavigate(null, () => "b", _ => { });
            Assert.That(
                machine.SaveThenNavigate(null, () => "c", _ => { }),
                Is.EqualTo(SaveOutcome.Declined),
                "Setup failed: a click while page b loads should be declined."
            );

            Assert.That(machine.GoToOnceLoaded("c"), Is.True);

            Assert.That(PageListApi.IsOnTheWayToPage(machine, "c"), Is.True);
        }

        [Test]
        public void IsOnTheWayToPage_PageHasLoaded_False()
        {
            var machine = MakeStateMachineEditingPageA();

            Assert.That(PageListApi.IsOnTheWayToPage(machine, "a"), Is.False);
        }
    }
}
