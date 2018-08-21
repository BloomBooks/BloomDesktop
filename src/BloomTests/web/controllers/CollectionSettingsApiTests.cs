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
		[TestCase("USAID-US-3098-2075", 2017,12,29)]
		[TestCase("SIL-LEAD-361769-2476", 3000,1, 1)]
		[TestCase("PNG-RISE-3831-2090", 2020, 1, 1)]
		public void GetExpirationDate_Valid_ReturnsCorrectDate(string input, int year, int month, int day)
		{
			var result = CollectionSettingsApi.GetExpirationDate(input);
			Assert.That(result.Year, Is.EqualTo(year));
			Assert.That(result.Month, Is.EqualTo(month));
			Assert.That(result.Day, Is.EqualTo(day));
		}

		[TestCase("")] // empty
		[TestCase(null)]
		[TestCase("SIL748410821")] // no dashes
		[TestCase("SIL-74847709")] // too few dashes
		[TestCase("SIL-7484-7709-nonsense")] // extra at end
		[TestCase("SIL-7484-10822")] // wrong checksum
		[TestCase("SIL-silly-1234")] // not a number in part 2
		[TestCase("SIL-7484-silly")] // not a number in part 3
		public void GetExpirationDate_InValid_ReturnsMinDate(string input)
		{
			var result = CollectionSettingsApi.GetExpirationDate(input);
			Assert.That(result, Is.EqualTo(DateTime.MinValue));
		}

	}
}
