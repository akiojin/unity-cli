# Profiler Performance Measurement - Quick Start Guide

This guide helps you get started with Unity Profiler performance measurement via MCP tools.

## Prerequisites

- Unity Editor with `unity-cli` package installed
- MCP client connected to Unity Editor
- Unity project running (Play Mode or Edit Mode)

## Basic Workflow

### 1. Start Profiling Session

```javascript
// Start profiling in normal mode (standard profiling)
const startResult = await client.callTool('profiler_start', {
  mode: 'normal',          // 'normal' or 'deep' (deep = more detailed but higher overhead)
  recordToFile: true,      // Save .data file for analysis in Unity Profiler Window
  maxDurationSec: 60       // Auto-stop after 60 seconds (optional, 0 = unlimited)
});

console.log(startResult);
// {
//   sessionId: "a1b2c3d4...",
//   startedAt: "2025-11-17T15:00:00.000Z",
//   isRecording: true,
//   outputPath: ".unity/capture/profiler_a1b2c3d4..._2025-11-17_15-00-00.data"
// }
```

### 2. Check Status (Optional)

```javascript
// Get current profiling status
const status = await client.callTool('profiler_status', {});

console.log(status);
// {
//   isRecording: true,
//   sessionId: "a1b2c3d4...",
//   startedAt: "2025-11-17T15:00:00.000Z",
//   elapsedSec: 15.5,
//   remainingSec: 44.5    // null if no maxDurationSec set
// }
```

### 3. Stop Profiling

```javascript
// Stop profiling session
const stopResult = await client.callTool('profiler_stop', {
  sessionId: "a1b2c3d4..."  // Optional: specify session to stop (current session if omitted)
});

console.log(stopResult);
// {
//   sessionId: "a1b2c3d4...",
//   outputPath: ".unity/capture/profiler_a1b2c3d4..._2025-11-17_15-00-00.data",
//   duration: 60.2,
//   frameCount: 1805,
//   metrics: null           // Non-null if recordToFile=false
// }
```

### 4. Analyze Results

1. Open Unity Editor
2. Window → Analysis → Profiler
3. Click "Load" button
4. Select `.unity/capture/profiler_*.data` file
5. Analyze CPU, memory, rendering, and GC metrics

## Advanced Usage

### Real-time Metrics (No File Output)

```javascript
// Start profiling without saving .data file
const startResult = await client.callTool('profiler_start', {
  mode: 'normal',
  recordToFile: false,     // Skip file output
  metrics: [               // Optional: specific metrics to record
    'System Used Memory',
    'Draw Calls Count',
    'GC Allocated In Frame'
  ]
});

// Stop and get metrics
const stopResult = await client.callTool('profiler_stop', {});
console.log(stopResult.metrics);
// [
//   { name: "System Used Memory", value: 524288000, unit: "bytes" },
//   { name: "Draw Calls Count", value: 120, unit: "count" },
//   { name: "GC Allocated In Frame", value: 8192, unit: "bytes" }
// ]
```

### List Available Metrics

```javascript
// Get all available metrics grouped by category
const metricsResult = await client.callTool('profiler_get_metrics', {
  listAvailable: true
});

console.log(metricsResult.categories);
// {
//   "Memory": ["System Used Memory", "GC Allocated In Frame", ...],
//   "Rendering": ["Draw Calls Count", "Triangles Count", ...],
//   "Scripts": ["Scripts Update Time", ...],
//   ...
// }
```

### Query Current Metric Values

```javascript
// Query specific metrics (current frame values)
const metricsResult = await client.callTool('profiler_get_metrics', {
  listAvailable: false,
  metrics: ['System Used Memory', 'Draw Calls Count']
});

console.log(metricsResult.metrics);
// [
//   { name: "System Used Memory", value: 524288000, unit: "bytes" },
//   { name: "Draw Calls Count", value: 120, unit: "count" }
// ]
```

### Deep Profiling

```javascript
// Start deep profiling (more detailed analysis, higher overhead)
const startResult = await client.callTool('profiler_start', {
  mode: 'deep',            // Enable deep profiling
  recordToFile: true,
  maxDurationSec: 30       // Shorter duration recommended for deep profiling
});

// Note: Deep profiling records all function calls (including internal Unity calls)
// This provides more detailed analysis but significantly impacts performance
```

## Error Handling

### Common Errors

1. **E_ALREADY_RUNNING**: Another profiling session is already running

   ```javascript
   // Solution: Stop current session first
   await client.callTool('profiler_stop', {});
   ```

2. **E_INVALID_MODE**: Invalid mode parameter

   ```javascript
   // Valid modes: 'normal' or 'deep'
   await client.callTool('profiler_start', { mode: 'normal' });
   ```

3. **E_INVALID_METRICS**: Invalid metric names

   ```javascript
   // Solution: Query available metrics first
   const available = await client.callTool('profiler_get_metrics', { listAvailable: true });
   ```

4. **E_NOT_RECORDING**: No profiling session is running

   ```javascript
   // Solution: Start profiling first
   await client.callTool('profiler_start', {});
   ```

5. **E_INVALID_SESSION**: Session ID does not match current session

   ```javascript
   // Solution: Use correct session ID or omit parameter
   const status = await client.callTool('profiler_status', {});
   await client.callTool('profiler_stop', { sessionId: status.sessionId });
   ```

6. **E_FILE_IO**: Failed to save .data file

   ```javascript
   // Solution: Check disk space and .unity/capture/ directory permissions
   ```

## Best Practices

1. **Use Normal Mode First**: Start with `mode: 'normal'` for general profiling. Only use `mode: 'deep'` when you need detailed function-level analysis.

2. **Set Auto-stop Duration**: Always set `maxDurationSec` to prevent unlimited recording that may consume disk space.

3. **Profile During Gameplay**: Start profiling during actual gameplay scenarios for realistic performance data.

4. **Analyze .data Files**: Use Unity Profiler Window to visualize and analyze `.data` files. The MCP tools provide raw data collection, but Unity's built-in tools offer better visualization.

5. **Real-time Metrics for Automation**: Use `recordToFile: false` with specific `metrics` for automated performance testing or CI/CD pipelines.

6. **Check Status Before Stopping**: Query `profiler_status` to confirm session is running before calling `profiler_stop`.

## Troubleshooting

### Profiling Session Won't Start

- **Check Unity Editor**: Ensure Unity Editor is running and connected
- **Check Existing Session**: Another session may be running. Call `profiler_status` and `profiler_stop` if needed.

### .data File Not Saved

- **Check Output Path**: Verify `.unity/capture/` directory exists and is writable
- **Check Disk Space**: Ensure sufficient disk space for profiling data
- **Check recordToFile**: Confirm `recordToFile: true` was set in `profiler_start`

### Metrics Not Available

- **Platform-Specific**: Some metrics are platform-specific (e.g., mobile-only metrics)
- **Query Available**: Use `profiler_get_metrics` with `listAvailable: true` to check available metrics

### Performance Impact

- **Deep Profiling Overhead**: `mode: 'deep'` has significant performance overhead. Use short durations (10-30 seconds).
- **Metric Count**: Recording many metrics increases overhead. Limit to essential metrics for real-time profiling.

## Next Steps

- Read [spec.md](./spec.md) for detailed feature specifications
- Read [plan.md](./plan.md) for implementation architecture
- Explore Unity Profiler Window documentation: <https://docs.unity3d.com/Manual/Profiler.html>
