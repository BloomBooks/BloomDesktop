using Bloom.Collection;

namespace Bloom.Edit
{
	public class ReaderToolsModel
	{
		public delegate ReaderToolsView Factory();//autofac uses this

		private CollectionSettings _collectionSettings;

		public ReaderToolsModel(CollectionSettings settings)
		{
			_collectionSettings = settings;
			// Enhance JohnT: eventually we probably persist somewhere what stage or level they are at?

		}

		/// <summary>
		/// The view for which we are the model.
		/// </summary>
		internal IReaderToolsView View { get; set; }

		/// <summary>
		/// Set the InnerHtml of the element identified by the ID. It must exist.
		/// Overridden in test stubs.
		/// </summary>
		/// <param name="id"></param>
		/// <param name="val"></param>
		internal virtual void UpdateElementContent(string id, string val)
		{
			//View.Browser.DomDocument.GetElementById(id).InnerHtml = val;
		}

		/// <summary>
		/// Set the specified attribute of the specified object to the value.
		/// Overridden in test stubs.
		/// </summary>
		/// <param name="elementId"></param>
		/// <param name="attrName"></param>
		/// <param name="val"></param>
		internal virtual void UpdateElementAttribute(string elementId, string attrName, string val)
		{
			var elt = View.Browser.DomDocument.GetElementById(elementId);
			elt.SetAttribute(attrName, val);
		}

		/// <summary>
		/// Since this is being used on shipping HTML, we should not be asking for elements that don't exist.
		/// So it is deliberately allowed to crash if that happens.
		/// A missing attribute is allowed and will just return an empty string.
		/// Overridden in test stubs.
		/// </summary>
		/// <param name="elementId"></param>
		/// <param name="attrName"></param>
		/// <returns></returns>
		internal virtual string GetElementAttribute(string elementId, string attrName)
		{
			var elt = View.Browser.DomDocument.GetElementById(elementId);
			var attr = elt.Attributes[attrName];
			if (attr == null)
				return "";
			return attr.NodeValue ?? "";
		}

		/// <summary>
		/// Get the (HTML) content of the specified element. It must exist.
		/// </summary>
		/// <param name="id"></param>
		/// <returns></returns>
		internal virtual string GetElementContent(string id)
		{
			return ""; //View.Browser.DomDocument.GetElementById(id).InnerHtml;
		}

		/// <summary>
		/// Invoked when the user clicks on an element in the view with the specified id.
		/// (note: many ids are assigned for other reasons and have no click behavior.)
		/// </summary>
		/// <param name="id"></param>
		internal void ControlClicked(string id)
		{
			switch (id)
			{

				//case "setUpStages":
				//    SetUpStages();
				//    break;
			}
		}
	}
}
