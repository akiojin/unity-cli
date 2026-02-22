namespace UnityCliBridge.Models
{
    /// <summary>
    /// Represents the connection status of the Unity CLI Bridge
    /// </summary>
    public enum BridgeStatus
    {
        /// <summary>
        /// Unity CLI Bridge listener is not configured
        /// </summary>
        NotConfigured,
        
        /// <summary>
        /// Disconnected from the Unity CLI Bridge listener
        /// </summary>
        Disconnected,
        
        /// <summary>
        /// Currently attempting to connect
        /// </summary>
        Connecting,
        
        /// <summary>
        /// Successfully connected to the Unity CLI Bridge listener
        /// </summary>
        Connected,
        
        /// <summary>
        /// An error occurred during connection or operation
        /// </summary>
        Error
    }
}
