using System;
using System.Xml;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bloom.Edit
{
    /// <summary>
    /// This class represents the state of one tool in the Toolbox accordion which can show to the right of the
    /// page when the user expands it.
    /// These objects are serialized as part of the meta.json file representing the state of a book.
    /// The State field is persisted in this way; it is also passed in to the JavaScript that manages
    /// the toolbox. New fields and properties should be kept non-public or marked with an
    /// appropriate attribute if they should NOT be persisted in JSON.
    /// Note that the values of the Name field are used in the json and therefore cannot readily be changed.
    /// (Migration would handle a change going forward, but older Blooms would lose the data at best.)
    /// </summary>
    public class ToolboxToolState
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
        public string ToolId { get; private set; }

        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        /// <summary>
        /// Different tools may use this arbitrarily. Currently decodable and leveled readers use it to store
        /// the stage or level a book belongs to (at least the one last active when editing it).
        /// </summary>
        [JsonProperty("state")]
        public string State { get; set; }

        public static ToolboxToolState CreateFromToolId(string toolId)
        {
            return new ToolboxToolState() { ToolId = toolId };
        }

        /// <summary>
        /// The name used to identify this tool's state in RetrieveToolSettings (the state passed to the
        /// tool's initializer).
        /// </summary>
        internal string StateName
        {
            get { return ToolId + "State"; }
        }

        public static object GetToolboxToolFromJsonObject(JObject item)
        {
            return item.ToObject<ToolboxToolState>();
        }
    }

    /// <summary>
    /// This class is used as the ItemConverterType for the Tools property of BookMetaData.
    /// It originally allowed us to deserialize a sequence of polymorphic tools, creating the
    /// right subclass for each based on the name.
    /// Now we just have one class ToolboxTool...it may be possible to get rid of it altogether.
    /// </summary>
    public class ToolboxToolConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(ToolboxToolState).IsAssignableFrom(objectType);
        }

        // Default writing is fine.
        public override bool CanWrite
        {
            get { return false; }
        }

        //implement JsonConverter.ReadJson
        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer
        )
        {
            JObject item = JObject.Load(reader);
            return ToolboxToolState.GetToolboxToolFromJsonObject(item);
        }

        // We don't need a real implementation of this because returning false from CanWrite
        // tells the converter to use the default write code.
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
