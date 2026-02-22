using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;

namespace ComplexTestNamespace
{
    // 大サイズテストファイル（約500行）
    [System.Serializable]
    public class TestDataContainer
    {
        public int id;
        public string name;
        public Vector3 position;
        public Quaternion rotation;
        public Color color;
        
        public TestDataContainer(int id, string name)
        {
            this.id = id;
            this.name = name;
            this.position = Vector3.zero;
            this.rotation = Quaternion.identity;
            this.color = Color.white;
        }
    }
    
    public interface ITestInterface
    {
        void ExecuteTest();
        bool ValidateTest();
        string GetTestResult();
    }
    
    public abstract class AbstractTestClass
    {
        protected string testName;
        protected float testScore;
        
        public AbstractTestClass(string name)
        {
            testName = name;
            testScore = 0f;
        }
        
        public abstract void InitializeTest();
        public abstract void RunTest();
        public abstract void CleanupTest();
        
        protected virtual void LogTestResult()
        {
            UnityEngine.Debug.LogFormat($"Test {testName} completed with score: {testScore}");
        }
    }
    
    public class LargeTestFile : MonoBehaviour, ITestInterface
    {
        [Header("Configuration")]
        [SerializeField] private int maxDataEntries = 1000;
        [SerializeField] private float updateInterval = 0.1f;
        [SerializeField] private bool enableLogging = true;
        [SerializeField] private TestDataContainer[] testDataArray;
        
        [Header("Performance Settings")]
        [SerializeField] private int batchSize = 50;
        [SerializeField] private float performanceThreshold = 16.67f; // 60 FPS
        [SerializeField] private bool useObjectPooling = true;
        
        private List<TestDataContainer> activeDataList;
        private Queue<TestDataContainer> dataPool;
        private Dictionary<int, TestDataContainer> dataLookup;
        private HashSet<int> processedIds;
        
        private Coroutine dataProcessingCoroutine;
        private Coroutine performanceMonitorCoroutine;
        private System.Diagnostics.Stopwatch performanceTimer;
        
        public delegate void DataProcessedEvent(TestDataContainer data);
        public delegate void PerformanceEvent(float deltaTime, int processedCount);
        
        public event DataProcessedEvent OnDataProcessed;
        public event PerformanceEvent OnPerformanceUpdate;
        
        public enum ProcessingMode
        {
            Sequential,
            Parallel,
            Batched,
            Optimized
        }
        
        public enum DataState
        {
            Uninitialized,
            Initializing,
            Ready,
            Processing,
            Complete,
            Error
        }
        
        [SerializeField] private ProcessingMode currentProcessingMode = ProcessingMode.Sequential;
        [SerializeField] private DataState currentDataState = DataState.Uninitialized;
        
        private readonly object dataLock = new object();
        private volatile bool isProcessing = false;
        
        void Start()
        {
            InitializeComponents();
            SetupDataStructures();
            StartDataProcessing();
            InitializePerformanceMonitoring();
        }
        
        void Update()
        {
            HandleUserInput();
            UpdateDataProcessing();
            MonitorPerformance();
            UpdateUI();
        }
        
        private void InitializeComponents()
        {
            performanceTimer = new System.Diagnostics.Stopwatch();
            activeDataList = new List<TestDataContainer>();
            dataPool = new Queue<TestDataContainer>();
            dataLookup = new Dictionary<int, TestDataContainer>();
            processedIds = new HashSet<int>();
            
            currentDataState = DataState.Initializing;
            
            if (enableLogging)
            {
                UnityEngine.Debug.LogFormat("Large test file initialized");
            }
        }
        
        private void SetupDataStructures()
        {
            // Initialize object pool
            if (useObjectPooling)
            {
                for (int i = 0; i < maxDataEntries; i++)
                {
                    TestDataContainer data = new TestDataContainer(i, $"PooledData_{i}");
                    dataPool.Enqueue(data);
                }
            }
            
            // Initialize test data array
            testDataArray = new TestDataContainer[100];
            for (int i = 0; i < testDataArray.Length; i++)
            {
                testDataArray[i] = CreateTestData(i, $"ArrayData_{i}");
            }
            
            currentDataState = DataState.Ready;
        }
        
        private TestDataContainer CreateTestData(int id, string name)
        {
            TestDataContainer data = new TestDataContainer(id, name);
            data.position = new Vector3(
                UnityEngine.Random.Range(-100f, 100f),
                UnityEngine.Random.Range(0f, 50f),
                UnityEngine.Random.Range(-100f, 100f)
            );
            data.rotation = Quaternion.Euler(
                UnityEngine.Random.Range(0f, 360f),
                UnityEngine.Random.Range(0f, 360f),
                UnityEngine.Random.Range(0f, 360f)
            );
            data.color = new Color(
                UnityEngine.Random.Range(0f, 1f),
                UnityEngine.Random.Range(0f, 1f),
                UnityEngine.Random.Range(0f, 1f),
                1f
            );
            
            return data;
        }
        
