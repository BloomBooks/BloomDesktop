using System;
using System.Drawing;
using Bloom.Book;
using Bloom.Publish.Android.usb;
using Bloom.web;

namespace BloomTests.Publish
{
	class MockUsbPublisher : UsbPublisher
	{
		public MockUsbPublisher(WebSocketProgress progress, BookServer bookServer)
			:base(progress, bookServer)
		{
			Stopped = () => SetState("dummy");
		}

		internal override void SendBookDoWork(Bloom.Book.Book book, Color backColor)
		{
			throw new OutOfMemoryException("MockUsbPublisher threw a fake OutOfMemoryException in SendBookDoWork");
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
