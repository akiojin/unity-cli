using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UnityCliBridge.Models
{
    /// <summary>
    /// Represents a command received from the MCP server
    /// </summary>
    [Serializable]
    public class Command
    {
        /// <summary>
        /// Unique identifier for the command
        /// </summary>
        [JsonProperty("id")]
        public string Id { get; set; }
        
        /// <summary>
        /// The type of command to execute
        /// </summary>
        [JsonProperty("type")]
        public string Type { get; set; }
        
        /// <summary>
        /// Parameters for the command as a JSON object
        /// </summary>
        [JsonProperty("params")]
        public JObject Parameters { get; set; }
        
        /// <summary>
        /// Timestamp when the command was received
        /// </summary>
        [JsonIgnore]
        public DateTime ReceivedAt { get; set; } = DateTime.Now;
        
        public override string ToString()
        {
            return $"Command[{Id}]: {Type}";
        }
    }
}