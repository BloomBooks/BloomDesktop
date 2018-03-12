#if !__MonoCS__
using System;
using System.Drawing;
using System.IO;
using Bloom.Book;
using Bloom.Publish.Android.usb;
using Bloom.web;

namespace BloomTests.Publish
{
	class MockUsbPublisher : UsbPublisher
	{
		const int HR_ERROR_DISK_FULL = unchecked((int)0x80070070);

		public MockUsbPublisher(WebSocketProgress progress, BookServer bookServer)
			:base(progress, bookServer)
		{
			Stopped = () => SetState("dummy");
		}

		internal override void SendBookDoWork(Bloom.Book.Book book, Color backColor)
		{
			throw new IOException("MockUsbPublisher threw a fake IOException (Disk is full) in SendBookDoWork", HR_ERROR_DISK_FULL);
		}

		private void SetState(string message)
		{
			// do nothing.
		}

		public void SetLastBloomdFilePath(string filePath)
		{
			_lastPublishedBloomdPath = filePath;
		}

		public string GetBloomdFileSize()
		{
			return GetSizeOfBloomdFile(_lastPublishedBloomdPath);
		}
	}
}
#endif
