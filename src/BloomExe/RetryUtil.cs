using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Bloom
{
	public static class RetryUtil
	{
		private const int kDefaultMaxRetryAttempts = 20;
		private const int kDefaultRetryDelay = 100;
		private static readonly ISet<Type> kDefaultExceptionTypesToRetry = new HashSet<Type> { Type.GetType("System.IO.IOException") };

		public static void Retry(Action action, int maxRetryAttempts = kDefaultMaxRetryAttempts, int retryDelay = kDefaultRetryDelay, ISet<Type> exceptionTypesToRetry = null)
		{
			Retry<object>(() =>
			{
				action();
				return null;
			}, maxRetryAttempts, retryDelay, exceptionTypesToRetry);
		}

		public static T Retry<T>(Func<T> action, int maxRetryAttempts = kDefaultMaxRetryAttempts, int retryDelay = kDefaultRetryDelay, ISet<Type> exceptionTypesToRetry = null)
		{
			if (exceptionTypesToRetry == null)
				exceptionTypesToRetry = kDefaultExceptionTypesToRetry;

			for (int attempt = 1; attempt <= maxRetryAttempts; attempt++)
			{
				try
				{
					var result = action();
					Debug.WriteLine("Successful after {0} attempts", attempt);
					return result;
				}
				catch (Exception e)
				{
					if (exceptionTypesToRetry.Contains(e.GetType()))
					{
						if (attempt == maxRetryAttempts)
						{
							Debug.WriteLine("Failed after {0} attempts", attempt);
							throw;
						}
						Thread.Sleep(retryDelay);
					}
				}
			}
			return default(T);
		}
	}
}
