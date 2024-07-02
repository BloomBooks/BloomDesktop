using System.Collections.Generic;
using System.IO;
using System.Xml;
using Bloom;
using Bloom.Book;
using Bloom.SafeXml;
using NUnit.Framework;
using SIL.IO;
using SIL.Linq;

namespace BloomTests.Book
{
    [TestFixture]
    public class XMatterHelperTests
    {
        private HtmlDom _dom;
        private Dictionary<string, string> _dataSet;

        private string _factoryXMatter;
        private string _testXmatter;

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            _factoryXMatter = BloomFileLocator.GetFactoryXMatterDirectory();
            var codeBaseDir = BloomFileLocator.GetCodeBaseFolder();
            _testXmatter = $"{codeBaseDir}/../../src/BloomTests/xMatter";
        }

        [SetUp]
        public void Setup()
        {
            _dom = new HtmlDom(
                @"<html><head> <link href='file://blahblah\\a5portrait.css' type='text/css' /></head><body><div id='bloomDataDiv'></div><div id ='firstPage' class='bloom-page'>1st page</div></body></html>"
            );
            _dataSet = new Dictionary<string, string>();
            _dataSet.Add("V", "xyz");
            _dataSet.Add("N1", "fr");
            _dataSet.Add("N2", "en");
        }

        private XMatterHelper CreatePaperSaverHelper(string xmatterName = "PaperSaver")
        {
            if (xmatterName == "PaperSaver")
                xmatterName = "Factory";

            return new XMatterHelper(_dom, xmatterName, new FileLocator(new[] { _factoryXMatter }));
        }

        [Test]
        public void PathToXMatterHtml_AllDefaults_Correct()
        {
            string pathToXMatterHtml = CreatePaperSaverHelper().PathToXMatterHtml;
            Assert.IsTrue(File.Exists(pathToXMatterHtml), pathToXMatterHtml);
        }

        [Test]
        public void GetStyleSheetFileName_AllDefaults_Correct()
        {
            Assert.AreEqual(
                "Factory-XMatter.css",
                CreatePaperSaverHelper().GetStyleSheetFileName()
            );
        }

        [TestCase("Device-XMatter.css", "Device")]
        [TestCase("Kyrgyzstan2020-XMatter.css", "Kyrgyzstan2020")]
        [TestCase("garbageInput.css", null)]
        public void GetXMatterFromStyleSheetFileName_AllDefaults_ExtractsPrefix(
            string filename,
            string expectedXMatterName
        )
        {
            string actual = XMatterHelper.GetXMatterFromStyleSheetFileName(filename);
            Assert.AreEqual(expectedXMatterName, actual, "XMatterName did not match.");
        }

