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
		[TestCase("Acme-003506-0487", 2019,2,10)]
		[TestCase("Quite-Phony-003098-4247", 2017,12,29)]
		[TestCase("SOME-FAKE-361769-3038", 3000,1, 1)]
		[TestCase("Somevery long fake thing-361769-9523", 3000,1,1)]
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
		[TestCase("Quite-Phony-3098-4247")] // Too few digits in part 2
		[TestCase("Acme-003506-487")] // Too few digits in part 3
		[TestCase("Somevery long fake thing-361769-19523")] // Too many digits in part 3
		public void GetExpirationDate_InValid_ReturnsMinDate(string input)
		{
			var result = CollectionSettingsApi.GetExpirationDate(input);
			Assert.That(result, Is.EqualTo(DateTime.MinValue));
		}

		[TestCase("", true)]
		[TestCase(null, true)]
		[TestCase("Acme3506487", true)] // no dashes
		[TestCase("Acme-3506487", true)] // too few dashes
		[TestCase("Acme-3506-487-nonsense", false)] // clearly wrong here
		[TestCase("Acme-003506-0488", false)] // wrong checksum
		[TestCase("Acme-silly-1234", true)] // not a number in part 2... but could be start of Acme-silly-123456-7890
		[TestCase("Acme-7484-silly", false)] // not a number in part 3...debatable, COULD be start of Acme-7484-silly-123456-7890
		[TestCase("Acme-7484-si", false)] // short not a number in part 3...debatable, COULD be start of Acme-7484-si-123456-7890
		[TestCase("Quite-Phony-3098-4247", false)] // Too few digits in part 2
		[TestCase("Acme-003506-487", true)] // Too few digits in part 3
		[TestCase("Somevery long fake thing-361769-19523", false)] // Too many digits in part 3
		[TestCase("Acme-3506-487", false)] // Last two parts are numbers, but first is too short
		[TestCase("Acme-003506-", true)] // No digits in part 3 is OK, even though part 3 won't parse
		public void SubscriptionAppearsIncomplete(string input, bool incomplete)
		{
			var result = CollectionSettingsApi.SubscriptionCodeLooksIncomplete(input);
			Assert.That(result, Is.EqualTo(incomplete));
		}
	}
}
