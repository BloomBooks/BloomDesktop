#if !__MonoCS__
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Bloom.Book;
using Bloom.Publish.BloomPub;
using Bloom.Publish.BloomPub.usb;
using Bloom.web;

namespace BloomTests.Publish
{
	public class MockUsbPublisher : UsbPublisher
	{
		private int _exceptionToThrow;

		public MockUsbPublisher(WebSocketProgress progress, BookServer bookServer)
			:base(progress, bookServer)
		{
			Stopped = () => SetState("dummy");
			_exceptionToThrow = HR_ERROR_DISK_FULL;
		}

		protected override void SendBookDoWork(Bloom.Book.Book book, Color backColor, BloomPubPublishSettings settings)
		{
			throw new COMException("MockUsbPublisher threw a fake COMException in SendBookDoWork.", _exceptionToThrow);
		}

		private void SetState(string dummy)
		{
			// do nothing.
		}

		/// <summary>
		/// Tell MockUsbPublisher which exception to throw in SendBookDoWork()
		/// </summary>
		/// <param name="exceptionChoice">ExceptionToThrow enum type</param>
		public void SetExceptionToThrow(ExceptionToThrow exceptionChoice)
		{
			switch (exceptionChoice)
			{
				case ExceptionToThrow.DeviceFull:
					_exceptionToThrow = HR_ERROR_DISK_FULL;
					break;
				case ExceptionToThrow.DeviceHung:
					_exceptionToThrow = HR_E_WPD_DEVICE_IS_HUNG;
					break;
				case ExceptionToThrow.HandleDeviceFull:
					_exceptionToThrow = HR_ERROR_HANDLE_DISK_FULL;
					break;
				default:
					throw new ApplicationException("Unknown Exception type chosen");
			}
		}

		public void SetLastBloomdFileSize(string filePath)
		{
			_lastPublishedBloomdSize = GetSizeOfBloomdFile(filePath);
		}

		public string GetStoredBloomdFileSize()
		{
			return _lastPublishedBloomdSize;
		}

		public enum ExceptionToThrow
		{
			DeviceFull,
			DeviceHung,
			HandleDeviceFull
		}
	}
}
#endif