        private void StartDataProcessing()
        {
            if (dataProcessingCoroutine != null)
            {
                StopCoroutine(dataProcessingCoroutine);
            }
            
            dataProcessingCoroutine = StartCoroutine(ProcessDataCoroutine());
        }
        
        private IEnumerator ProcessDataCoroutine()
        {
            currentDataState = DataState.Processing;
            isProcessing = true;
            
            while (isProcessing)
            {
                performanceTimer.Restart();
                
                switch (currentProcessingMode)
                {
                    case ProcessingMode.Sequential:
                        yield return ProcessSequential();
                        break;
                    case ProcessingMode.Parallel:
                        yield return ProcessParallel();
                        break;
                    case ProcessingMode.Batched:
                        yield return ProcessBatched();
                        break;
                    case ProcessingMode.Optimized:
                        yield return ProcessOptimized();
                        break;
                }
                
                performanceTimer.Stop();
                
                float deltaTime = (float)performanceTimer.Elapsed.TotalMilliseconds;
                OnPerformanceUpdate?.Invoke(deltaTime, activeDataList.Count);
                
                yield return new WaitForSeconds(updateInterval);
            }
            
            currentDataState = DataState.Complete;
        }
        
        private IEnumerator ProcessSequential()
        {
            for (int i = 0; i < batchSize && i < testDataArray.Length; i++)
            {
                ProcessSingleData(testDataArray[i]);
                yield return null;
            }
        }
        
        private IEnumerator ProcessParallel()
        {
            var batch = testDataArray.Take(batchSize).ToArray();
            
            System.Threading.Tasks.Parallel.ForEach(batch, data =>
            {
                lock (dataLock)
                {
                    ProcessSingleData(data);
                }
            });
            
            yield return null;
        }
        
        private IEnumerator ProcessBatched()
        {
            for (int batchStart = 0; batchStart < testDataArray.Length; batchStart += batchSize)
            {
                int batchEnd = Mathf.Min(batchStart + batchSize, testDataArray.Length);
                
                for (int i = batchStart; i < batchEnd; i++)
                {
                    ProcessSingleData(testDataArray[i]);
                }
                
                yield return null;
            }
        }
        
        private IEnumerator ProcessOptimized()
        {
            var dataToProcess = activeDataList
                .Where(data => !processedIds.Contains(data.id))
                .Take(batchSize)
                .ToList();
            
            foreach (var data in dataToProcess)
            {
                ProcessSingleData(data);
                processedIds.Add(data.id);
            }
            
            yield return null;
        }
        
        private void ProcessSingleData(TestDataContainer data)
        {
            // Simulate complex processing
            data.position += Vector3.one * Time.deltaTime;
            data.rotation *= Quaternion.Euler(1f, 1f, 1f);
            
            // Color animation
            float hue = (Time.time + data.id) % 1f;
            data.color = Color.HSVToRGB(hue, 0.8f, 0.9f);
            
            // Update lookup
            if (!dataLookup.ContainsKey(data.id))
            {
                dataLookup.Add(data.id, data);
            }
            else
            {
                dataLookup[data.id] = data;
            }
            
            OnDataProcessed?.Invoke(data);
        }
        
        private void HandleUserInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.lKey.wasPressedThisFrame)
            {
                SwitchProcessingMode();
            }
            
            if (keyboard.pKey.wasPressedThisFrame)
            {
                ToggleProcessing();
            }
            
            if (keyboard.rKey.wasPressedThisFrame)
            {
                ResetAllData();
            }
            
