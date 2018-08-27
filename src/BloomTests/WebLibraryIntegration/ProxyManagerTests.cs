// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using NUnit.Framework;
using Bloom.WebLibraryIntegration;

namespace BloomTests.WebLibraryIntegration
{
	[TestFixture]
	public class ProxyManagerTests
	{
		private class ProxyManagerDouble: ProxyManager
		{
			public ProxyManagerDouble(string proxy)
			{
				Parse(proxy);
			}
		}

		private string ProxyVariableLowercase { get; set; }
		private string ProxyVariableUppercase { get; set; }

		[OneTimeSetUp]
		public void FixtureSetup()
		{
			ProxyVariableLowercase = Environment.GetEnvironmentVariable("http_proxy");
			ProxyVariableUppercase = Environment.GetEnvironmentVariable("HTTP_PROXY");
		}

		[OneTimeTearDown]
		public void FixtureTearDown()
		{
			Environment.SetEnvironmentVariable("http_proxy", ProxyVariableLowercase);
			Environment.SetEnvironmentVariable("HTTP_PROXY", ProxyVariableUppercase);
		}

		[SetUp]
		public void Setup()
		{
			Environment.SetEnvironmentVariable("http_proxy", null);
			Environment.SetEnvironmentVariable("HTTP_PROXY", null);
		}

		[Test]
		public void NoProxy_DefaultsToNull()
		{
			var proxy = new ProxyManagerDouble(null);

			Assert.That(proxy.Hostname, Is.Null);
			Assert.That(proxy.Port, Is.EqualTo(0));
			Assert.That(proxy.Username, Is.Null);
			Assert.That(proxy.Password, Is.Null);
		}

		[Test]
		public void ProxyNoPort_DefaultsToPort80()
		{
			var proxy = new ProxyManagerDouble("http://example.com");

			Assert.That(proxy.Hostname, Is.EqualTo("example.com"));
			Assert.That(proxy.Port, Is.EqualTo(80));
			Assert.That(proxy.Username, Is.Null);
			Assert.That(proxy.Password, Is.Null);
		}

		[Test]
		public void ProxyNoProtocol_DefaultsToNull()
		{
			var proxy = new ProxyManagerDouble("example.com");

			Assert.That(proxy.Hostname, Is.Null);
			Assert.That(proxy.Port, Is.EqualTo(0));
			Assert.That(proxy.Username, Is.Null);
			Assert.That(proxy.Password, Is.Null);
		}

		[Test]
		public void ProxyAndPort_DefaultsToUsernameNull()
		{
			var proxy = new ProxyManagerDouble("http://example.com:8080");

			Assert.That(proxy.Hostname, Is.EqualTo("example.com"));
			Assert.That(proxy.Port, Is.EqualTo(8080));
			Assert.That(proxy.Username, Is.Null);
			Assert.That(proxy.Password, Is.Null);
		}

		[Test]
		public void ProxyAndPortAndUser_DefaultsToPasswordNull()
		{
			var proxy = new ProxyManagerDouble("http://user@example.com:8080");

			Assert.That(proxy.Hostname, Is.EqualTo("example.com"));
			Assert.That(proxy.Port, Is.EqualTo(8080));
			Assert.That(proxy.Username, Is.EqualTo("user"));
			Assert.That(proxy.Password, Is.Null);
		}

		[Test]
		public void ProxyAndPortAndUserAndPassword()
		{
			var proxy = new ProxyManagerDouble("http://user:password@example.com:8080");

			Assert.That(proxy.Hostname, Is.EqualTo("example.com"));
			Assert.That(proxy.Port, Is.EqualTo(8080));
			Assert.That(proxy.Username, Is.EqualTo("user"));
			Assert.That(proxy.Password, Is.EqualTo("password"));
		}

		[Test]
		public void LowercaseEnvironmentVariable()
		{
			Environment.SetEnvironmentVariable("http_proxy", "http://example.com");

			var proxy = new ProxyManager();

			Assert.That(proxy.Hostname, Is.EqualTo("example.com"));
			Assert.That(proxy.Port, Is.EqualTo(80));
			Assert.That(proxy.Username, Is.Null);
			Assert.That(proxy.Password, Is.Null);
		}

		[Test]
		public void UppercaseEnvironmentVariable()
		{
			Environment.SetEnvironmentVariable("HTTP_PROXY", "http://example.com");

			var proxy = new ProxyManager();

			Assert.That(proxy.Hostname, Is.EqualTo("example.com"));
			Assert.That(proxy.Port, Is.EqualTo(80));
			Assert.That(proxy.Username, Is.Null);
			Assert.That(proxy.Password, Is.Null);
		}

		[Test]
		[Platform(Exclude="Win", Reason="Environment variables on Windows are case-insensitive")]
		public void BothEnvironmentVariables_UsesLowercaseVariable()
		{
			Environment.SetEnvironmentVariable("http_proxy", "http://example1.com");
			Environment.SetEnvironmentVariable("HTTP_PROXY", "http://example2.com");

			var proxy = new ProxyManager();

			Assert.That(proxy.Hostname, Is.EqualTo("example1.com"));
			Assert.That(proxy.Port, Is.EqualTo(80));
			Assert.That(proxy.Username, Is.Null);
			Assert.That(proxy.Password, Is.Null);
		}
	}
}
