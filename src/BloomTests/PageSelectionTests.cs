using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Bloom;
using Bloom.Book;
using Bloom.Edit;
using NUnit.Framework;

namespace BloomTests
{
	[TestFixture]
	public class PageSelectionTests
	{
		[Test]
		public void CurrentSelection_ADifferentBookIsSelected_GoesToFirstPage()
		{

		}

		[Test]
		public void CurrentSelection_AfterSelectedPageIsDeleted_GoesToNextPage()
		{

		}
		[Test]
		public void CurrentSelection_AfterLastAndSelectedPageIsDeleted_GoesToNewLastPage()
		{

		}

		[Test]
		public void CurrentSelection_AfterPageIsInserted_GoesToNewPage()
		{
			var bs = new BookSelection();
			var ps = new PageSelection();//bs);
		 //   ps.
		}
	}
}