            if (keyboard.gKey.wasPressedThisFrame)
            {
                GenerateRandomData();
            }
        }
        
        private void SwitchProcessingMode()
        {
            int modeCount = Enum.GetValues(typeof(ProcessingMode)).Length;
            currentProcessingMode = (ProcessingMode)(((int)currentProcessingMode + 1) % modeCount);
            
            if (enableLogging)
            {
                UnityEngine.Debug.LogFormat($"Switched to processing mode: {currentProcessingMode}");
            }
        }
        
        private void ToggleProcessing()
        {
            isProcessing = !isProcessing;
            
            if (isProcessing && dataProcessingCoroutine == null)
            {
                StartDataProcessing();
            }
            
            if (enableLogging)
            {
                UnityEngine.Debug.LogFormat($"Processing {(isProcessing ? "enabled" : "disabled")}");
            }
        }
        
        private void ResetAllData()
        {
            lock (dataLock)
            {
                activeDataList.Clear();
                dataLookup.Clear();
                processedIds.Clear();
                
                // Repopulate with fresh data
                SetupDataStructures();
            }
            
            if (enableLogging)
            {
                UnityEngine.Debug.LogFormat("All data reset");
            }
        }
        
        private void GenerateRandomData()
        {
            if (useObjectPooling && dataPool.Count > 0)
            {
                var data = dataPool.Dequeue();
                data = CreateTestData(data.id, $"Generated_{data.id}_{Time.time}");
                activeDataList.Add(data);
            }
            else if (activeDataList.Count < maxDataEntries)
            {
                var data = CreateTestData(activeDataList.Count, $"Generated_{activeDataList.Count}_{Time.time}");
                activeDataList.Add(data);
            }
        }
        
        private void UpdateDataProcessing()
        {
            if (activeDataList.Count > maxDataEntries)
            {
                PruneExcessData();
            }
            
            OptimizeDataStructures();
        }
        
        private void PruneExcessData()
        {
            while (activeDataList.Count > maxDataEntries)
            {
                var removedData = activeDataList[0];
                activeDataList.RemoveAt(0);
                
                if (useObjectPooling)
                {
                    dataPool.Enqueue(removedData);
                }
                
                if (dataLookup.ContainsKey(removedData.id))
                {
                    dataLookup.Remove(removedData.id);
                }
            }
        }
        
        private void OptimizeDataStructures()
        {
            if (Time.frameCount % 300 == 0) // Every 5 seconds at 60 FPS
            {
                // Clean up processed IDs to prevent memory bloat
                if (processedIds.Count > maxDataEntries * 2)
                {
                    processedIds.Clear();
                }
                
                // Compact lookup dictionary
                var activeIds = activeDataList.Select(data => data.id).ToHashSet();
                var keysToRemove = dataLookup.Keys.Where(id => !activeIds.Contains(id)).ToList();
                
                foreach (var key in keysToRemove)
                {
                    dataLookup.Remove(key);
                }
            }
        }
        
        private void MonitorPerformance()
        {
            if (Time.deltaTime > performanceThreshold / 1000f)
            {
                if (enableLogging)
                {
                    UnityEngine.Debug.LogWarning($"Performance warning: Frame took {Time.deltaTime * 1000f:F2}ms");
                }
                
                // Automatic optimization
                if (currentProcessingMode == ProcessingMode.Parallel)
                {
                    currentProcessingMode = ProcessingMode.Optimized;
                }
            }
        }
        
        private void UpdateUI()
        {
            // This would update UI elements in a real scenario
            // For now, we'll just log occasionally
            if (Time.frameCount % 600 == 0 && enableLogging) // Every 10 seconds at 60 FPS
            {
                UnityEngine.Debug.LogFormat($"Status - Active: {activeDataList.Count}, Processed: {processedIds.Count}, Mode: {currentProcessingMode}");
            }
        }
        
        private void InitializePerformanceMonitoring()
        {
            if (performanceMonitorCoroutine != null)
            {
                StopCoroutine(performanceMonitorCoroutine);
            }
            
            performanceMonitorCoroutine = StartCoroutine(PerformanceMonitorCoroutine());
        }
        
        private IEnumerator PerformanceMonitorCoroutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(5f);
                
                if (enableLogging)
                {
                    float avgFrameTime = Time.deltaTime * 1000f;
                    int memoryUsage = (int)(System.GC.GetTotalMemory(false) / (1024 * 1024));
                    
                    UnityEngine.Debug.LogFormat($"Performance Stats - Frame: {avgFrameTime:F2}ms, Memory: {memoryUsage}MB, Objects: {activeDataList.Count}");
                }
            }
        }
        
        // ITestInterface implementation
        public void ExecuteTest()
        {
            isProcessing = true;
            currentDataState = DataState.Processing;
            StartDataProcessing();
        }
        
        public bool ValidateTest()
        {
            return activeDataList.Count > 0 && 
                   dataLookup.Count > 0 && 
                   currentDataState == DataState.Ready;
        }
        
        public string GetTestResult()
        {
            return $"Test completed with {activeDataList.Count} active data entries, " +
                   $"{processedIds.Count} processed items, using {currentProcessingMode} mode";
        }
        
        // Public API methods
        public void AddTestData(TestDataContainer data)
        {
            if (activeDataList.Count < maxDataEntries)
            {
                activeDataList.Add(data);
                dataLookup[data.id] = data;
            }
        }
        
        public TestDataContainer GetTestData(int id)
        {
            return dataLookup.TryGetValue(id, out TestDataContainer data) ? data : null;
        }
        
        public List<TestDataContainer> GetAllTestData()
        {
            return new List<TestDataContainer>(activeDataList);
        }
        
        public void ClearAllData()
        {
            lock (dataLock)
            {
                activeDataList.Clear();
                dataLookup.Clear();
                processedIds.Clear();
            }
        }
        
        // Cleanup methods
        void OnDestroy()
        {
            isProcessing = false;
            
            if (dataProcessingCoroutine != null)
            {
                StopCoroutine(dataProcessingCoroutine);
            }
            
            if (performanceMonitorCoroutine != null)
            {
                StopCoroutine(performanceMonitorCoroutine);
            }
            
            ClearAllData();
            
            OnDataProcessed = null;
            OnPerformanceUpdate = null;
        }
        
        void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                isProcessing = false;
            }
            else
            {
                isProcessing = true;
                StartDataProcessing();
            }
        }
        
        void OnApplicationQuit()
        {
            if (enableLogging)
            {
                UnityEngine.Debug.LogFormat("Large test file application quit");
            }
        }
    }
}
