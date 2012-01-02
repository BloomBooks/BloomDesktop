using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Palaso.Extensions;

namespace Bloom.Book
{
	/// <summary>
	/// Locate and list all the XMatter (Front & Back Matter) Packs that this user could use
	/// </summary>
	public class XMatterPackFinder
	{
		private readonly IEnumerable<string> _foldersPotentiallyHoldingPack;
		private List<XMatterInfo> _all;
		public XMatterPackFinder(IEnumerable<string> foldersPotentiallyHoldingPack)
		{
			_foldersPotentiallyHoldingPack = foldersPotentiallyHoldingPack;
		}

		public   IEnumerable<XMatterInfo> All
		{
			get
			{
				if (_all != null)
					return _all;
				FindAll();
				return _all;
			}
		}

		public object FactoryDefault
		{
			get { return All.FirstOrDefault(x => x.Key == "Factory"); }

		}

		public void FindAll()
		{
			Debug.Assert(_all==null);
			_all = new List<XMatterInfo>();

			foreach (var path in _foldersPotentiallyHoldingPack)
			{
				foreach (var directory in Directory.GetDirectories(path, "*-XMatter", SearchOption.AllDirectories))
				{
					_all.Add(new XMatterInfo(directory));
				}
			}
		}

		/// <summary>
		/// E.g. in "Factory-XMatter", the key is "Factory".
		/// </summary>
		public XMatterInfo FindByKey(string xMatterPackKey)
		{
			return All.FirstOrDefault(x => x.Key == xMatterPackKey);
		}
	}
}
