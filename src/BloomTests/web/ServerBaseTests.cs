using System;
using Bloom;
using Bloom.web;
using NUnit.Framework;

namespace BloomTests.web
{
	[TestFixture]
	public class ServerBaseTests
	{
		/// <summary>
		/// Subclass to access protected methods for testing.
		/// </summary>
		private class TestServerBase : ServerBase
		{
			static internal string TryGetLocalPathWithoutQuery(IRequestInfo info)
			{
				return GetLocalPathWithoutQuery(info);
			}
		}

		private class FakeRequestInfo : IRequestInfo
		{
			public FakeRequestInfo(string url)
			{
				RawUrl = url;
			}

			public string LocalPathWithoutQuery { get { throw new NotImplementedException(); } }
			public string ContentType { set { throw new NotImplementedException(); } }

			public string RawUrl { get; private set; }

			public void WriteCompleteOutput(string s) { throw new NotImplementedException(); }
			public void ReplyWithFileContent(string path) { throw new NotImplementedException(); }
			public void ReplyWithImage(string path) { throw new NotImplementedException(); }
			public void WriteError(int errorCode) { throw new NotImplementedException(); }
			public void WriteError(int errorCode, string errorDescription) { throw new NotImplementedException(); }
			public System.Collections.Specialized.NameValueCollection GetQueryString() { throw new NotImplementedException(); }
			public System.Collections.Specialized.NameValueCollection GetPostData() { throw new NotImplementedException(); }
		}

		[Test]
		public void TestGetLocalPathWithoutQuery()
		{
			const string test1 = "/tmp/foo.bar";
			var local = TestServerBase.TryGetLocalPathWithoutQuery(new FakeRequestInfo(test1.ToLocalhost()));
			Assert.AreEqual(test1, local);

			const string test2 = "//server/folder/file.ext";
			local = TestServerBase.TryGetLocalPathWithoutQuery(new FakeRequestInfo(test2.ToLocalhost()));
			Assert.AreEqual(test2, local);

			const string test3 = "/tmp/foo.png?thumbnail=1";
			const string test3init = "/tmp/foo.png";
			local = TestServerBase.TryGetLocalPathWithoutQuery(new FakeRequestInfo(test3.ToLocalhost()));
			Assert.AreEqual(test3init, local);

			const string test4 = "/tmp/This is 100% a #1 bad idea!? I would say.xyz";
			local = TestServerBase.TryGetLocalPathWithoutQuery(new FakeRequestInfo(test4.ToLocalhost()));
			Assert.AreEqual(test4, local);

			const string test5win = @"C:\Users\me\AppData\Local\Temp\file #1.png";
			const string test5forward = "C:/Users/me/AppData/Local/Temp/file #1.png";
			if (Environment.OSVersion.Platform == PlatformID.Win32NT)
				local = TestServerBase.TryGetLocalPathWithoutQuery(new FakeRequestInfo(test5win.ToLocalhost()));
			else
				local = TestServerBase.TryGetLocalPathWithoutQuery(new FakeRequestInfo(test5forward.ToLocalhost()));
			Assert.AreEqual(test5forward, local);
		}
	}
}
