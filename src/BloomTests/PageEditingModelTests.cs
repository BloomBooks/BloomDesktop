using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using Bloom.Edit;
using NUnit.Framework;
using Palaso.TestUtilities;

namespace BloomTests
{
	public class PageEditingModelTests
	{
		[Test]
		public void ChangePicture_PictureIsFromOutsideProject_PictureCopiedAndAttributeChanged()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><div/><div><img id='one'/><img id='two' src='old.png'/></div></body></html>");
			var model = new PageEditingModel();
			using (var src = new TemporaryFolder("bloom pictures test source"))
			using (var dest = new TemporaryFolder("bloom picture tests dest"))
			{
				var newImagePath = src.Combine("new.png");
				File.WriteAllText(newImagePath,string.Empty);
				model.ChangePicture(dest.Path, dom, "two", newImagePath);
				Assert.IsTrue(File.Exists(dest.Combine("new.png")));
				AssertThatXmlIn.Dom(dom).HasSpecifiedNumberOfMatchesForXpath(@"//img[@id='two' and @src='new.png']", 1);
		  }

		}

		/// <summary>
		/// With this, we test the secenario where someone grabs, say "untitled.png", then does
		/// so again in a different place. At this time, we will just throw away the first one
		/// and use the new one, in both places in document. Alternatively, we could take the
		/// trouble to rename the second one to a safe name so that there are two files.
		/// </summary>
		[Test]
		public void ChangePicture_AlreadyHaveACopyInPublicationFolder_PictureUpdated()
		{
			var dom = new XmlDocument();
			dom.LoadXml("<html><body><div/><div><img id='one'/><img id='two' src='old.png'/></div></body></html>");
			var model = new PageEditingModel();
			using (var src = new TemporaryFolder("bloom pictures test source"))
			using (var dest = new TemporaryFolder("bloom picture tests dest"))
			{
				var dogImagePath = src.Combine("dog.png");
				File.WriteAllText(dogImagePath, "new dog");
				var destDogImagePath = dest.Combine("dog.png");
				File.WriteAllText(destDogImagePath, "old dog");
				model.ChangePicture(dest.Path, dom, "two", dogImagePath);
				Assert.AreEqual("new dog", File.ReadAllText(destDogImagePath));
			}
		}

	}
}
