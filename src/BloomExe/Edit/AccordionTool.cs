using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SIL.Extensions;

namespace Bloom.Edit
{
	/// <summary>
	/// This class represents one tool in the Toolbox accordion which can show to the right of the
	/// page when the user expands it. There is a subclass for each tool.
	/// These objects are serialized as part of the meta.json file representing the state of a book.
	/// The State field is persisted in this way; it is also passed in to the JavaScript that manages
	/// the accordion. New fields and properties should be kept non-public or marked with an
	/// appropriate attribute if they should NOT be persisted in JSON.
	/// New subclasses will typically require a new case in WithName and also in AccordionToolConverter.ReadJson.
	/// Note that the values of the Name field are used in the json and therefore cannot readily be changed.
	/// (Migration would handle a change going forward, but older Blooms would lose the data at best.)
	/// </summary>
	public abstract class AccordionTool
	{
		[JsonProperty("name")]
		public abstract string Name { get; }

		[JsonProperty("enabled")]
		public bool Enabled { get; set; }

		/// <summary>
		/// Different tools may use this arbitrarily. Currently decodable and leveled readers use it to store
		/// the stage or level a book belongs to (at least the one last active when editing it).
		/// </summary>
		[JsonProperty("state")]
		public string State { get; set; }

		public static AccordionTool WithName(string name)
		{
			switch (name)
			{
				case DecodableReaderTool.DRName: return new DecodableReaderTool();
				case LeveledReaderTool.LRName: return new LeveledReaderTool();
				case TalkingBookTool.TBName: return new TalkingBookTool();
			}
			throw new ArgumentException("Unexpected tool name");
		}

		/// <summary>
		/// The name used to identify this tool's state in RetrieveToolSettings (the state passed to the
		/// tool's initializer).
		/// </summary>
		internal string StateName
		{
			get { return Name + "State"; }
		}

		// May be overridden to save some information about the tool state during page save.
		// Default does nothing.
		internal virtual void SaveSettings(EditingView _view)
		{ }

		// May be overridden to restore the tool state during page initialization.
		// This is run after the page is otherwise idle.
		internal virtual void RestoreSettings(EditingView _view)
		{ }
	}

	/// <summary>
	/// This gives us something to return if we encounter an unknown tool name when deserializing.
	/// </summary>
	public class UnknownTool : AccordionTool
	{
		public override string Name { get { return "unknownTool"; } }
	}

	/// <summary>
	/// This class is used as the ItemConverterType for the Tools property of BookMetaData.
	/// It allows us to deserialize a sequence of polymorphic tools, creating the
	/// right subclass for each based on the name.
	/// </summary>
	public class AccordionToolConverter : JsonConverter
	{
		public override bool CanConvert(Type objectType)
		{
			return typeof(AccordionTool).IsAssignableFrom(objectType);
		}

		// Default writing is fine.
		public override bool CanWrite
		{ get { return false; } }

		public override object ReadJson(JsonReader reader,
			Type objectType, object existingValue, JsonSerializer serializer)
		{
			JObject item = JObject.Load(reader);
			switch ((string)item["name"])
			{
				case DecodableReaderTool.DRName:
					return item.ToObject<DecodableReaderTool>();
				case LeveledReaderTool.LRName:
					return item.ToObject<LeveledReaderTool>();
				case TalkingBookTool.TBName:
					return item.ToObject<TalkingBookTool>();
			}
			// At this point we are either encountering a meta.json that has been modified by hand,
			// or more likely one from a more recent Bloom that has an additional tool.
			// We will ignore the unknown tool (see BookMetaData.FromString()). Here in the
			// deserialize process, however, we have to return something.
			// Enhance: in theory, we could keep at least the tool's state and enabled status
			// in case this book moves back to a version of Bloom that has the tool. But we'd
			// have to carefully ignore Unknown tools in many places. Hopefully YAGNI.
			return new UnknownTool();
		}

		// We don't need a real implementation of this because returning false from CanWrite
		// tells the converter to use the default write code.
		public override void WriteJson(JsonWriter writer,
			object value, JsonSerializer serializer)
		{
			throw new NotImplementedException();
		}
	}
}
