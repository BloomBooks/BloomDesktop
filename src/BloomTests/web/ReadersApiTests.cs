using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Edit;
using Bloom.Api;
using NUnit.Framework;

namespace BloomTests.web
{
	[TestFixture]
	public class ReadersApiTests
	{
		private EnhancedImageServer _server;
		[SetUp]
		public void Setup()
		{
			_server = new EnhancedImageServer();
			ReadersApi.Init(_server); //this won't get us far, as there is no project context setup
		}

		[TearDown]
		public void TearDown()
		{
			_server.Dispose();
			_server = null;
		}

		[Test]
		public void Get_Test_Works()
		{
			var result = ApiTest.GetString(_server,"readers/test");
			Assert.That(result, Is.EqualTo("OK"));
		}
	}
}