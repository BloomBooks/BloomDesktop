using Bloom.SafeXml;
using NUnit.Framework;

namespace BloomTests.Book
{
    [TestFixture]
    public sealed class PageTests
    {
        private SafeXmlDocument GetDom()
        {
            var content =
                @"<?xml version='1.0' encoding='utf-8' ?>
				<html>
					<body class='a5Portrait'>
					<div class='bloom-page' testid='pageWithJustTokPisin'>
						 <p id='0'>
							<textarea lang='tpi'> Taim yu planim gaden yu save wokim banis.</textarea>
						</p>
					</div>
				<div class='bloom-page' id='pageWithTokPisinAndEnglish'>
							<p id='1'>
								<textarea lang='en' >1en</textarea>
								<textarea lang='tpi'>1tpi</textarea>
								<textarea lang='xyz'>1tpi</textarea>
							</p>
							<p id='2'>
								<textarea lang='en'>2en</textarea>
								<textarea lang='tpi'>2tpi</textarea>
							 </p>
						</div>
				</body>
				</html>
		";
            var dom = SafeXmlDocument.Create();
            dom.LoadXml(content);
            return dom;
        }
    }
}
