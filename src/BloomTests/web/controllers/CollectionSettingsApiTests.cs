using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.web.controllers;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
	public class CollectionSettingsApiTests
	{
		[TestCase("Acme-3506-487", 2019,2,10)]
		[TestCase("Quite-Phony-3098-4247", 2017,12,29)]
		[TestCase("SOME-FAKE-361769-3038", 3000,1, 1)]
		public void GetExpirationDate_Valid_ReturnsCorrectDate(string input, int year, int month, int day)
		{
			var result = CollectionSettingsApi.GetExpirationDate(input);
			Assert.That(result.Year, Is.EqualTo(year));
			Assert.That(result.Month, Is.EqualTo(month));
			Assert.That(result.Day, Is.EqualTo(day));
		}

		[TestCase("")] // empty
		[TestCase(null)]
		[TestCase("Acme3506487")] // no dashes
		[TestCase("Acme-3506487")] // too few dashes
		[TestCase("Acme-3506-487-nonsense")] // extra at end
		[TestCase("Acme-3506-488")] // wrong checksum
		[TestCase("Acme-silly-1234")] // not a number in part 2
		[TestCase("Acme-7484-silly")] // not a number in part 3
		public void GetExpirationDate_InValid_ReturnsMinDate(string input)
		{
			var result = CollectionSettingsApi.GetExpirationDate(input);
			Assert.That(result, Is.EqualTo(DateTime.MinValue));
		}

	}
}