        [Test]
        public void InjectXMatter_AllDefaults_Inserts3PagesBetweenDataDivAndFirstPage()
        {
            CreatePaperSaverHelper().InjectXMatter(_dataSet, Layout.A5Portrait, false, "en");
            AssertThatXmlIn
                .Dom(_dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//body/div[1][@id='bloomDataDiv']", 1);
            AssertThatXmlIn
                .Dom(_dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//body/div[2][contains(@class,'cover')]", 1);
            AssertThatXmlIn
                .Dom(_dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//body/div[3][contains(@class,'credits')]",
                    1
                );
            AssertThatXmlIn
                .Dom(_dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//body/div[4][contains(@class,'titlePage')]",
                    1
                );
            AssertThatXmlIn
                .Dom(_dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//body/div[5][@id='firstPage']", 1);
            AssertThatXmlIn
                .Dom(_dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//body/div[6][contains(@class,'bloom-backMatter')]",
                    1
                );
            AssertThatXmlIn
                .Dom(_dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//body/div[7][contains(@class,'bloom-backMatter')]",
                    1
                );
        }

        /// <summary>
        /// Initially, we were re-using "74731b2d-18b0-420f-ac96-6de20f659810" for every book,
        /// making the htmlthumbnailer's caching system totally messed up.
        /// </summary>
        [Test]
        public void InjectXMatter_IdsDifferentFromTemplate_SameAsPreviousInject()
        {
            var paperSaverHelper = CreatePaperSaverHelper();
            paperSaverHelper.InjectXMatter(_dataSet, Layout.A5Portrait, false, "en");
            var id1 = _dom.SelectSingleNode("//div[contains(@class,'cover')]").GetAttribute("id");
            var idSource = (
                paperSaverHelper.XMatterDom.SelectSingleNode("//div[contains(@class,'cover')]")
                as SafeXmlElement
            ).GetAttribute("id");
            var oldIds = new List<string>();
            XMatterHelper.RemoveExistingXMatter(_dom, oldIds);
            Setup(); //reset for another round
            CreatePaperSaverHelper()
                .InjectXMatter(_dataSet, Layout.A5Portrait, false, "en", oldIds);
            var id2 = _dom.SelectSingleNode("//div[contains(@class,'cover')]").GetAttribute("id");

            Assert.AreNotEqual(idSource, id1);
            Assert.AreNotEqual(idSource, id2);
            Assert.AreEqual(id1, id2);
        }

        [Test]
        public void InjectXMatter_SpanWithNameOfLanguage2_GetsLang()
        {
            var frontMatterDom = SafeXmlDocument.Create();
            frontMatterDom.LoadXml(
                @"<html><head> <link href='file://blahblah\\a5portrait.css' type='text/css' /></head><body>
						 <div class='bloom-page cover coverColor bloom-frontMatter' data-page='required'>
						 <span data-collection='nameOfLanguage' lang='N2'  class=''>{Regional}</span>
						</div></body></html>"
            );
            var helper = CreatePaperSaverHelper();
            helper.XMatterDom = frontMatterDom;

            helper.InjectXMatter(_dataSet, Layout.A5Portrait, false, "en");
            AssertThatXmlIn
                .Dom(_dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//div/span[@lang='en']", 1);
            //NB: it's not this class's job to actually fill in the value (e.g. English, in this case). Just to set it up so that a future process will do that.
        }

        [Test]
        public void InjectXMatter_HasBackMatter_BackMatterInjectedAtEnd()
        {
            var xMatterDom = SafeXmlDocument.Create();
            xMatterDom.LoadXml(
                @"<html><head> <link href='file://blahblah\\a5portrait.css' type='text/css' /></head><body>
						 <div class='bloom-page cover coverColor bloom-frontMatter' data-page='required'>
						 <span data-collection='nameOfLanguage' lang='N2'  class=''>{Regional}</span>
						</div>
						<div class='bloom-page cover coverColor bloom-backMatter insideBackCover' data-page='required'>
						 <span data-collection='nameOfLanguage' lang='N2'  class=''>{Regional}</span>
						</div>
						<div class='bloom-page cover coverColor bloom-backMatter outsideBackCover' data-page='required'>
						 <span data-collection='nameOfLanguage' lang='N2'  class=''>{Regional}</span>
						</div>
						</body></html>"
            );
            var helper = CreatePaperSaverHelper();
            helper.XMatterDom = xMatterDom;

            helper.InjectXMatter(_dataSet, Layout.A5Portrait, false, "en");
            AssertThatXmlIn
                .Dom(_dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//body/div[1][@id='bloomDataDiv']", 1);
            AssertThatXmlIn
                .Dom(_dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//body/div[2][contains(@class,'cover')]", 1);
            AssertThatXmlIn
                .Dom(_dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath("//body/div[3][@id='firstPage']", 1);
            AssertThatXmlIn
                .Dom(_dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//body/div[4][contains(@class,'bloom-backMatter')]",
                    1
                );
            AssertThatXmlIn
                .Dom(_dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//body/div[5][contains(@class,'bloom-backMatter')]",
                    1
                );
            //NB: it's not this class's job to actually fill in the value (e.g. English, in this case). Just to set it up so that a future process will do that.
        }

        [Test]
        public void InjectXMatter_GenericLanguageFieldsHaveCorrectEffectiveLangAttribute()
        {
            var xMatterDom = SafeXmlDocument.Create();
            var dom = new HtmlDom(
                @"<html><head></head><body>
					<div class='bloom-page titlePage bloom-frontMatter'>
		                <div class='bloom-editable' lang='en' data-book='bookTitle'>
		                    <p>The Moon and the Cap</p>
		                </div>
						<div class='langName bloom-writeOnly' data-library='languageLocation'></div>
					</div>
					<div class='bloom-page credits bloom-frontMatter'>
						<div class='copyright Credits-Page-style' data-derived='originalCopyrightAndLicense' lang='*'>
			                This book is an adaptation of the original, <cite data-book='originalTitle'>The Moon and the Cap</cite>, Copyright © 2007, Pratham Books. Licensed under CC BY 4.0.
			            </div>
					</div>
				</body></html>"
            );
            var helper = new XMatterHelper(
                dom,
                "Factory",
                new FileLocator(new[] { _factoryXMatter })
            );
            xMatterDom.LoadXml(
                @"<html><head></head><body>
					<div class='bloom-page titlePage bloom-frontMatter' data-page='required'>
		                <div class='bloom-editable' lang='en' data-book='bookTitle'>
		                </div>
						<div class='langName bloom-writeOnly' data-library='languageLocation'></div>
					</div>
					<div class='bloom-page credits bloom-frontMatter' data-page='required'>
						<div class='copyright Credits-Page-style' data-derived='originalCopyrightAndLicense' lang='*'>
			            </div>
					</div>
				</body></html>"
            );
            helper.XMatterDom = xMatterDom;

            // SUT
            helper.InjectXMatter(_dataSet, Layout.A5Portrait, false, "ru");

            // The closest parent with a meaningful lang needs to have language2. Then we get the proper font. See BL-8545.
            var languageLocationDiv = dom.SelectSingleNode(
                "//div[@data-library='languageLocation']"
            );
            var originalCopyrightAndLicenseDiv = dom.SelectSingleNode(
                "//div[@data-derived='originalCopyrightAndLicense']"
            );
            SafeXmlElement[] elementsToCheck = { languageLocationDiv, originalCopyrightAndLicenseDiv };
            elementsToCheck.ForEach(elementToCheck =>
            {
                string lang = languageLocationDiv.GetAttribute("lang");
                while (string.IsNullOrEmpty(lang) || lang == "*")
                {
                    elementToCheck = (SafeXmlElement)elementToCheck.ParentNode;
                    lang = elementToCheck.GetAttribute("lang");
                }

                Assert.That(lang, Is.EqualTo("ru"));
            });

            // Verify we didn't break other fields
            var titleDiv = dom.SelectSingleNode("//div[@data-book='bookTitle']");
            Assert.That(titleDiv.GetAttribute("lang"), Is.EqualTo("en"));
        }

        [Test]
        public void SuperPaperSaver_BookRequiresFacingPages_FlyleafInserted()
        {
            RunHelperForBookStartingWithSpread("SuperPaperSaver");
            AssertThatXmlIn
                .Dom(_dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//body/div[3][@id and contains(@class, 'bloom-flyleaf')]",
                    1
                );
        }

        [Test]
        public void Traditional_BookRequiresFacingPages_FlyleafInserted()
        {
            RunHelperForBookStartingWithSpread("Traditional");
            AssertThatXmlIn
                .Dom(_dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//body/div[5][contains(@class, 'bloom-flyleaf')]",
                    1
                );
        }

        [Test]
        public void PaperSaver_BookDoesNotRequireFacingPages_FlyleafNotInserted()
        {
            CreatePaperSaverHelper().InjectXMatter(_dataSet, Layout.A5Portrait, false, "en");
            AssertThatXmlIn
                .Dom(_dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//body/div[contains(@class, 'bloom-flyleaf')]",
                    0
                );
        }

        //for paper saver, we already place the first book page facing the second, so we should not insert a flyleaf
        [Test]
        public void SuperPaperSaver_BookRequiresFacingPages_FlyleafNotInserted()
        {
            RunHelperForBookStartingWithSpread("PaperSaver");
            AssertThatXmlIn
                .Dom(_dom.RawDom)
                .HasSpecifiedNumberOfMatchesForXpath(
                    "//body/div[contains(@class, 'bloom-flyleaf')]",
                    0
                );
        }

        private void RunHelperForBookStartingWithSpread(string xmatterPackName)
        {
            _dom = new HtmlDom(
                @"<html><body>
									 <div class='bloom-page' data-page='required spread-start'>
									 <span data-collection='nameOfLanguage' lang='N2'  class=''>{Regional}</span>
									</div>
									</body></html>"
            );

            var helper = CreatePaperSaverHelper(xmatterPackName);
            helper.InjectXMatter(_dataSet, Layout.A5Portrait, false, "en");
        }

        [TestCase("Factory", false, "Factory-XMatter.css")]
        [TestCase("Factory", true, "Device-XMatter.css")]
        [TestCase("Test", false, "Test-XMatter.css")]
        [TestCase("Test", true, "Test-Device-XMatter.css")]
        public void Constructor_CreateDeviceHelperIfRequested(
            string baseXmatterName,
            bool useDeviceVersionIfAvailable,
            string expected
        )
        {
            var fileLocator = new FileLocator(new[] { _factoryXMatter, _testXmatter });
            var helper = new XMatterHelper(
                _dom,
                baseXmatterName,
                fileLocator,
                useDeviceVersionIfAvailable
            );

            Assert.AreEqual(expected, helper.GetStyleSheetFileName());
        }

        [Test]
        [TestCase(
            "<meta name='xmatter' content='SuperPaperSaver'></meta>",
            "SuperPaperSaver-XMatter.css"
        )]
        [TestCase("<meta name='xmatter' content=''></meta>", "Factory-XMatter.css")]
        [TestCase("<meta name='xmatter' content=' \t '></meta>", "Factory-XMatter.css")]
        [TestCase("<meta name='xmatter' content='DoesNotExist'></meta>", "Factory-XMatter.css")]
        [TestCase("", "Factory-XMatter.css")]
        [TestCase("<meta name='xmatter' content='Test'></meta>", "Test-XMatter.css")]
        [TestCase("<meta name='xmatter' content='Test'></meta>", "Test-Device-XMatter.css", true)]
        [TestCase(
            "<meta name='xmatter' content='SuperPaperSaver'></meta>",
            "Device-XMatter.css",
            true
        )]
        public void TestBookSpecifiesXMatter(
            string xmatterBook,
            string expected,
            bool useDeviceVersionIfAvailable = false
        )
        {
            var fileLocator = new FileLocator(new[] { _factoryXMatter, _testXmatter });

            // Test that the XMatterHelper finds a required xmatter setting.
            var dom1 = new HtmlDom(
                "<html>"
                    + "<head>"
                    + "<meta charset='UTF-8'></meta>"
                    + "<meta name='BloomFormatVersion' content='2.0'></meta>"
                    + "<meta name='pageTemplateSource' content='Basic Book'></meta>"
                    + xmatterBook
                    + "</head>"
                    + "<body>"
                    + "<div id='bloomDataDiv'>"
                    + "<div data-book='contentLanguage1' lang='*'>en</div>"
                    + "<div data-book='contentLanguage1Rtl' lang='*'>False</div>"
                    + "<div data-book='languagesOfBook' lang='*'>English</div>"
                    + "</div>"
                    + "</body>"
                    + "</html>"
            );
            XMatterHelper helper1;
            if (xmatterBook.Contains("DoesNotExist"))
            {
                using (new NonFatalProblem.ExpectedByUnitTest())
                {
                    helper1 = new XMatterHelper(
                        dom1,
                        "Factory",
                        fileLocator,
                        useDeviceVersionIfAvailable
                    );
                }
            }
            else
            {
                helper1 = new XMatterHelper(
                    dom1,
                    "Factory",
                    fileLocator,
                    useDeviceVersionIfAvailable
                );
            }
            if (xmatterBook.Contains("DoesNotExist") || string.IsNullOrEmpty(xmatterBook))
            {
                // An xmatter specification that cannot be found should be removed from the DOM.
                // An empty xmatter specification is also removed.
                Assert.That(dom1.GetMetaValue("xmatter", null), Is.Null);
            }
            else
            {
                // Verify that we may have what we want for the xmatter specification, valid or not.
                Assert.That(dom1.GetMetaValue("xmatter", null), Is.Not.Null);
            }
            Assert.That(helper1.GetStyleSheetFileName(), Is.EqualTo(expected));
        }

        [Test]
        public void InjectDefaultUserStylesFromXMatter_InjectsOnlyStylesNotPresent()
        {
            _dom = new HtmlDom(
                @"
<html>
	<head>
		<style type=""text/css"" title=""userModifiedStyles"">
/*<![CDATA[*/
.Title-Front-Cover-style { font-size: 28pt !important; font-family:""Times New Roman""; color:white;     text-align: center;}
.BigWords-style { font-size: 45pt !important; text-align: center !important; }
/*]]>*/
		</style>
		<link href='file://blahblah\\a5portrait.css' type='text/css' />
	</head>
	<body>
		<div id='bloomDataDiv'></div>
		<div id ='firstPage' class='bloom-page'>1st page</div>
	</body>
</html>"
            );
            var helper = new XMatterHelper(
                _dom,
                "Uzbekistan2023",
                new FileLocator(new[] { Path.Combine(_factoryXMatter, "project-specific") })
            );

            helper.InjectDefaultUserStylesFromXMatter();

            var stylesNode = _dom.SelectSingleNode("//head/style[@title='userModifiedStyles']");
            var styles = stylesNode.InnerText;
            Assert.That(
                styles,
                Contains.Substring(
                    @".Title-Front-Cover-style { font-size: 28pt !important; font-family:""Times New Roman"";"
                ),
                "Should not have overwritten existing style"
            );
            Assert.That(
                styles,
                Contains.Substring(
                    @".Cover-Lower-Credits-style { font-size: 16pt !important; font-style:italic; font-family: Aileron, Arial;"
                ),
                "Should have inserted default version of missing style"
            );
            var stylesXml = stylesNode.InnerXml.Trim();
            Assert.That(stylesXml.StartsWith("/*<![CDATA[*/"));
            Assert.That(stylesXml.EndsWith("/*]]>*/"));
            // Guards against a regression to a problem where the closing wrapper got doubled.
            Assert.That(stylesXml.Substring(13), Does.Not.Contain("<![CDATA["));
            Assert.That(stylesXml.Substring(0, stylesXml.Length - 7), Does.Not.Contain("]]>"));
        }

        //		TODO: at the moment, we'd have to create a whole xmatter folder
        //		/// <summary>
        //		/// NB: It's not clear what the behavior should eventually be... how do we know it isn't supposed to be in english?
        //		/// But for now, this gives us the behavior we want on the title page
        //		/// </summary>
        //		[Test]
        //		public void CreateBookOnDiskFromTemplate_HasParagraphMarkedV_ConvertsToVernacular()//??????????????
        //		{
        //			_starter.TestingSoSkipAddingXMatter = true;
        //			var body = @" < div class='bloom-page'>
        //                        <p id='bookTitle' lang='en' data-book='bookTitle'>Book Title</p>
        //                    </div>";
        //			string sourceTemplateFolder = GetShellBookFolder(body);
        //			var path = GetPathToHtml(_starter.CreateBookOnDiskFromTemplate(sourceTemplateFolder, _projectFolder.Path));
        //			AssertThatXmlIn.HtmlFile(path).HasSpecifiedNumberOfMatchesForXpath("//p[@lang='xyz']", 1);
        //		}


        //TODO: tests with a different paper size

        //TODO: tests with a custom pack, with images

        //TODO: test with custom pack and a paper size/orientation that we're missing a css for, should fall back to factory

        //TODO: test with defaults but a paper size/orientation that we're missing a css for, should warn and use a5portrait
    }
}
