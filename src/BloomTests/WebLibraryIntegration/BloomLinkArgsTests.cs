using Bloom.WebLibraryIntegration;
using NUnit.Framework;
using RestSharp.Extensions.MonoHttp;

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
	}
}
