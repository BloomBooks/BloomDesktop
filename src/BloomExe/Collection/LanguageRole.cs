using System.Linq;
using System.Xml.Linq;

namespace Bloom.Collection
{
	public class LanguageRole
	{
		public string Id;
		public string Language;
		public string Name;

		// TODO: This is probably no good, because we'll need to localize the name of the Role. But it's a start.
		// In fact, we probably want different names for some of these roles than how they have been referred to in the past.
		// Probably the sign language designation in the collection should just be a role that points into the Languages
		// collection.
		private readonly string[,] _knownRoles =
		{
			{ "content1", "First Language on Page" },
			{ "content2", "Second Language on Page" },
			{ "content3", "Third Language on Page" },
			{ "contentNational1", "National Language" },
			{ "contentNational2", "Regional Language" },
			{ "meta1", "Title, Credits, Back Cover, etc." }
		};

		private readonly int NumberOfRoles;

		public LanguageRole(string id, string isoCode, string name="")
		{
			Id = id;
			Language = isoCode;
			NumberOfRoles = _knownRoles.Length;
			Name = GetRoleName(id, name);
		}

		private string GetRoleName(string id, string name)
		{
			if (!string.IsNullOrEmpty(name))
				return name;
			for (var i = 0; i < NumberOfRoles; i++)
			{
				if (id == _knownRoles[i, 0])
				{
					return _knownRoles[i, 1];
				}
			}

			return string.Empty;
		}

		public void SaveAsLanguageRoleXElement(XElement xml)
		{
			xml.Add(new XElement("LanguageRole",
				new XAttribute("id", Id),
				new XAttribute("language", Language),
				new XAttribute("name", Name))
			);
		}

		/// <summary>
		/// Assumes we are feeding it just the LanguageRole element.
		/// </summary>
		public void ReadFromXml(XElement xml, bool defaultToContent1IfMissing)
		{
			const string elementName = "LanguageRole";

			var roleElement = xml.Descendants(elementName).First();
			Id = CollectionSettings.ReadAttribute(roleElement, "id", "unknownRole");
			Language = CollectionSettings.ReadAttribute(roleElement, "language", "");
			Name = CollectionSettings.ReadAttribute(roleElement, "name", "Unknown Role");
		}
	}
}
