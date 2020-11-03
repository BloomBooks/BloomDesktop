using System.Web;
using Bloom.WebLibraryIntegration;
using NUnit.Framework;

namespace BloomTests.WebLibraryIntegration
{
	[TestFixture]
	public class BloomLinkArgsTests
	{
		[Test]
		public void LinkArgs_WithTitle_GetsCorrectOrderAndTitle()
		{
			var order = HttpUtility.UrlEncode("whatever=&?!<>'\".BookOrder");
			var title = HttpUtility.UrlEncode("title=&?!<>'\"");
			var input = BloomLinkArgs.kBloomUrlPrefix + BloomLinkArgs.kOrderFile + "=" + order + "&title=" + title;
			var args = new BloomLinkArgs(input);
			Assert.That(args.OrderUrl, Is.EqualTo("whatever=&?!<>'\".BookOrder"));
			Assert.That(args.Title, Is.EqualTo("title=&?!<>'\""));
		}

		[Test]
		public void LinkArgs_WithoutTitle_GetsCorrectOrderAndTitle()
		{
			var order = HttpUtility.UrlEncode("somepath/whatever=.BookOrder");
			var input = BloomLinkArgs.kBloomUrlPrefix + BloomLinkArgs.kOrderFile + "=" + order;
			var args = new BloomLinkArgs(input);
			Assert.That(args.OrderUrl, Is.EqualTo("somepath/whatever=.BookOrder"));
			Assert.That(args.Title, Is.EqualTo("whatever="));
		}

		//BL-5419
		[Test]
		[Platform(Exclude="Linux", Reason="The Linux Firefox apparently URL-encodes non-ASCII characters.  The Mono HttpUtility.UrlDecode() method mangles non-ASCII characters.")]
		public void BloomLinkArgs_WithSpanishFromFirefox_DoesNotMangle()
		{
			var url =
				"bloom://localhost/order?orderFile=BloomLibraryBooks/chris_hurst%40sil.org%2f2bc65ef6-9c84-4b3a-af69-fcc6ed2682b4%2fComo+la+hormigita+salvó+al+pájaro+blanco%2fYanda+’Yar+Tururuwa+ta+Gode+wa+Farar+Tsuntsuwa.BloomBookOrder&title=Como%20la%20hormigita%20salvó%20al%20pájaro%20blanco%0A";
			var args = new BloomLinkArgs(url);
			//Assert.That(args.OrderUrl, Is.EqualTo("somepath/whatever=.BookOrder"));
			Assert.True(args.OrderUrl.Contains("pájaro"));
			Assert.True(args.Title.Contains("pájaro"));
		}

		//BL-5419. Chrome was ok, becuase it encoded the accented characters
		[Test]
		public void BloomLinkArgs_WithSpanishFromChrome_DoesNotMangle()
		{
			var url =
				"bloom://localhost/order?orderFile=BloomLibraryBooks/chris_hurst%40sil.org%2f2bc65ef6-9c84-4b3a-af69-fcc6ed2682b4%2fComo+la+hormigita+salv%c3%b3+al+p%c3%a1jaro+blanco%2fYanda+%e2%80%99Yar+Tururuwa+ta+Gode+wa+Farar+Tsuntsuwa.BloomBookOrder&title=Como%20la%20hormigita%20salv%C3%B3%20al%20p%C3%A1jaro%20blanco%0A";
			var args = new BloomLinkArgs(url);
			//Assert.That(args.OrderUrl, Is.EqualTo("somepath/whatever=.BookOrder"));
			Assert.True(args.OrderUrl.Contains("pájaro"));
			Assert.True(args.Title.Contains("pájaro"));
		}
	}
}
