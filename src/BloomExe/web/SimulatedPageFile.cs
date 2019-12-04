using System;

namespace Bloom.Api
{
	/// <summary>
	/// A SimulatedPageFile is used in connection with simulating a current-page file that needs
	/// to (seem to) be in the book folder so local hrefs work. We don't actually put files there
	/// (see BloomServer.MakeSimulatedPageFileInBookFolder for more), but rather
	/// store some data in the our file server object.
	/// The particular purpose of the SimulatedPageFile is to manage the lifetime for which
	/// the simulated page is kept in the server. It can be passed to the Browser which will
	/// Dispose() it when no longer needed.
	/// (In that regard, it is used in rather the same way as a TempFile object is used to
	/// make sure that a temp file gets deleted when the Browser no longer needs it.)
	/// </summary>
	public class SimulatedPageFile : IDisposable
	{
		public void Dispose()
		{
			BloomServer.RemoveSimulatedPageFile(Key);
		}

		/// <summary>
		/// The key under which the server stores the data that should be discarded
		/// when this object gets disposed.
		/// </summary>
		public string Key { get; set; }
	}
}
