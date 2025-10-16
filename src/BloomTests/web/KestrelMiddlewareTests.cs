// Copyright (c) 2024 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.IO;
using System.Threading.Tasks;
using Bloom.Api;
using Bloom.Book;
using Bloom.Collection;
using Bloom.ImageProcessing;
using L10NSharp;
using L10NSharp.Windows.Forms;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using SIL.IO;
using SIL.Reporting;
using TemporaryFolder = SIL.TestUtilities.TemporaryFolder;
using ApiHttpMethods = Bloom.Api.HttpMethods;  // Disambiguate from ASP.NET Core

namespace BloomTests.web
{
    /// <summary>
    /// Unit tests for Phase 2.2 middleware components.
    /// Tests KestrelApiMiddleware and KestrelRequestInfo functionality.
    /// </summary>
    [TestFixture]
    public class KestrelMiddlewareTests
    {
        private Mock<ILogger<KestrelApiMiddleware>> _mockLogger;
        private Mock<BloomApiHandler> _mockApiHandler;
        private BookSelection _bookSelection;

        [SetUp]
        public void Setup()
        {
            ErrorReport.IsOkToInteractWithUser = false;
            _bookSelection = new BookSelection();
            _mockLogger = new Mock<ILogger<KestrelApiMiddleware>>();
            _mockApiHandler = new Mock<BloomApiHandler>(_bookSelection);
        }

        [TearDown]
        public void TearDown()
        {
            // Minimal cleanup
        }

        #region KestrelRequestInfo Tests

        [Test]
        public void KestrelRequestInfo_LocalPathWithoutQuery_ReturnsCorrectPath()
        {
            // Setup
            var context = CreateMockHttpContext("/bloom/api/test", "GET");
            var requestInfo = new KestrelRequestInfo(context);

            // Execute
            var path = requestInfo.LocalPathWithoutQuery;

            // Verify
            Assert.AreEqual("/bloom/api/test", path);
        }

        [Test]
        public void KestrelRequestInfo_LocalPathWithoutQuery_HandlesQueryString()
        {
            // Setup
            var context = CreateMockHttpContext("/bloom/api/test?param=value", "GET");
            var requestInfo = new KestrelRequestInfo(context);

            // Execute
            var path = requestInfo.LocalPathWithoutQuery;

            // Verify
            Assert.AreEqual("/bloom/api/test", path);
        }

        [Test]
        public void KestrelRequestInfo_HttpMethod_GET_ReturnsCorrectMethod()
        {
            // Setup
            var context = CreateMockHttpContext("/bloom/api/test", "GET");
            var requestInfo = new KestrelRequestInfo(context);

            // Execute
            var method = requestInfo.HttpMethod;

            // Verify
            Assert.AreEqual(ApiHttpMethods.Get, method);
        }

        [Test]
        public void KestrelRequestInfo_HttpMethod_POST_ReturnsCorrectMethod()
        {
            // Setup
            var context = CreateMockHttpContext("/bloom/api/test", "POST");
            var requestInfo = new KestrelRequestInfo(context);

            // Execute
            var method = requestInfo.HttpMethod;

            // Verify
            Assert.AreEqual(ApiHttpMethods.Post, method);
        }

        [Test]
        public void KestrelRequestInfo_WriteCompleteOutput_SetsHaveOutput()
        {
            // Setup
            var context = CreateMockHttpContext("/bloom/api/test", "GET");
            var requestInfo = new KestrelRequestInfo(context);

            // Execute
            requestInfo.WriteCompleteOutput("test output");

            // Verify
            Assert.IsTrue(requestInfo.HaveOutput);
        }

        [Test]
        public void KestrelRequestInfo_WriteError_SetsStatusCode()
        {
            // Setup
            var context = CreateMockHttpContext("/bloom/api/test", "GET");
            var requestInfo = new KestrelRequestInfo(context);

            // Execute
            requestInfo.WriteError(404);

            // Verify
            Assert.AreEqual(404, context.Response.StatusCode);
            Assert.IsTrue(requestInfo.HaveOutput);
        }

        #endregion

        #region KestrelApiMiddleware Tests

        [Test]
        public async Task KestrelApiMiddleware_NonApiRequest_PassesToNext()
        {
            // Setup
            var context = CreateMockHttpContext("/some-other-path", "GET");
            var nextCalled = false;
            RequestDelegate next = (ctx) => { nextCalled = true; return Task.CompletedTask; };
            var middleware = new KestrelApiMiddleware(next, _mockLogger.Object, _mockApiHandler.Object);

            // Execute
            await middleware.InvokeAsync(context);

            // Verify
            Assert.IsTrue(nextCalled, "Next middleware should have been called");
        }

        [Test]
        public async Task KestrelApiMiddleware_ApiRequest_CallsHandler()
        {
            // Setup - Test that API requests are routed to the handler
            var context = CreateMockHttpContext("/bloom/api/test", "GET");
            var nextCalled = false;
            RequestDelegate next = (ctx) => { nextCalled = true; return Task.CompletedTask; };
            
            // Use a real BloomApiHandler for this test
            var realApiHandler = new BloomApiHandler(_bookSelection);
            var middleware = new KestrelApiMiddleware(next, _mockLogger.Object, realApiHandler);

            // Execute
            await middleware.InvokeAsync(context);

            // Verify - Next should not be called since this is an API request
            Assert.IsFalse(nextCalled, "Next middleware should not have been called for API request");
            
            // The response should be 404 since no handlers are registered, which means the middleware worked
            Assert.AreEqual(404, context.Response.StatusCode, "Should return 404 for unhandled API requests");
        }

        [Test]
        public async Task KestrelApiMiddleware_ApiRequest_WithNullHandler_Returns500()
        {
            // Setup - Test error handling when handler is null
            var context = CreateMockHttpContext("/bloom/api/test", "GET");
            var nextCalled = false;
            RequestDelegate next = (ctx) => { nextCalled = true; return Task.CompletedTask; };
            
            // Use null handler to trigger exception
            var middleware = new KestrelApiMiddleware(next, _mockLogger.Object, null);

            // Execute
            await middleware.InvokeAsync(context);

            // Verify
            Assert.IsFalse(nextCalled, "Next middleware should not have been called");
            Assert.AreEqual(500, context.Response.StatusCode, "Should return 500 for null handler");
        }

        [Test]
        public async Task KestrelApiMiddleware_ApiPath_ExtractedCorrectly()
        {
            // Setup - Test that the API path is extracted correctly
            var context = CreateMockHttpContext("/bloom/api/some/deep/path", "GET");
            var nextCalled = false;
            RequestDelegate next = (ctx) => { nextCalled = true; return Task.CompletedTask; };
            
            var realApiHandler = new BloomApiHandler(_bookSelection);
            var middleware = new KestrelApiMiddleware(next, _mockLogger.Object, realApiHandler);

            // Execute
            await middleware.InvokeAsync(context);

            // Verify - Should process as API request
            Assert.IsFalse(nextCalled, "Next middleware should not have been called for API request");
        }

        #endregion

        #region Helper Methods

        private HttpContext CreateMockHttpContext(string path, string method)
        {
            var context = new DefaultHttpContext();
            
            // Parse path and query
            var parts = path.Split('?');
            var pathPart = parts[0];
            var queryPart = parts.Length > 1 ? parts[1] : "";

            context.Request.Path = pathPart;
            context.Request.Method = method;
            
            if (!string.IsNullOrEmpty(queryPart))
            {
                context.Request.QueryString = new QueryString("?" + queryPart);
            }

            // Create response body stream
            context.Response.Body = new MemoryStream();

            return context;
        }

        #endregion
    }
}