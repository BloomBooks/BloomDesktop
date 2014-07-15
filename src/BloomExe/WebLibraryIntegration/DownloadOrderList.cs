using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bloom.WebLibraryIntegration
{
	/// <summary>
	/// This class exists (as a singleton, managed by Autofac) to manage requests for books to be downloaded.
	/// </summary>
	public class DownloadOrderList
	{
		public event EventHandler OrderAdded;
		private List<string> _orders = new List<string>();

		public void AddOrder(string order)
		{
			_orders.Add(order);
			if (OrderAdded != null)
				OrderAdded(this, new EventArgs());
		}

		public string GetOrder()
		{
			if (_orders.Count == 0)
				return null;
			var result = _orders[0];
			_orders.RemoveAt(0);
			return result;
		}
	}
}
