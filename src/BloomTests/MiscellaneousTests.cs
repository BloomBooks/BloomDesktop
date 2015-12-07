using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.Edit;
using NUnit.Framework;
using SIL.Media.Naudio;

namespace BloomTests
{
	[TestFixture]
	public class MiscellaneousTests
	{
		/// <summary>
		/// BL-2974. We don't directly use the reference to NAudio in Bloom.exe, it's just there to make sure the DLL gets copied.
		/// But the build won't actually fail if it's not there to be copied. Creating this object will fail if it isn't.
		/// The prevents us from shipping a build if we somehow mess up the TeamCity configuration so the dependencies don't
		/// load a useable NAudio.dll.
		/// </summary>
		[Test]
		public void NAudioIsInstalled()
		{
			Assert.DoesNotThrow(() => new AudioRecorder(1));
		}
	}
}
