using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Api;
using Bloom.Book;
using NUnit.Framework;

namespace BloomTests.web
{
	[TestFixture]
	public class EndpointHandlerTests
	{
		public static readonly object _portMonitor = new object();
		private EnhancedImageServer _server;

		[SetUp]
		public void Setup()
		{
			// as long as we're only using one, fixed port number, we need to prevent unit test runner
			// from running these tests in parallel.
			Monitor.Enter(_portMonitor);
			_server = new EnhancedImageServer(new BookSelection());
		}

		[TearDown]
		public void Teardown()
		{
			_server.Dispose();
			_server = null;
			Monitor.Exit(_portMonitor);
		}

		[Test]
		public void Get_OneParameter_KeyValueReceived()
		{
			var result = ApiTest.GetString(_server, endPoint: "test", query: "color=blue", returnType: ApiTest.ContentType.Text,
				 handler: request =>
				 {
					 Assert.That(request.RequiredParam("color"), Is.EqualTo("blue"));
					 request.ReplyWithText(request.RequiredParam("color"));
				 }
				);
			Assert.That(result, Is.EqualTo("blue"));
		}


		[Test]
		public void Post_JSON_JSONReceived()
		{
			var result = ApiTest.PostString(_server, endPoint: "test", data: "{\"color\": \"blue\"}", returnType: ApiTest.ContentType.JSON,
				handler: request =>
				{
					var requiredPostJson = request.RequiredPostJson();
					request.ReplyWithText(DynamicJson.Parse(requiredPostJson).color);
				});
			Assert.That(result, Is.EqualTo("blue"));
		}

		[Test]
		public void Get_EndPointHasTwoSegments_Works()
		{
			var result = ApiTest.GetString(_server, endPoint: "parent/child", query: "color=blue", returnType: ApiTest.ContentType.Text,
				 handler: request => request.Succeeded());
			Assert.That(result, Is.EqualTo("OK"));
		}


		[Test]
		public void Get_EndPointCaseIsIgnored()
		{
			var result = ApiTest.GetString(_server, endPoint: "fooBAR", endOfUrlForTest:"FOObar",
				 handler: request => request.Succeeded());
			Assert.That(result, Is.EqualTo("OK"));
		}

		[Test]
		public void Get_Unrecognized_Throws()
		{
			Assert.Throws<System.Net.WebException>(() => ApiTest.GetString(_server, endPoint: "foo[0-9]bar", endOfUrlForTest: "foobar",
				 handler: request => request.Succeeded()));
		}
		[Test]
		public void Get_RegexEndPoint()
		{
			var result = ApiTest.GetString(_server, endPoint: "foo[0-9]bar", endOfUrlForTest: "foo7bar",
				 handler: request => request.Succeeded());
			Assert.That(result, Is.EqualTo("OK"));
		}
	}
}
