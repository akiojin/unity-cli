namespace UnityCliBridge.Models
{
    /// <summary>
    /// Represents the connection status of the Unity CLI Bridge
    /// </summary>
    public enum McpStatus
    {
        /// <summary>
        /// MCP server is not configured
        /// </summary>
        NotConfigured,
        
        /// <summary>
        /// Disconnected from MCP server
        /// </summary>
        Disconnected,
        
        /// <summary>
        /// Currently attempting to connect
        /// </summary>
        Connecting,
        
        /// <summary>
        /// Successfully connected to MCP server
        /// </summary>
        Connected,
        
        /// <summary>
        /// An error occurred during connection or operation
        /// </summary>
        Error
    }
}