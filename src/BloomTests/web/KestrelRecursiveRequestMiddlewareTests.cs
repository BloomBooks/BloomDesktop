// Copyright (c) 2024 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)

using System;
using System.Threading.Tasks;
using Bloom.web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace BloomTests.web
{
    /// <summary>
    /// Unit tests for KestrelRecursiveRequestMiddleware Phase 2.3 checkpoint.
    /// Tests recursive request detection, tracking, and thread counting.
    /// </summary>
    [TestFixture]
    public class KestrelRecursiveRequestMiddlewareTests
    {
        private Mock<ILogger<KestrelRecursiveRequestMiddleware>> _mockLogger;
        private DefaultHttpContext _httpContext;

        [SetUp]
        public void SetUp()
        {
            _mockLogger = new Mock<ILogger<KestrelRecursiveRequestMiddleware>>();
            _httpContext = new DefaultHttpContext();
            _httpContext.Request.Path = "/test";
            _httpContext.Request.Method = "GET";
        }

        [Test]
        public void IsRecursiveRequestContext_WithGenerateThumbnailTrue_ReturnsTrue()
        {
            // Arrange
            _httpContext.Request.QueryString = new QueryString("?generateThumbnailIfNecessary=true");

            // Act
            bool result = KestrelRecursiveRequestMiddleware.IsRecursiveRequestContext(_httpContext);

            // Assert
            Assert.IsTrue(result, "Should detect recursive request when generateThumbnailIfNecessary=true");
        }

        [Test]
        public void IsRecursiveRequestContext_WithGenerateThumbnailFalse_ReturnsFalse()
        {
            // Arrange
            _httpContext.Request.QueryString = new QueryString("?generateThumbnailIfNecessary=false");

            // Act
            bool result = KestrelRecursiveRequestMiddleware.IsRecursiveRequestContext(_httpContext);

            // Assert
            Assert.IsFalse(result, "Should not detect recursive request when generateThumbnailIfNecessary=false");
        }

        [Test]
        public void IsRecursiveRequestContext_WithoutGenerateThumbnailParameter_ReturnsFalse()
        {
            // Arrange
            _httpContext.Request.QueryString = new QueryString("?otherparam=value");

            // Act
            bool result = KestrelRecursiveRequestMiddleware.IsRecursiveRequestContext(_httpContext);

            // Assert
            Assert.IsFalse(result, "Should not detect recursive request when generateThumbnailIfNecessary parameter is missing");
        }

        [Test]
        public void IsRecursiveRequestContext_WithEmptyQueryString_ReturnsFalse()
        {
            // Arrange
            _httpContext.Request.QueryString = new QueryString("");

            // Act
            bool result = KestrelRecursiveRequestMiddleware.IsRecursiveRequestContext(_httpContext);

            // Assert
            Assert.IsFalse(result, "Should not detect recursive request with empty query string");
        }

        [Test]
        public async Task InvokeAsync_NonRecursiveRequest_PassesToNextMiddleware()
        {
            // Arrange
            _httpContext.Request.QueryString = new QueryString("?otherparam=value");
            bool nextCalled = false;
            RequestDelegate next = (ctx) =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var middleware = new KestrelRecursiveRequestMiddleware(next, _mockLogger.Object);

            // Act
            await middleware.InvokeAsync(_httpContext);

            // Assert
            Assert.IsTrue(nextCalled, "Next middleware should be called for non-recursive requests");
            Assert.IsFalse(_httpContext.Items.ContainsKey("IsRecursiveRequest"), "Should not mark non-recursive request");
        }

        [Test]
        public async Task InvokeAsync_RecursiveRequest_MarksContextAndPassesToNext()
        {
            // Arrange
            _httpContext.Request.QueryString = new QueryString("?generateThumbnailIfNecessary=true");
            bool nextCalled = false;
            RequestDelegate next = (ctx) =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var middleware = new KestrelRecursiveRequestMiddleware(next, _mockLogger.Object);
            int initialRecursiveCount = KestrelRecursiveRequestMiddleware.RecursiveRequestCount;

            // Act
            await middleware.InvokeAsync(_httpContext);

            // Assert
            Assert.IsTrue(nextCalled, "Next middleware should be called for recursive requests");
            Assert.IsTrue(_httpContext.Items.ContainsKey("IsRecursiveRequest"), "Should mark recursive request in context");
            Assert.AreEqual(true, _httpContext.Items["IsRecursiveRequest"], "IsRecursiveRequest should be set to true");
            Assert.AreEqual(initialRecursiveCount, KestrelRecursiveRequestMiddleware.RecursiveRequestCount, "Recursive count should be reset after completion");
        }

        [Test]
        public async Task InvokeAsync_RecursiveRequest_IncrementsAndDecrementsCounters()
        {
            // Arrange
            _httpContext.Request.QueryString = new QueryString("?generateThumbnailIfNecessary=true");
            int recursiveCountDuringExecution = 0;
            int busyCountDuringExecution = 0;

            RequestDelegate next = (ctx) =>
            {
                recursiveCountDuringExecution = KestrelRecursiveRequestMiddleware.RecursiveRequestCount;
                busyCountDuringExecution = KestrelRecursiveRequestMiddleware.BusyRequestCount;
                return Task.CompletedTask;
            };

            var middleware = new KestrelRecursiveRequestMiddleware(next, _mockLogger.Object);
            int initialRecursiveCount = KestrelRecursiveRequestMiddleware.RecursiveRequestCount;
            int initialBusyCount = KestrelRecursiveRequestMiddleware.BusyRequestCount;

            // Act
            await middleware.InvokeAsync(_httpContext);

            // Assert
            Assert.AreEqual(initialRecursiveCount + 1, recursiveCountDuringExecution, "Recursive count should increment during execution");
            Assert.AreEqual(initialBusyCount + 1, busyCountDuringExecution, "Busy count should increment during execution");
            Assert.AreEqual(initialRecursiveCount, KestrelRecursiveRequestMiddleware.RecursiveRequestCount, "Recursive count should be restored after completion");
            Assert.AreEqual(initialBusyCount, KestrelRecursiveRequestMiddleware.BusyRequestCount, "Busy count should be restored after completion");
        }

        [Test]
        public async Task InvokeAsync_NonRecursiveRequest_OnlyIncrementsBusyCount()
        {
            // Arrange
            _httpContext.Request.QueryString = new QueryString("?otherparam=value");
            int recursiveCountDuringExecution = 0;
            int busyCountDuringExecution = 0;

            RequestDelegate next = (ctx) =>
            {
                recursiveCountDuringExecution = KestrelRecursiveRequestMiddleware.RecursiveRequestCount;
                busyCountDuringExecution = KestrelRecursiveRequestMiddleware.BusyRequestCount;
                return Task.CompletedTask;
            };

            var middleware = new KestrelRecursiveRequestMiddleware(next, _mockLogger.Object);
            int initialRecursiveCount = KestrelRecursiveRequestMiddleware.RecursiveRequestCount;
            int initialBusyCount = KestrelRecursiveRequestMiddleware.BusyRequestCount;

            // Act
            await middleware.InvokeAsync(_httpContext);

            // Assert
            Assert.AreEqual(initialRecursiveCount, recursiveCountDuringExecution, "Recursive count should not change for non-recursive requests");
            Assert.AreEqual(initialBusyCount + 1, busyCountDuringExecution, "Busy count should increment during execution");
            Assert.AreEqual(initialRecursiveCount, KestrelRecursiveRequestMiddleware.RecursiveRequestCount, "Recursive count should remain unchanged");
            Assert.AreEqual(initialBusyCount, KestrelRecursiveRequestMiddleware.BusyRequestCount, "Busy count should be restored after completion");
        }

        [Test]
        public async Task InvokeAsync_ExceptionInNext_ProperlyDecrementsCounters()
        {
            // Arrange
            _httpContext.Request.QueryString = new QueryString("?generateThumbnailIfNecessary=true");
            RequestDelegate next = (ctx) => throw new InvalidOperationException("Test exception");

            var middleware = new KestrelRecursiveRequestMiddleware(next, _mockLogger.Object);
            int initialRecursiveCount = KestrelRecursiveRequestMiddleware.RecursiveRequestCount;
            int initialBusyCount = KestrelRecursiveRequestMiddleware.BusyRequestCount;

            // Act & Assert
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => 
                await middleware.InvokeAsync(_httpContext));

            Assert.AreEqual("Test exception", ex.Message);
            Assert.AreEqual(initialRecursiveCount, KestrelRecursiveRequestMiddleware.RecursiveRequestCount, "Recursive count should be restored even after exception");
            Assert.AreEqual(initialBusyCount, KestrelRecursiveRequestMiddleware.BusyRequestCount, "Busy count should be restored even after exception");
        }
    }
}