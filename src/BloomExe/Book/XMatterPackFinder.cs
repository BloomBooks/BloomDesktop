using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using SIL.Extensions;

namespace Bloom.Book
{
	/// <summary>
	/// Locate and list all the XMatter (Front & Back Matter) Packs that this user could use
	/// </summary>
	public class XMatterPackFinder
	{
		private readonly IEnumerable<string> _foldersPotentiallyHoldingPack;
		private List<XMatterInfo> _factoryXMatters;
		private List<XMatterInfo> _otherXMatters;
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

		/// <summary>
		/// Returns the 'Factory' xMatter locations, folders that ship with Bloom.
		/// Currently this is just the children of the first of _foldersPotentiallyHoldingPack.
		/// We may need more control one day, but I think it's unlikely enough to apply Yagni.
		/// </summary>
		public IEnumerable<XMatterInfo> Factory
		{
			get
			{
				lock (this)
				{
					if (_factoryXMatters == null)
						FindAll();
					return _factoryXMatters;
				}
			}
		}

		public IEnumerable<XMatterInfo> NonFactory
		{
			get
			{
				if (_otherXMatters == null)
					FindAll();
				return _otherXMatters;
			}
		}

		public XMatterInfo FactoryDefault
		{
			get { return All.FirstOrDefault(x => x.Key == "Factory"); }

		}

		public void FindAll()
		{
			Debug.Assert(_all==null);
			_all = new List<XMatterInfo>();
			_factoryXMatters = new List<XMatterInfo>();
			_otherXMatters = new List<XMatterInfo>();

			bool factory = true; // We consider the first path to be the factory ones for now.
			foreach (var path in _foldersPotentiallyHoldingPack)
			{
				if (!Directory.Exists(path))
					continue; // XMatter in CommonData may not exist.
				foreach (var directory in Directory.GetDirectories(path, "*-XMatter", SearchOption.AllDirectories))
				{
					AddXMatterDir(directory, factory);
				}

				foreach (var shortcut in Directory.GetFiles(path, "*.lnk", SearchOption.TopDirectoryOnly))
				{
					var p = ResolveShortcut.Resolve(shortcut);
					if (Directory.Exists(p))
						AddXMatterDir(p, factory);
				}
				factory = false;
			}
		}

		private void AddXMatterDir(string directory, bool factory)
		{
			var xMatterInfo = new XMatterInfo(directory);
			_all.Add(xMatterInfo);
			if (factory)
				_factoryXMatters.Add(xMatterInfo);
			else
				_otherXMatters.Add(xMatterInfo);
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
