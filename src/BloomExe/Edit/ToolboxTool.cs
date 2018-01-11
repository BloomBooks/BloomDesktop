using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Bloom.Book;
using Bloom.Collection;
using BloomTemp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SIL.Extensions;
using SIL.IO;

namespace Bloom.Edit
{
	/// <summary>
	/// This class represents one tool in the Toolbox accordion which can show to the right of the
	/// page when the user expands it. There is a subclass for each tool.
	/// These objects are serialized as part of the meta.json file representing the state of a book.
	/// The State field is persisted in this way; it is also passed in to the JavaScript that manages
	/// the toolbox. New fields and properties should be kept non-public or marked with an
	/// appropriate attribute if they should NOT be persisted in JSON.
	/// New subclasses will typically require a new case in CreateFromToolId and also in GetToolboxToolFromJsonObject.
	/// Note that the values of the Name field are used in the json and therefore cannot readily be changed.
	/// (Migration would handle a change going forward, but older Blooms would lose the data at best.)
	/// </summary>
	public abstract class ToolboxTool
	{
		/// <summary>
		/// This is the id used to identify the tool in the meta.json file that accompanies the book.
		/// These files are included in the books in BloomLibrary, which means that it is likely both
		/// that current Bloom versions will see old meta.json, and more dangerously, older Bloom
		/// versions will see new meta.json as people publish books using a newer version of the tool.
		/// Older versions cope with unknown names, but they will not display the correct tool if they
		/// see an unrecognized name for a tool they know about; therefore, the actual names used for
		/// existing tools should be changed only with care and for very good reason.
		/// </summary>
		[JsonProperty("name")]
		public abstract string ToolId { get; }

		[JsonProperty("enabled")]
		public bool Enabled { get; set; }

		/// <summary>
		/// Override in tools that don't have an Enabled checkbox but are always enabled.
		/// </summary>
		[JsonIgnore]
		public virtual bool AlwaysEnabled
		{
			get { return false; }
		}

		/// <summary>
		/// Different tools may use this arbitrarily. Currently decodable and leveled readers use it to store
		/// the stage or level a book belongs to (at least the one last active when editing it).
		/// </summary>
		[JsonProperty("state")]
		public string State { get; set; }

		public virtual void SaveDefaultState()
		{
		}

		public virtual string DefaultState()
		{
			return null;
		}

		public static ToolboxTool CreateFromToolId(string toolId)
		{
			switch (toolId)
			{
				case DecodableReaderTool.StaticToolId: return new DecodableReaderTool();
				case LeveledReaderTool.StaticToolId: return new LeveledReaderTool();
				case TalkingBookTool.StaticToolId: return new TalkingBookTool();
				case BookSettingsTool.StaticToolId: return new BookSettingsTool();
				case PanAndZoomTool.StaticToolId: return new PanAndZoomTool();
			}
			throw new ArgumentException("Unexpected tool name "+toolId);
		}

		/// <summary>
		/// The name used to identify this tool's state in RetrieveToolSettings (the state passed to the
		/// tool's initializer).
		/// </summary>
		internal string StateName
		{
			get { return ToolId + "State"; }
		}

		// May be overridden to save some information about the tool state during page save.
		// Default does nothing.
		internal virtual void SaveSettings(ElementProxy toolbox)
		{ }

		public static object GetToolboxToolFromJsonObject(JObject item)
		{
			switch ((string) item["name"])
			{
				//enhance: we don't really want to "register" our panels in several places like this
				case DecodableReaderTool.StaticToolId:
					return item.ToObject<DecodableReaderTool>();
				case LeveledReaderTool.StaticToolId:
					return item.ToObject<LeveledReaderTool>();
				case TalkingBookTool.StaticToolId:
					return item.ToObject<TalkingBookTool>();
				case BookSettingsTool.StaticToolId:
					return item.ToObject<BookSettingsTool>();
				case PanAndZoomTool.StaticToolId:
					return item.ToObject<PanAndZoomTool>();
				default: // this version doesn't know about that tool
					return new UnknownTool();
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
	}

	/// <summary>
	/// This gives us something to return if we encounter an unknown tool name when deserializing.
	/// </summary>
	public class UnknownTool : ToolboxTool
	{
		public override string ToolId { get { return "unknownTool"; } }
	}

	/// <summary>
	/// This class is used as the ItemConverterType for the Tools property of BookMetaData.
	/// It allows us to deserialize a sequence of polymorphic tools, creating the
	/// right subclass for each based on the name.
	/// </summary>
	public class ToolboxToolConverter : JsonConverter
	{
		public override bool CanConvert(Type objectType)
		{
			return typeof(ToolboxTool).IsAssignableFrom(objectType);
		}

		// Default writing is fine.
		public override bool CanWrite
		{ get { return false; } }


		//implement JsonConverter.ReadJson
		public override object ReadJson(JsonReader reader,
			Type objectType, object existingValue, JsonSerializer serializer)
		{
			JObject item = JObject.Load(reader);
			return ToolboxTool.GetToolboxToolFromJsonObject(item);
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
