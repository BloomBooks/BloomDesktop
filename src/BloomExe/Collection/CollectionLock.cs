using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bloom.ToPalaso;
using SIL.Code;
using SIL.IO;

namespace Bloom.Collection
{
	/// <summary>
	/// Prevent the collection folder from being moved or renamed while we're running. We can't actually lock a folder,
	/// so we do this by locking the .bloomCollectionFile. We do this in a way that allows others (and our own code)
	/// to read and write to it, just not delete/rename/move it.  BL-11484
	/// Some operations that don't look like moving it (e.g., unzipping a new version of it)
	/// fail unexpectedly, so we provide a way to unlock temporarily for such operations.
	/// </summary>
	public class CollectionLock
	{
		private string _filePath;
		private FileStream _streamToLockCollectionFile;

		// For unit testing we may not need this locking, so can use a dummy lock.
		public CollectionLock() {}
		public CollectionLock(string filePath)
		{
			_filePath = filePath;
		}

		public void Lock()
		{
			if (_filePath == null) return;
			// Prevent the collection folder from being moved or renamed while we're running. We can't actually lock a folder,
			// so we do this by locking the .bloomCollectionFile. We do this in a way that allows others (and our own code)
			// to read and write to it, just not delete/rename/move it.  BL-11484
			try
			{
				_streamToLockCollectionFile = RobustFile.Open(_filePath, FileMode.Open, FileAccess.Read,
						FileShare.ReadWrite);
			}
			catch (Exception err)
			{
#if DEBUG
				throw err;
#endif
				// Swallow because this locking is totally optional and so not worth crashing over if for
				// some reason something else also has it open.
			}
		}

		public void Unlock()
		{
			if (_filePath == null) return;
			try
			{
				_streamToLockCollectionFile.Close();
			}
			catch (Exception err)
			{
#if DEBUG
				throw err;
#endif
				//swallow
			}
			_streamToLockCollectionFile = null;
		}

		// Unlock just while performing the specified task.
		public void UnlockFor(Action task)
		{
			if (_streamToLockCollectionFile != null)
			{
				Unlock();
				try
				{
					task();
				}
				finally
				{
					Lock();
				}
			}
			else
			{
				task();
			}
		}
	}
}
