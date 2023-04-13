using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

		string[] packsToSkip = new string[] { "null", "bigbook", "shrp", "sharp", "forunittest", "templatestarter" };
		public   IEnumerable<XMatterInfo> GetXMattersToOfferInSettings(string xmatterKeyForcedByBranding)
		{
		
				if (_factoryXMatters == null || _customInstalledXMatters == null)
					FindAll();
				if (string.IsNullOrEmpty(xmatterKeyForcedByBranding))
				{
					return _factoryXMatters.Where(p => !p.PathToFolder.Contains("project-specific"))
						.Concat(_customInstalledXMatters)
						.Where(pack => !packsToSkip.Any(s => pack.Key.ToLowerInvariant().Contains(s)));
				}
				else // this is a Bloom Enterprise Project with a Branding that selects the xmatter 
				{
					return _factoryXMatters.Where(p => p.Key == xmatterKeyForcedByBranding);
				}
		}

		public string GetValidXmatter(string xmatterKeyForcedByBranding, string proposedXmatter)
		{
			var possibleXmatters = GetXMattersToOfferInSettings(xmatterKeyForcedByBranding).ToArray();
			if (possibleXmatters.Any(x => x.Key == proposedXmatter))
				return proposedXmatter;
			if (possibleXmatters.Length == 1)
				return possibleXmatters[0].Key; // Only one is allowed for current branding
			return FactoryDefault.Key;
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
				_customInstalledXMatters.Add(xMatterInfo);
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
