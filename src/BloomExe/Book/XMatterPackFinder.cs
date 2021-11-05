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
		private List<XMatterInfo> _customInstalledXMatters;
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
		public   IEnumerable<XMatterInfo> ToOfferInSettings
		{
			get
			{
				if (_factoryXMatters == null || _customInstalledXMatters == null)
					FindAll();
				return _factoryXMatters.Where(p=>!p.PathToFolder.Contains("project-specific")).Concat(_customInstalledXMatters);
			}
		}

		/// <summary>
		/// Returns the 'Factory' xMatter locations, folders that ship with Bloom.
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

		public IEnumerable<XMatterInfo> CustomInstalled
		{
			get
			{
				if (_customInstalledXMatters == null)
					FindAll();
				return _customInstalledXMatters;
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
			_customInstalledXMatters = new List<XMatterInfo>();

			bool factory = true; // We consider the first path to be the factory ones for now.
			foreach (var path in _foldersPotentiallyHoldingPack)
			{
				if (!Directory.Exists(path))
					continue; // XMatter in CommonData may not exist.

				foreach (var directory in Directory.GetDirectories(path, "*-XMatter", SearchOption.AllDirectories))
				{
					_factoryXMatters.Add(AddXMatterDir(directory));
				}

				foreach (var shortcut in Directory.GetFiles(path, "*.lnk", SearchOption.TopDirectoryOnly))
				{
					var p = ResolveShortcut.Resolve(shortcut);
					if (Directory.Exists(p))
						_customInstalledXMatters.Add(AddXMatterDir(p));
				}
				

				factory = false;
			}
		}

		private XMatterInfo AddXMatterDir(string directory)
		{
			var xMatterInfo = new XMatterInfo(directory);
			_all.Add(xMatterInfo);
			return xMatterInfo;
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
