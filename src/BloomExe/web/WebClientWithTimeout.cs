// Copyright (c) 2014 SIL International
// This software is licensed under the MIT License (http://opensource.org/licenses/MIT)
using System;
using System.Net;

namespace Bloom.web
{
	/// <summary>
	/// the base class waits for 30 seconds, which is too long for local thing like we are doing
	/// </summary>
	public class WebClientWithTimeout : WebClient
	{
		private int _timeout;
		/// <summary>
		/// Time in milliseconds
		/// </summary>
		public int Timeout
		{
			get
			{
				return _timeout;
			}
			set
			{
				_timeout = value;
			}
		}

		public WebClientWithTimeout()
		{
			this._timeout = 60000;
		}

		public WebClientWithTimeout(int timeout)
		{
			this._timeout = timeout;
		}

		protected override WebRequest GetWebRequest(Uri address)
		{
			var result = base.GetWebRequest(address);
			result.Timeout = this._timeout;
			return result;
		}
	}
}
