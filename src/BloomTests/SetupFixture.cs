// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using NUnit.Framework;
using Gecko;
using Bloom;

namespace BloomTests
{
	/// <summary>
	/// The methods in this class run once before and after each test run, i.e. they get
	/// executed exactly once.
	/// </summary>
	[SetUpFixture]
	public class SetupFixture
	{
		[OneTimeSetUp]
		public void Setup()
		{
			Browser.SetUpXulRunner();
		}

		[OneTimeTearDown]
		public void TearDown()
		{
			Xpcom.Shutdown();
		}
	}
}
