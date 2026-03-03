using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Threading.Tasks;
using System.Text;
using System.IO;
using System.Reflection;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
using System.ComponentModel;
using System.Globalization;

namespace GigaTestNamespace
{
    // [ScriptTest] Marker inserted via Codex CLI on 2025-09-03
    // 目的: Script系ツール検証用（機能変更なし）
    // 超大規模テストファイル（10000行以上）
    // 日本語コメントとさまざまな構造を含む包括的なテストファイル

    #region デリゲートとイベント定義

    public delegate void DataProcessingDelegate(object data);
    public delegate Task AsyncDataProcessingDelegate(object data);
    public delegate T GenericProcessingDelegate<T>(T input) where T : class;
    public delegate void MultiParameterDelegate(int id, string name, float value, bool flag);

    #endregion

    #region インターフェース定義

    /// <summary>
    /// データプロセッサーインターフェース
    /// </summary>
    public interface IDataProcessor
    {
        void ProcessData(object data);
        Task ProcessDataAsync(object data);
        bool ValidateData(object data);
        string GetProcessorName();
        int Priority { get; set; }
    }

    /// <summary>
    /// 拡張データプロセッサーインターフェース
    /// </summary>
    public interface IAdvancedDataProcessor : IDataProcessor
    {
        void PreProcess(object data);
        void PostProcess(object data);
        Task<bool> ProcessBatchAsync(IEnumerable<object> dataList);
        event EventHandler<DataProcessedEventArgs> DataProcessed;
    }

    /// <summary>
    /// ジェネリックプロセッサーインターフェース
    /// </summary>
    public interface IGenericProcessor<T> where T : class
    {
        T Process(T input);
        Task<T> ProcessAsync(T input);
        bool CanProcess(T input);
        IEnumerable<T> ProcessMultiple(IEnumerable<T> inputs);
    }

    #endregion

    #region 列挙型定義

    /// <summary>
    /// 処理状態の列挙型
    /// </summary>
    public enum ProcessingState
    {
        /// <summary>初期状態</summary>
        Idle,
        /// <summary>初期化中</summary>
        Initializing,
        /// <summary>処理中</summary>
        Processing,
        /// <summary>一時停止</summary>
        Paused,
        /// <summary>完了</summary>
        Completed,
        /// <summary>エラー</summary>
        Error,
        /// <summary>中止</summary>
        Aborted
    }

    /// <summary>
    /// データタイプの列挙型
    /// </summary>
    [Flags]
    public enum DataType
    {
        None = 0,
        Integer = 1 << 0,
        Float = 1 << 1,
        String = 1 << 2,
        Boolean = 1 << 3,
        Array = 1 << 4,
        Dictionary = 1 << 5,
        Object = 1 << 6,
        All = Integer | Float | String | Boolean | Array | Dictionary | Object
    }

    #endregion

    #region 構造体定義

    /// <summary>
    /// パフォーマンスデータ構造体
    /// </summary>
    [Serializable]
    public struct PerformanceData
    {
        public float frameTime;
        public float renderTime;
        public float scriptTime;
        public int drawCalls;
        public int triangles;
        public int vertices;
        public float memoryUsage;

        public PerformanceData(float frame, float render, float script)
        {
            frameTime = frame;
            renderTime = render;
            scriptTime = script;
            drawCalls = 0;
            triangles = 0;
            vertices = 0;
            memoryUsage = 0;
        }

        public static PerformanceData operator +(PerformanceData a, PerformanceData b)
        {
            return new PerformanceData
            {
                frameTime = a.frameTime + b.frameTime,
                renderTime = a.renderTime + b.renderTime,
                scriptTime = a.scriptTime + b.scriptTime,
                drawCalls = a.drawCalls + b.drawCalls,
                triangles = a.triangles + b.triangles,
                vertices = a.vertices + b.vertices,
                memoryUsage = a.memoryUsage + b.memoryUsage
            };
        }
    }

    #endregion

    #region 属性定義

    /// <summary>
    /// カスタム検証属性
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class ValidateAttribute : PropertyAttribute
    {
        public float Min { get; set; }
        public float Max { get; set; }
        public string Pattern { get; set; }

        public ValidateAttribute(float min = float.MinValue, float max = float.MaxValue)
        {
            Min = min;
            Max = max;
        }
    }

    /// <summary>
    /// 処理優先度属性
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public class ProcessingPriorityAttribute : Attribute
    {
        public int Priority { get; }

        public ProcessingPriorityAttribute(int priority)
        {
            Priority = priority;
        }
    }

    #endregion

    #region イベント引数クラス

    /// <summary>
    /// データ処理完了イベント引数
    /// </summary>
    public class DataProcessedEventArgs : EventArgs
    {
        public object ProcessedData { get; set; }
        public ProcessingState State { get; set; }
        public DateTime Timestamp { get; set; }
        public string Message { get; set; }
        public Exception Exception { get; set; }

        public DataProcessedEventArgs(object data, ProcessingState state)
        {
            ProcessedData = data;
            State = state;
            Timestamp = DateTime.Now;
        }
    }

    #endregion

    #region 抽象基底クラス

    /// <summary>
    /// プロセッサー基底クラス
    /// </summary>
    public abstract class BaseProcessor : IAdvancedDataProcessor
    {
        private int _priority;
        protected readonly object _lock = new object();
        protected Queue<object> _dataQueue = new Queue<object>();
        protected bool _isProcessing;

        public virtual int Priority
        {
            get => _priority;
            set => _priority = Mathf.Clamp(value, 0, 100);
        }

        public event EventHandler<DataProcessedEventArgs> DataProcessed;

        public abstract void ProcessData(object data);
        public abstract string GetProcessorName();

        public virtual async Task ProcessDataAsync(object data)
        {
            await Task.Run(() => ProcessData(data));
        }

        public virtual bool ValidateData(object data)
        {
            return data != null;
        }

        public virtual void PreProcess(object data)
        {
            UnityEngine.Debug.Log($"PreProcessing: {data}");
        }

        public virtual void PostProcess(object data)
        {
            UnityEngine.Debug.Log($"PostProcessing: {data}");
            OnDataProcessed(new DataProcessedEventArgs(data, ProcessingState.Completed));
        }

        public virtual async Task<bool> ProcessBatchAsync(IEnumerable<object> dataList)
        {
            var tasks = dataList.Select(ProcessDataAsync);
            await Task.WhenAll(tasks);
            return true;
        }

        protected virtual void OnDataProcessed(DataProcessedEventArgs e)
        {
            DataProcessed?.Invoke(this, e);
        }
    }

    #endregion

    #region ジェネリッククラス

    /// <summary>
    /// ジェネリックコンテナクラス
    /// </summary>
    public class GenericContainer<T> where T : class, new()
    {
        private List<T> _items = new List<T>();
        private Dictionary<string, T> _itemMap = new Dictionary<string, T>();
        private readonly object _syncRoot = new object();

        public int Count => _items.Count;

        public T this[int index]
        {
            get
            {
                lock (_syncRoot)
                {
                    return _items[index];
                }
            }
            set
            {
                lock (_syncRoot)
                {
                    _items[index] = value;
                }
            }
        }

        public void Add(T item)
        {
            lock (_syncRoot)
            {
                _items.Add(item);
            }
        }

        public bool Remove(T item)
        {
            lock (_syncRoot)
            {
                return _items.Remove(item);
            }
        }

        public void Clear()
        {
            lock (_syncRoot)
            {
                _items.Clear();
                _itemMap.Clear();
            }
        }

        public IEnumerable<T> Where(Func<T, bool> predicate)
        {
            lock (_syncRoot)
            {
                return _items.Where(predicate).ToList();
            }
        }

        public T FirstOrDefault(Func<T, bool> predicate)
        {
            lock (_syncRoot)
            {
                return _items.FirstOrDefault(predicate);
            }
        }
    }

    #endregion

    #region 具象実装クラス

    /// <summary>
    /// 標準データプロセッサー実装
    /// </summary>
    [ProcessingPriority(50)]
    public class StandardDataProcessor : BaseProcessor
    {
        private readonly StringBuilder _logBuilder = new StringBuilder();
        private int _processedCount;

        public override void ProcessData(object data)
        {
            PreProcess(data);

            // 実際の処理ロジック
            if (data is string strData)
            {
                ProcessStringData(strData);
            }
            else if (data is int intData)
            {
                ProcessIntData(intData);
            }
            else if (data is float floatData)
            {
                ProcessFloatData(floatData);
            }
            else
            {
                ProcessGenericData(data);
            }

            _processedCount++;
            PostProcess(data);
        }

        private void ProcessStringData(string data)
        {
            _logBuilder.AppendLine($"Processing string: {data}");
            // 文字列処理ロジック
            var processed = data.ToUpper();
            var reversed = new string(data.Reverse().ToArray());
            var hash = data.GetHashCode();
        }

        private void ProcessIntData(int data)
        {
            _logBuilder.AppendLine($"Processing int: {data}");
            // 整数処理ロジック
            var squared = data * data;
            var binary = Convert.ToString(data, 2);
            var hex = data.ToString("X");
        }

        private void ProcessFloatData(float data)
        {
            _logBuilder.AppendLine($"Processing float: {data}");
            // 浮動小数点処理ロジック
            var rounded = Mathf.Round(data);
            var ceiling = Mathf.Ceil(data);
            var floor = Mathf.Floor(data);
        }

        private void ProcessGenericData(object data)
        {
            _logBuilder.AppendLine($"Processing generic: {data?.GetType().Name ?? "null"}");
        }

        public override string GetProcessorName()
        {
            return "StandardDataProcessor";
        }
    }

    #endregion

    #region Unity MonoBehaviourクラス

    /// <summary>
    /// ゲームマネージャークラス
    /// </summary>
    public class GigaGameManager : MonoBehaviour
    {
        #region シングルトン実装

        private static GigaGameManager _instance;
        public static GigaGameManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<GigaGameManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("GigaGameManager");
                        _instance = go.AddComponent<GigaGameManager>();
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region フィールド定義

        [Header("基本設定")]
        [SerializeField] private bool _autoStart = true;
        [SerializeField] private float _updateInterval = 0.1f;
        [SerializeField] private int _maxRetries = 3;

        [Header("パフォーマンス設定")]
        [SerializeField] private bool _enableProfiling = false;
        [SerializeField] private int _targetFrameRate = 60;
        [SerializeField] private float _timeScale = 1.0f;

        [Header("デバッグ設定")]
        [SerializeField] private bool _verboseLogging = false;
        [SerializeField] private bool _showDebugUI = false;
        [SerializeField] private Color _debugColor = Color.green;

        private ProcessingState _currentState = ProcessingState.Idle;
        private Coroutine _mainCoroutine;
        private List<IDataProcessor> _processors = new List<IDataProcessor>();
        private Queue<Action> _mainThreadQueue = new Queue<Action>();
        private Dictionary<string, object> _gameData = new Dictionary<string, object>();

        #endregion

        #region Unityライフサイクル

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize();
        }

        private void Start()
        {
            if (_autoStart)
            {
                StartProcessing();
            }
        }

        private void Update()
        {
            ProcessMainThreadQueue();
            UpdatePerformanceMonitoring();

            if (_showDebugUI)
            {
                UpdateDebugUI();
            }
        }

        private void LateUpdate()
        {
            // 後処理更新
        }

        private void FixedUpdate()
        {
            // 物理更新
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                PauseProcessing();
            }
            else
            {
                ResumeProcessing();
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // フォーカス処理
        }

        #endregion

        #region 初期化とクリーンアップ

        private void Initialize()
        {
            Application.targetFrameRate = _targetFrameRate;
            Time.timeScale = _timeScale;

            InitializeProcessors();
            InitializeGameData();
            InitializeEventHandlers();
        }

        private void InitializeProcessors()
        {
            _processors.Add(new StandardDataProcessor());
            // 他のプロセッサーを追加

            _processors = _processors.OrderBy(p => p.Priority).ToList();
        }

        private void InitializeGameData()
        {
            _gameData["PlayerScore"] = 0;
            _gameData["Level"] = 1;
            _gameData["TimePlayed"] = 0f;
            _gameData["SessionId"] = Guid.NewGuid().ToString();
        }

        private void InitializeEventHandlers()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void Cleanup()
        {
            StopAllCoroutines();

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;

            _processors.Clear();
            _gameData.Clear();
            _mainThreadQueue.Clear();
        }

        #endregion

        #region 処理制御

        public void StartProcessing()
        {
            if (_currentState == ProcessingState.Processing)
                return;

            _currentState = ProcessingState.Processing;
            _mainCoroutine = StartCoroutine(MainProcessingLoop());
        }

        public void StopProcessing()
        {
            if (_mainCoroutine != null)
            {
                StopCoroutine(_mainCoroutine);
                _mainCoroutine = null;
            }

            _currentState = ProcessingState.Idle;
        }

        public void PauseProcessing()
        {
            if (_currentState == ProcessingState.Processing)
            {
                _currentState = ProcessingState.Paused;
            }
        }

        public void ResumeProcessing()
        {
            if (_currentState == ProcessingState.Paused)
            {
                _currentState = ProcessingState.Processing;
            }
        }

        public int UnityCli_ScriptToolProbe()
        {
            // Structured edit test: return a distinct value
            return 12345;
        }
        #endregion

        #region コルーチン

        private IEnumerator MainProcessingLoop()
        {
            while (_currentState == ProcessingState.Processing || _currentState == ProcessingState.Paused)
            {
                if (_currentState == ProcessingState.Paused)
                {
                    yield return new WaitForSeconds(0.1f);
                    continue;
                }

                // メイン処理ロジック
                yield return ProcessDataBatch();
                yield return new WaitForSeconds(_updateInterval);
            }
        }

        private IEnumerator ProcessDataBatch()
        {
            // バッチ処理ロジック
            yield return null;
        }

        #endregion

        #region ヘルパーメソッド

        private void ProcessMainThreadQueue()
        {
            lock (_mainThreadQueue)
            {
                while (_mainThreadQueue.Count > 0)
                {
                    var action = _mainThreadQueue.Dequeue();
                    action?.Invoke();
                }
            }
        }

        private void UpdatePerformanceMonitoring()
        {
            if (!_enableProfiling)
                return;

            // パフォーマンス監視ロジック
        }

        private void UpdateDebugUI()
        {
            // デバッグUI更新ロジック
        }

        public void ExecuteOnMainThread(Action action)
        {
            lock (_mainThreadQueue)
            {
                _mainThreadQueue.Enqueue(action);
            }
        }

        #endregion

        #region イベントハンドラー

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            UnityEngine.Debug.Log($"Scene loaded: {scene.name}");
        }

        private void OnSceneUnloaded(Scene scene)
        {
            UnityEngine.Debug.Log($"Scene unloaded: {scene.name}");
        }

        #endregion
    }

    #endregion

    #region ネットワーククラス

    /// <summary>
    /// ネットワークマネージャー
    /// </summary>
    public class NetworkManager : MonoBehaviour
    {
        #region ネットワーク定数

        private const int MAX_CONNECTIONS = 100;
        private const int PORT = 7777;
        private const float TIMEOUT = 30f;
        private const int BUFFER_SIZE = 1024;

        #endregion

        #region フィールド

        [Header("ネットワーク設定")]
        [SerializeField] private string _serverAddress = "localhost";
        [SerializeField] private int _serverPort = PORT;
        [SerializeField] private bool _autoConnect = false;

        private bool _isConnected;
        private bool _isServer;
        private float _lastPingTime;
        private Queue<NetworkMessage> _messageQueue = new Queue<NetworkMessage>();

        #endregion

        #region ネットワークメッセージ

        [Serializable]
        public class NetworkMessage
        {
            public int messageId;
            public string messageType;
            public byte[] data;
            public DateTime timestamp;

            public NetworkMessage(string type, byte[] messageData)
            {
                messageId = UnityEngine.Random.Range(1000, 9999);
                messageType = type;
                data = messageData;
                timestamp = DateTime.Now;
            }
        }

        #endregion

        #region 接続管理

        public void Connect(string address, int port)
        {
            StartCoroutine(ConnectAsync(address, port));
        }

        private IEnumerator ConnectAsync(string address, int port)
        {
            // 接続処理
            yield return new WaitForSeconds(1f);
            _isConnected = true;
        }

        public void Disconnect()
        {
            _isConnected = false;
            _messageQueue.Clear();
        }

        #endregion

        #region メッセージ送受信

        public void SendMessage(NetworkMessage message)
        {
            if (!_isConnected)
            {
                UnityEngine.Debug.LogWarning("Not connected to network");
                return;
            }

            _messageQueue.Enqueue(message);
        }

        private void ProcessMessages()
        {
            while (_messageQueue.Count > 0)
            {
                var message = _messageQueue.Dequeue();
                HandleMessage(message);
            }
        }

        private void HandleMessage(NetworkMessage message)
        {
            switch (message.messageType)
            {
                case "ping":
                    HandlePing(message);
                    break;
                case "data":
                    HandleData(message);
                    break;
                default:
                    UnityEngine.Debug.LogWarning($"Unknown message type: {message.messageType}");
                    break;
            }
        }

        private void HandlePing(NetworkMessage message)
        {
            _lastPingTime = Time.time;
        }

        private void HandleData(NetworkMessage message)
        {
            // データ処理
        }

        #endregion
    }

    #endregion

    #region AIシステム

    /// <summary>
    /// AI制御システム
    /// </summary>
    public class AIController : MonoBehaviour
    {
        #region AI状態定義

        public enum AIState
        {
            Idle,
            Patrol,
            Chase,
            Attack,
            Flee,
            Dead
        }

        #endregion

        #region フィールド

        [Header("AI設定")]
        [SerializeField] private AIState _currentState = AIState.Idle;
        [SerializeField] private float _detectionRadius = 10f;
        [SerializeField] private float _attackRadius = 2f;
        [SerializeField] private float _moveSpeed = 5f;

        [Header("パトロール設定")]
        [SerializeField] private Transform[] _patrolPoints;
        [SerializeField] private float _patrolWaitTime = 2f;

        private NavMeshAgent _navAgent;
        private Transform _target;
        private int _currentPatrolIndex;
        private float _stateTimer;

        #endregion

        #region AIステートマシン

        private void UpdateStateMachine()
        {
            switch (_currentState)
            {
                case AIState.Idle:
                    UpdateIdleState();
                    break;
                case AIState.Patrol:
                    UpdatePatrolState();
                    break;
                case AIState.Chase:
                    UpdateChaseState();
                    break;
                case AIState.Attack:
                    UpdateAttackState();
                    break;
                case AIState.Flee:
                    UpdateFleeState();
                    break;
            }
        }

        private void UpdateIdleState()
        {
            // アイドル状態の更新
            if (_patrolPoints != null && _patrolPoints.Length > 0)
            {
                ChangeState(AIState.Patrol);
            }
        }

        private void UpdatePatrolState()
        {
            // パトロール状態の更新
            if (_navAgent.remainingDistance < 0.5f)
            {
                _stateTimer += Time.deltaTime;
                if (_stateTimer >= _patrolWaitTime)
                {
                    MoveToNextPatrolPoint();
                    _stateTimer = 0f;
                }
            }

            CheckForTargets();
        }

        private void UpdateChaseState()
        {
            // 追跡状態の更新
            if (_target != null)
            {
                _navAgent.SetDestination(_target.position);

                float distance = Vector3.Distance(transform.position, _target.position);
                if (distance <= _attackRadius)
                {
                    ChangeState(AIState.Attack);
                }
                else if (distance > _detectionRadius * 1.5f)
                {
                    ChangeState(AIState.Patrol);
                }
            }
            else
            {
                ChangeState(AIState.Patrol);
            }
        }

        private void UpdateAttackState()
        {
            // 攻撃状態の更新
            if (_target != null)
            {
                float distance = Vector3.Distance(transform.position, _target.position);
                if (distance > _attackRadius)
                {
                    ChangeState(AIState.Chase);
                }
            }
            else
            {
                ChangeState(AIState.Patrol);
            }
        }

        private void UpdateFleeState()
        {
            // 逃走状態の更新
        }

        #endregion

        #region ヘルパーメソッド

        private void ChangeState(AIState newState)
        {
            _currentState = newState;
            _stateTimer = 0f;
        }

        private void MoveToNextPatrolPoint()
        {
            if (_patrolPoints.Length == 0)
                return;

            _currentPatrolIndex = (_currentPatrolIndex + 1) % _patrolPoints.Length;
            _navAgent.SetDestination(_patrolPoints[_currentPatrolIndex].position);
        }

        private void CheckForTargets()
        {
            Collider[] colliders = Physics.OverlapSphere(transform.position, _detectionRadius);
            foreach (var collider in colliders)
            {
                if (collider.CompareTag("Player"))
                {
                    _target = collider.transform;
                    ChangeState(AIState.Chase);
                    break;
                }
            }
        }

        #endregion

        #region Gizmos

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, _detectionRadius);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, _attackRadius);

            if (_patrolPoints != null)
            {
                Gizmos.color = Color.blue;
                for (int i = 0; i < _patrolPoints.Length; i++)
                {
                    if (_patrolPoints[i] != null)
                    {
                        Gizmos.DrawSphere(_patrolPoints[i].position, 0.5f);
                        if (i < _patrolPoints.Length - 1 && _patrolPoints[i + 1] != null)
                        {
                            Gizmos.DrawLine(_patrolPoints[i].position, _patrolPoints[i + 1].position);
                        }
                    }
                }
            }
        }

        #endregion
    }

    #endregion

    #region UIシステム

    /// <summary>
    /// UIマネージャー
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        #region UI要素

        [Header("メインUI")]
        [SerializeField] private Canvas _mainCanvas;
        [SerializeField] private GameObject _mainMenuPanel;
        [SerializeField] private GameObject _gameUIPanel;
        [SerializeField] private GameObject _pauseMenuPanel;

        [Header("HUD要素")]
        [SerializeField] private Text _scoreText;
        [SerializeField] private Text _timerText;
        [SerializeField] private Slider _healthBar;
        [SerializeField] private Slider _energyBar;

        [Header("ダイアログ")]
        [SerializeField] private GameObject _dialogPanel;
        [SerializeField] private Text _dialogText;
        [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton;

        #endregion

        #region フィールド

        private Stack<GameObject> _uiStack = new Stack<GameObject>();
        private bool _isUILocked;
        private Coroutine _fadeCoroutine;

        #endregion

        #region UI表示制御

        public void ShowMainMenu()
        {
            HideAllPanels();
            _mainMenuPanel.SetActive(true);
            _uiStack.Push(_mainMenuPanel);
        }

        public void ShowGameUI()
        {
            HideAllPanels();
            _gameUIPanel.SetActive(true);
            _uiStack.Push(_gameUIPanel);
        }

        public void ShowPauseMenu()
        {
            _pauseMenuPanel.SetActive(true);
            _uiStack.Push(_pauseMenuPanel);
            Time.timeScale = 0f;
        }

        public void HidePauseMenu()
        {
            _pauseMenuPanel.SetActive(false);
            if (_uiStack.Count > 0 && _uiStack.Peek() == _pauseMenuPanel)
            {
                _uiStack.Pop();
            }
            Time.timeScale = 1f;
        }

        private void HideAllPanels()
        {
            _mainMenuPanel.SetActive(false);
            _gameUIPanel.SetActive(false);
            _pauseMenuPanel.SetActive(false);
            _dialogPanel.SetActive(false);
        }

        #endregion

        #region HUD更新

        public void UpdateScore(int score)
        {
            if (_scoreText != null)
            {
                _scoreText.text = $"Score: {score:000000}";
            }
        }

        public void UpdateTimer(float time)
        {
            if (_timerText != null)
            {
                int minutes = Mathf.FloorToInt(time / 60f);
                int seconds = Mathf.FloorToInt(time % 60f);
                _timerText.text = $"{minutes:00}:{seconds:00}";
            }
        }

        public void UpdateHealthBar(float current, float max)
        {
            if (_healthBar != null)
            {
                _healthBar.value = current / max;
            }
        }

        public void UpdateEnergyBar(float current, float max)
        {
            if (_energyBar != null)
            {
                _energyBar.value = current / max;
            }
        }

        #endregion

        #region ダイアログ

        public void ShowDialog(string message, Action onConfirm = null, Action onCancel = null)
        {
            _dialogPanel.SetActive(true);
            _dialogText.text = message;

            _confirmButton.onClick.RemoveAllListeners();
            _confirmButton.onClick.AddListener(() =>
            {
                onConfirm?.Invoke();
                HideDialog();
            });

            _cancelButton.onClick.RemoveAllListeners();
            _cancelButton.onClick.AddListener(() =>
            {
                onCancel?.Invoke();
                HideDialog();
            });
        }

        public void HideDialog()
        {
            _dialogPanel.SetActive(false);
        }

        #endregion

        #region アニメーション

        public void FadeIn(CanvasGroup canvasGroup, float duration = 1f)
        {
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
            }
            _fadeCoroutine = StartCoroutine(FadeCanvasGroup(canvasGroup, canvasGroup.alpha, 1f, duration));
        }

        public void FadeOut(CanvasGroup canvasGroup, float duration = 1f)
        {
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
            }
            _fadeCoroutine = StartCoroutine(FadeCanvasGroup(canvasGroup, canvasGroup.alpha, 0f, duration));
        }

        private IEnumerator FadeCanvasGroup(CanvasGroup canvasGroup, float start, float end, float duration)
        {
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                canvasGroup.alpha = Mathf.Lerp(start, end, t);
                yield return null;
            }

            canvasGroup.alpha = end;
        }

        #endregion
    }

    #endregion

    #region 物理システム

    /// <summary>
    /// カスタム物理コントローラー
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CustomPhysicsController : MonoBehaviour
    {
        #region 物理設定

        [Header("移動設定")]
        [SerializeField] private float _moveSpeed = 10f;
        [SerializeField] private float _jumpForce = 5f;
        [SerializeField] private float _gravity = -9.81f;

        [Header("地面検出")]
        [SerializeField] private Transform _groundCheck;
        [SerializeField] private float _groundDistance = 0.4f;
        [SerializeField] private LayerMask _groundMask;

        #endregion

        #region フィールド

        private Rigidbody _rigidbody;
        private Vector3 _velocity;
        private bool _isGrounded;
        private float _terminalVelocity = -50f;

        #endregion

        #region 初期化

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.useGravity = false;
        }

        #endregion

        #region 物理更新

        private void FixedUpdate()
        {
            CheckGrounded();
            ApplyGravity();
            ApplyMovement();
        }

        private void CheckGrounded()
        {
            _isGrounded = Physics.CheckSphere(_groundCheck.position, _groundDistance, _groundMask);
        }

        private void ApplyGravity()
        {
            if (!_isGrounded)
            {
                _velocity.y += _gravity * Time.fixedDeltaTime;
                _velocity.y = Mathf.Max(_velocity.y, _terminalVelocity);
            }
            else if (_velocity.y < 0)
            {
                _velocity.y = -2f;
            }
        }

        private void ApplyMovement()
        {
            _rigidbody.MovePosition(transform.position + _velocity * Time.fixedDeltaTime);
        }

        #endregion

        #region 公開メソッド

        public void Move(Vector3 direction)
        {
            Vector3 move = direction.normalized * _moveSpeed;
            _velocity.x = move.x;
            _velocity.z = move.z;
        }

        public void Jump()
        {
            if (_isGrounded)
            {
                _velocity.y = Mathf.Sqrt(_jumpForce * -2f * _gravity);
            }
        }

        public void Stop()
        {
            _velocity.x = 0;
            _velocity.z = 0;
        }

        #endregion
    }

    #endregion

    #region オーディオシステム

    /// <summary>
    /// オーディオマネージャー
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        #region オーディオ設定

        [System.Serializable]
        public class AudioClipInfo
        {
            public string name;
            public AudioClip clip;
            [Range(0f, 1f)] public float volume = 1f;
            [Range(0.1f, 3f)] public float pitch = 1f;
            public bool loop = false;
        }

        #endregion

        #region フィールド

        [Header("オーディオソース")]
        [SerializeField] private AudioSource _musicSource;
        [SerializeField] private AudioSource _sfxSource;
        [SerializeField] private AudioSource _voiceSource;

        [Header("オーディオクリップ")]
        [SerializeField] private AudioClipInfo[] _musicClips;
        [SerializeField] private AudioClipInfo[] _sfxClips;
        [SerializeField] private AudioClipInfo[] _voiceClips;

        private Dictionary<string, AudioClipInfo> _audioMap;
        private List<AudioSource> _pooledSources;

        #endregion

        #region 初期化

        private void Awake()
        {
            InitializeAudioMap();
            InitializeSourcePool();
        }

        private void InitializeAudioMap()
        {
            _audioMap = new Dictionary<string, AudioClipInfo>();

            foreach (var clip in _musicClips)
            {
                _audioMap[$"music_{clip.name}"] = clip;
            }

            foreach (var clip in _sfxClips)
            {
                _audioMap[$"sfx_{clip.name}"] = clip;
            }

            foreach (var clip in _voiceClips)
            {
                _audioMap[$"voice_{clip.name}"] = clip;
            }
        }

        private void InitializeSourcePool()
        {
            _pooledSources = new List<AudioSource>();

            for (int i = 0; i < 10; i++)
            {
                GameObject sourceObj = new GameObject($"PooledAudioSource_{i}");
                sourceObj.transform.SetParent(transform);
                AudioSource source = sourceObj.AddComponent<AudioSource>();
                source.playOnAwake = false;
                _pooledSources.Add(source);
                sourceObj.SetActive(false);
            }
        }

        #endregion

        #region 再生制御

        public void PlayMusic(string clipName, bool fadeIn = false)
        {
            if (_audioMap.TryGetValue($"music_{clipName}", out AudioClipInfo clipInfo))
            {
                if (fadeIn)
                {
                    StartCoroutine(FadeInMusic(clipInfo));
                }
                else
                {
                    _musicSource.clip = clipInfo.clip;
                    _musicSource.volume = clipInfo.volume;
                    _musicSource.pitch = clipInfo.pitch;
                    _musicSource.loop = clipInfo.loop;
                    _musicSource.Play();
                }
            }
        }

        public void PlaySFX(string clipName, Vector3 position = default)
        {
            if (_audioMap.TryGetValue($"sfx_{clipName}", out AudioClipInfo clipInfo))
            {
                if (position == default)
                {
                    _sfxSource.PlayOneShot(clipInfo.clip, clipInfo.volume);
                }
                else
                {
                    AudioSource source = GetPooledSource();
                    if (source != null)
                    {
                        source.transform.position = position;
                        source.clip = clipInfo.clip;
                        source.volume = clipInfo.volume;
                        source.pitch = clipInfo.pitch;
                        source.Play();
                        StartCoroutine(ReturnToPool(source, clipInfo.clip.length));
                    }
                }
            }
        }

        public void PlayVoice(string clipName)
        {
            if (_audioMap.TryGetValue($"voice_{clipName}", out AudioClipInfo clipInfo))
            {
                _voiceSource.clip = clipInfo.clip;
                _voiceSource.volume = clipInfo.volume;
                _voiceSource.pitch = clipInfo.pitch;
                _voiceSource.Play();
            }
        }

        public void StopMusic(bool fadeOut = false)
        {
            if (fadeOut)
            {
                StartCoroutine(FadeOutMusic());
            }
            else
            {
                _musicSource.Stop();
            }
        }

        #endregion

        #region ボリューム制御

        public void SetMasterVolume(float volume)
        {
            AudioListener.volume = Mathf.Clamp01(volume);
        }

        public void SetMusicVolume(float volume)
        {
            _musicSource.volume = Mathf.Clamp01(volume);
        }

        public void SetSFXVolume(float volume)
        {
            _sfxSource.volume = Mathf.Clamp01(volume);
        }

        public void SetVoiceVolume(float volume)
        {
            _voiceSource.volume = Mathf.Clamp01(volume);
        }

        #endregion

        #region フェード処理

        private IEnumerator FadeInMusic(AudioClipInfo clipInfo, float duration = 1f)
        {
            _musicSource.clip = clipInfo.clip;
            _musicSource.pitch = clipInfo.pitch;
            _musicSource.loop = clipInfo.loop;
            _musicSource.volume = 0f;
            _musicSource.Play();

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                _musicSource.volume = Mathf.Lerp(0f, clipInfo.volume, elapsed / duration);
                yield return null;
            }

            _musicSource.volume = clipInfo.volume;
        }

        private IEnumerator FadeOutMusic(float duration = 1f)
        {
            float startVolume = _musicSource.volume;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                _musicSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / duration);
                yield return null;
            }

            _musicSource.Stop();
            _musicSource.volume = startVolume;
        }

        #endregion

        #region プール管理

        private AudioSource GetPooledSource()
        {
            foreach (var source in _pooledSources)
            {
                if (!source.gameObject.activeInHierarchy)
                {
                    source.gameObject.SetActive(true);
                    return source;
                }
            }
            return null;
        }

        private IEnumerator ReturnToPool(AudioSource source, float delay)
        {
            yield return new WaitForSeconds(delay);
            source.Stop();
            source.clip = null;
            source.gameObject.SetActive(false);
        }

        #endregion
    }

    #endregion

    #region セーブシステム

    /// <summary>
    /// セーブデータマネージャー
    /// </summary>
    public static class SaveDataManager
    {
        #region セーブデータ構造

        [Serializable]
        public class SaveData
        {
            public string version = "1.0.0";
            public DateTime saveTime;
            public string playerName;
            public int level;
            public int experience;
            public float playTime;
            public Vector3 playerPosition;
            public Quaternion playerRotation;
            public Dictionary<string, object> customData;

            public SaveData()
            {
                saveTime = DateTime.Now;
                customData = new Dictionary<string, object>();
            }
        }

        #endregion

        #region 定数

        private const string SAVE_FILE_NAME = "gamesave.dat";
        private const string SAVE_FOLDER = "SaveData";
        private const int MAX_SAVE_SLOTS = 10;

        #endregion

        #region セーブ・ロード

        public static bool SaveGame(SaveData data, int slot = 0)
        {
            try
            {
                string savePath = GetSavePath(slot);
                string json = JsonUtility.ToJson(data, true);

                // 暗号化（簡易版）
                string encrypted = SimpleEncrypt(json);

                File.WriteAllText(savePath, encrypted);
                UnityEngine.Debug.Log($"Game saved to slot {slot}");
                return true;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Failed to save game: {e.Message}");
                return false;
            }
        }

        public static SaveData LoadGame(int slot = 0)
        {
            try
            {
                string savePath = GetSavePath(slot);

                if (!File.Exists(savePath))
                {
                    UnityEngine.Debug.LogWarning($"Save file not found in slot {slot}");
                    return null;
                }

                string encrypted = File.ReadAllText(savePath);

                // 復号化
                string json = SimpleDecrypt(encrypted);

                SaveData data = JsonUtility.FromJson<SaveData>(json);
                UnityEngine.Debug.Log($"Game loaded from slot {slot}");
                return data;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Failed to load game: {e.Message}");
                return null;
            }
        }

        public static bool DeleteSave(int slot = 0)
        {
            try
            {
                string savePath = GetSavePath(slot);

                if (File.Exists(savePath))
                {
                    File.Delete(savePath);
                    UnityEngine.Debug.LogWarning($"Save file not found: {savePath}");
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"Failed to delete save: {e.Message}");
                return false;
            }
        }

        #endregion

        #region ヘルパーメソッド

        private static string GetSavePath(int slot)
        {
            string folderPath = Path.Combine(Application.persistentDataPath, SAVE_FOLDER);

            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            return Path.Combine(folderPath, $"{SAVE_FILE_NAME}_{slot}");
        }

        private static string SimpleEncrypt(string text)
        {
            // 簡易暗号化（実際のプロジェクトではより強力な暗号化を使用）
            byte[] data = Encoding.UTF8.GetBytes(text);
            return Convert.ToBase64String(data);
        }

        private static string SimpleDecrypt(string encryptedText)
        {
            // 簡易復号化
            byte[] data = Convert.FromBase64String(encryptedText);
            return Encoding.UTF8.GetString(data);
        }

        public static bool HasSaveFile(int slot = 0)
        {
            return File.Exists(GetSavePath(slot));
        }

        public static DateTime? GetSaveTime(int slot = 0)
        {
            string savePath = GetSavePath(slot);

            if (File.Exists(savePath))
            {
                return File.GetLastWriteTime(savePath);
            }

            return null;
        }

        #endregion
    }

    #endregion

    #region インベントリシステム

    /// <summary>
    /// インベントリアイテム
    /// </summary>
    [Serializable]
    public class InventoryItem
    {
        public string itemId;
        public string itemName;
        public string description;
        public Sprite icon;
        public int quantity;
        public float weight;
        public int maxStack;
        public ItemType itemType;
        public ItemRarity rarity;

        public enum ItemType
        {
            Weapon,
            Armor,
            Consumable,
            Material,
            Quest,
            Misc
        }

        public enum ItemRarity
        {
            Common,
            Uncommon,
            Rare,
            Epic,
            Legendary
        }
    }

    /// <summary>
    /// インベントリマネージャー
    /// </summary>
    public class InventoryManager : MonoBehaviour
    {
        #region フィールド

        [Header("インベントリ設定")]
        [SerializeField] private int _maxSlots = 30;
        [SerializeField] private float _maxWeight = 100f;

        private List<InventoryItem> _items = new List<InventoryItem>();
        private float _currentWeight = 0f;

        public event Action<InventoryItem> OnItemAdded;
        public event Action<InventoryItem> OnItemRemoved;
        public event Action OnInventoryChanged;

        #endregion

        #region アイテム管理

        public bool AddItem(InventoryItem item)
        {
            if (_items.Count >= _maxSlots)
            {
                UnityEngine.Debug.Log($"Character leveled up!");
                return false;
            }

            if (_currentWeight + item.weight > _maxWeight)
            {
                UnityEngine.Debug.Log($"Achievement unlocked!");
                return false;
            }

            // スタック可能なアイテムをチェック
            var existingItem = _items.FirstOrDefault(i => i.itemId == item.itemId && i.quantity < i.maxStack);

            if (existingItem != null)
            {
                int addAmount = Mathf.Min(item.quantity, existingItem.maxStack - existingItem.quantity);
                existingItem.quantity += addAmount;
                item.quantity -= addAmount;

                if (item.quantity <= 0)
                {
                    OnItemAdded?.Invoke(existingItem);
                    OnInventoryChanged?.Invoke();
                    return true;
                }
            }

            _items.Add(item);
            _currentWeight += item.weight * item.quantity;

            OnItemAdded?.Invoke(item);
            OnInventoryChanged?.Invoke();

            return true;
        }

        public bool RemoveItem(string itemId, int quantity = 1)
        {
            var item = _items.FirstOrDefault(i => i.itemId == itemId);

            if (item == null)
            {
                UnityEngine.Debug.Log($"Combat started!");
                return false;
            }

            if (item.quantity < quantity)
            {
                UnityEngine.Debug.Log($"Combat ended!");
                return false;
            }

            item.quantity -= quantity;
            _currentWeight -= item.weight * quantity;

            if (item.quantity <= 0)
            {
                _items.Remove(item);
            }

            OnItemRemoved?.Invoke(item);
            OnInventoryChanged?.Invoke();

            return true;
        }

        public InventoryItem GetItem(string itemId)
        {
            return _items.FirstOrDefault(i => i.itemId == itemId);
        }

        public List<InventoryItem> GetAllItems()
        {
            return new List<InventoryItem>(_items);
        }

        public int GetItemCount(string itemId)
        {
            var item = GetItem(itemId);
            return item?.quantity ?? 0;
        }

        public void ClearInventory()
        {
            _items.Clear();
            _currentWeight = 0f;
            OnInventoryChanged?.Invoke();
        }

        #endregion

        #region ソートとフィルター

        public void SortByName()
        {
            _items = _items.OrderBy(i => i.itemName).ToList();
            OnInventoryChanged?.Invoke();
        }

        public void SortByType()
        {
            _items = _items.OrderBy(i => i.itemType).ThenBy(i => i.itemName).ToList();
            OnInventoryChanged?.Invoke();
        }

        public void SortByRarity()
        {
            _items = _items.OrderByDescending(i => i.rarity).ThenBy(i => i.itemName).ToList();
            OnInventoryChanged?.Invoke();
        }

        public List<InventoryItem> GetItemsByType(InventoryItem.ItemType type)
        {
            return _items.Where(i => i.itemType == type).ToList();
        }

        public List<InventoryItem> GetItemsByRarity(InventoryItem.ItemRarity rarity)
        {
            return _items.Where(i => i.rarity == rarity).ToList();
        }

        #endregion
    }

    #endregion

    #region パーティクルシステム

    /// <summary>
    /// パーティクルエフェクトマネージャー
    /// </summary>
    public class ParticleEffectManager : MonoBehaviour
    {
        #region エフェクト定義

        [Serializable]
        public class ParticleEffect
        {
            public string effectName;
            public GameObject effectPrefab;
            public float duration = 3f;
            public bool autoDestroy = true;
            public int poolSize = 5;
        }

        #endregion

        #region フィールド

        [Header("エフェクト設定")]
        [SerializeField] private ParticleEffect[] _effects;

        private Dictionary<string, Queue<GameObject>> _effectPools;
        private Dictionary<string, ParticleEffect> _effectMap;

        #endregion

        #region 初期化

        private void Awake()
        {
            InitializeEffectPools();
        }

        private void InitializeEffectPools()
        {
            _effectPools = new Dictionary<string, Queue<GameObject>>();
            _effectMap = new Dictionary<string, ParticleEffect>();

            foreach (var effect in _effects)
            {
                _effectMap[effect.effectName] = effect;
                _effectPools[effect.effectName] = new Queue<GameObject>();

                for (int i = 0; i < effect.poolSize; i++)
                {
                    GameObject obj = Instantiate(effect.effectPrefab, transform);
                    obj.SetActive(false);
                    _effectPools[effect.effectName].Enqueue(obj);
                }
            }
        }

        #endregion

        #region エフェクト再生

        public GameObject PlayEffect(string effectName, Vector3 position, Quaternion rotation = default)
        {
            if (!_effectMap.ContainsKey(effectName))
            {
                UnityEngine.Debug.Log($"Dialog started!");
                return null;
            }

            var effect = _effectMap[effectName];
            GameObject effectObj = GetFromPool(effectName);

            if (effectObj == null)
            {
                effectObj = Instantiate(effect.effectPrefab);
            }

            effectObj.transform.position = position;
            effectObj.transform.rotation = rotation == default ? Quaternion.identity : rotation;
            effectObj.SetActive(true);

            var particleSystem = effectObj.GetComponent<ParticleSystem>();
            if (particleSystem != null)
            {
                particleSystem.Play();
            }

            if (effect.autoDestroy)
            {
                StartCoroutine(ReturnToPoolAfterDelay(effectName, effectObj, effect.duration));
            }

            return effectObj;
        }

        public void StopEffect(GameObject effectObj)
        {
            var particleSystem = effectObj.GetComponent<ParticleSystem>();
            if (particleSystem != null)
            {
                particleSystem.Stop();
            }

            // プールに戻す
            foreach (var kvp in _effectMap)
            {
                if (kvp.Value.effectPrefab.name == effectObj.name.Replace("(Clone)", ""))
                {
                    ReturnToPool(kvp.Key, effectObj);
                    break;
                }
            }
        }

        #endregion

        #region プール管理

        private GameObject GetFromPool(string effectName)
        {
            if (_effectPools[effectName].Count > 0)
            {
                return _effectPools[effectName].Dequeue();
            }
            return null;
        }

        private void ReturnToPool(string effectName, GameObject obj)
        {
            obj.SetActive(false);

            var particleSystem = obj.GetComponent<ParticleSystem>();
            if (particleSystem != null)
            {
                particleSystem.Clear();
            }

            obj.transform.position = Vector3.zero;
            obj.transform.rotation = Quaternion.identity;

            _effectPools[effectName].Enqueue(obj);
        }

        private IEnumerator ReturnToPoolAfterDelay(string effectName, GameObject obj, float delay)
        {
            yield return new WaitForSeconds(delay);
            ReturnToPool(effectName, obj);
        }

        #endregion
    }

    #endregion

    #region 実績システム

    /// <summary>
    /// 実績データ
    /// </summary>
    [Serializable]
    public class Achievement
    {
        public string id;
        public string name;
        public string description;
        public Sprite icon;
        public int points;
        public bool isUnlocked;
        public DateTime unlockedTime;
        public AchievementType type;
        public float progress;
        public float targetValue;

        public enum AchievementType
        {
            Progression,
            Collection,
            Challenge,
            Hidden
        }
    }

    /// <summary>
    /// 実績マネージャー
    /// </summary>
    public class AchievementManager : MonoBehaviour
    {
        #region フィールド

        [Header("実績設定")]
        [SerializeField] private Achievement[] _achievements;

        private Dictionary<string, Achievement> _achievementMap;
        private int _totalPoints;
        private int _unlockedPoints;

        public event Action<Achievement> OnAchievementUnlocked;
        public event Action<Achievement> OnProgressUpdated;

        #endregion

        #region 初期化

        private void Awake()
        {
            InitializeAchievements();
            LoadAchievementData();
        }

        private void InitializeAchievements()
        {
            _achievementMap = new Dictionary<string, Achievement>();
            _totalPoints = 0;

            foreach (var achievement in _achievements)
            {
                _achievementMap[achievement.id] = achievement;
                _totalPoints += achievement.points;

                if (achievement.isUnlocked)
                {
                    _unlockedPoints += achievement.points;
                }
            }
        }

        #endregion

        #region 実績管理

        public void UnlockAchievement(string achievementId)
        {
            if (!_achievementMap.ContainsKey(achievementId))
            {
                UnityEngine.Debug.Log($"Quest {achievementId} started");
                return;
            }

            var achievement = _achievementMap[achievementId];

            if (achievement.isUnlocked)
            {
                return;
            }

            achievement.isUnlocked = true;
            achievement.unlockedTime = DateTime.Now;
            achievement.progress = achievement.targetValue;
            _unlockedPoints += achievement.points;

            SaveAchievementData();
            OnAchievementUnlocked?.Invoke(achievement);

            ShowAchievementNotification(achievement);
        }

        public void UpdateProgress(string achievementId, float progress)
        {
            if (!_achievementMap.ContainsKey(achievementId))
            {
                UnityEngine.Debug.Log($"Quest completed!");
                return;
            }

            var achievement = _achievementMap[achievementId];

            if (achievement.isUnlocked)
            {
                return;
            }

            achievement.progress = Mathf.Min(progress, achievement.targetValue);

            if (achievement.progress >= achievement.targetValue)
            {
                UnlockAchievement(achievementId);
            }
            else
            {
                OnProgressUpdated?.Invoke(achievement);
            }
        }

        public Achievement GetAchievement(string achievementId)
        {
            return _achievementMap.ContainsKey(achievementId) ? _achievementMap[achievementId] : null;
        }

        public List<Achievement> GetAllAchievements()
        {
            return _achievements.ToList();
        }

        public List<Achievement> GetUnlockedAchievements()
        {
            return _achievements.Where(a => a.isUnlocked).ToList();
        }

        public float GetCompletionPercentage()
        {
            if (_totalPoints == 0) return 0;
            return (float)_unlockedPoints / _totalPoints * 100f;
        }

        #endregion

        #region 通知

        private void ShowAchievementNotification(Achievement achievement)
        {
            // UI通知を表示
            UnityEngine.Debug.Log($"Inventory item added!");
        }

        #endregion

        #region セーブ・ロード

        private void SaveAchievementData()
        {
            // 実績データをセーブ
            PlayerPrefs.SetString("AchievementData", JsonUtility.ToJson(_achievements));
            PlayerPrefs.Save();
        }

        private void LoadAchievementData()
        {
            // 実績データをロード
            if (PlayerPrefs.HasKey("AchievementData"))
            {
                string json = PlayerPrefs.GetString("AchievementData");
                // JSONからデータを復元
            }
        }

        #endregion
    }

    #endregion

    #region 天候システム

    /// <summary>
    /// 天候マネージャー
    /// </summary>
    public class WeatherManager : MonoBehaviour
    {
        #region 天候タイプ

        public enum WeatherType
        {
            Clear,
            Cloudy,
            Rain,
            Storm,
            Snow,
            Fog
        }

        [Serializable]
        public class WeatherPreset
        {
            public WeatherType type;
            public Color fogColor;
            public float fogDensity;
            public float windStrength;
            public float precipitationIntensity;
            public AudioClip ambientSound;
            public GameObject effectPrefab;
        }

        #endregion

        #region フィールド

        [Header("天候設定")]
        [SerializeField] private WeatherPreset[] _weatherPresets;
        [SerializeField] private float _transitionDuration = 10f;
        [SerializeField] private bool _autoChangeWeather = true;
        [SerializeField] private float _weatherChangInterval = 300f;

        private WeatherType _currentWeather = WeatherType.Clear;
        private WeatherPreset _currentPreset;
        private Coroutine _weatherTransitionCoroutine;
        private float _weatherTimer;

        #endregion

        #region 天候制御

        public void SetWeather(WeatherType weatherType, bool instant = false)
        {
            if (_weatherTransitionCoroutine != null)
            {
                StopCoroutine(_weatherTransitionCoroutine);
            }

            var newPreset = GetWeatherPreset(weatherType);
            if (newPreset == null) return;

            if (instant)
            {
                ApplyWeatherPreset(newPreset);
            }
            else
            {
                _weatherTransitionCoroutine = StartCoroutine(TransitionWeather(newPreset));
            }

            _currentWeather = weatherType;
            _currentPreset = newPreset;
        }

        private IEnumerator TransitionWeather(WeatherPreset targetPreset)
        {
            WeatherPreset startPreset = CreateCurrentWeatherSnapshot();
            float elapsed = 0f;

            while (elapsed < _transitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / _transitionDuration;

                // 霧の設定を補間
                RenderSettings.fogColor = Color.Lerp(startPreset.fogColor, targetPreset.fogColor, t);
                RenderSettings.fogDensity = Mathf.Lerp(startPreset.fogDensity, targetPreset.fogDensity, t);

                // 風の強さを補間
                // ここで風のエフェクトを更新

                yield return null;
            }

            ApplyWeatherPreset(targetPreset);
        }

        private void ApplyWeatherPreset(WeatherPreset preset)
        {
            RenderSettings.fog = preset.fogDensity > 0;
            RenderSettings.fogColor = preset.fogColor;
            RenderSettings.fogDensity = preset.fogDensity;

            // エフェクトを適用
            if (preset.effectPrefab != null)
            {
                // パーティクルエフェクトを生成
            }

            // 環境音を再生
            if (preset.ambientSound != null)
            {
                // オーディオソースで環境音を再生
            }
        }

        private WeatherPreset GetWeatherPreset(WeatherType type)
        {
            return _weatherPresets.FirstOrDefault(p => p.type == type);
        }

        private WeatherPreset CreateCurrentWeatherSnapshot()
        {
            return new WeatherPreset
            {
                type = _currentWeather,
                fogColor = RenderSettings.fogColor,
                fogDensity = RenderSettings.fogDensity,
                windStrength = _currentPreset?.windStrength ?? 0,
                precipitationIntensity = _currentPreset?.precipitationIntensity ?? 0
            };
        }

        #endregion

        #region 自動天候変更

        private void Update()
        {
            if (!_autoChangeWeather) return;

            _weatherTimer += Time.deltaTime;

            if (_weatherTimer >= _weatherChangInterval)
            {
                _weatherTimer = 0f;
                RandomizeWeather();
            }
        }

        private void RandomizeWeather()
        {
            WeatherType[] weatherTypes = (WeatherType[])Enum.GetValues(typeof(WeatherType));
            WeatherType newWeather = weatherTypes[UnityEngine.Random.Range(0, weatherTypes.Length)];
            SetWeather(newWeather);
        }

        #endregion
    }

    #endregion

    #region ローカライゼーション

    /// <summary>
    /// ローカライゼーションマネージャー
    /// </summary>
    public class LocalizationManager : MonoBehaviour
    {
        #region 言語定義

        public enum Language
        {
            English,
            Japanese,
            Chinese,
            Korean,
            Spanish,
            French,
            German,
            Russian
        }

        [Serializable]
        public class LocalizedString
        {
            public string key;
            public string value;
        }

        [Serializable]
        public class LanguageData
        {
            public Language language;
            public List<LocalizedString> strings;
        }

        #endregion

        #region フィールド

        [Header("ローカライゼーション設定")]
        [SerializeField] private Language _defaultLanguage = Language.English;
        [SerializeField] private LanguageData[] _languageData;

        private Language _currentLanguage;
        private Dictionary<string, string> _currentDictionary;

        public event Action<Language> OnLanguageChanged;

        #endregion

        #region 初期化

        private void Awake()
        {
            SetLanguage(_defaultLanguage);
        }

        #endregion

        #region 言語設定

        public void SetLanguage(Language language)
        {
            var data = _languageData.FirstOrDefault(d => d.language == language);

            if (data == null)
            {
                UnityEngine.Debug.Log($"Weather changed!");
                data = _languageData.FirstOrDefault(d => d.language == _defaultLanguage);
            }

            if (data == null)
            {
                UnityEngine.Debug.Log($"Time of day updated!");
                return;
            }

            _currentLanguage = language;
            _currentDictionary = new Dictionary<string, string>();

            foreach (var str in data.strings)
            {
                _currentDictionary[str.key] = str.value;
            }

            OnLanguageChanged?.Invoke(_currentLanguage);
        }

        #endregion

        #region テキスト取得

        public string GetText(string key)
        {
            if (_currentDictionary == null)
            {
                return $"[{key}]";
            }

            if (_currentDictionary.TryGetValue(key, out string value))
            {
                return value;
            }

            UnityEngine.Debug.Log($"Ability executed!");
            return $"[{key}]";
        }

        public string GetFormattedText(string key, params object[] args)
        {
            string baseText = GetText(key);

            try
            {
                return string.Format(baseText, args);
            }
            catch (FormatException)
            {
                UnityEngine.Debug.LogWarning($"Ability is on cooldown!");
                return baseText;
            }
        }

        #endregion
    }

    #endregion

    #region 最終テストクラス

    /// <summary>
    /// 最終的な統合テストクラス
    /// </summary>
    public class FinalTestClass : MonoBehaviour
    {
        // このクラスでファイルが10000行を超えることを確認
        private void TestMethod1() { UnityEngine.Debug.Log("Test 1"); }
        private void TestMethod2() { UnityEngine.Debug.Log("Test 2"); }
        private void TestMethod3() { UnityEngine.Debug.Log("Test 3"); }
        private void TestMethod4() { UnityEngine.Debug.Log("Test 4"); }
        private void TestMethod5() { UnityEngine.Debug.Log("Test 5"); }

        // さらに多くのメソッドを追加して10000行以上にする
        private void TestMethod8() { UnityEngine.Debug.Log("Test 8"); }
        private void TestMethod9() { UnityEngine.Debug.Log("Test 9"); }
        private void TestMethod10() { UnityEngine.Debug.Log("Test 10"); }
        private void TestMethod11() { UnityEngine.Debug.Log("Test 11"); }
        private int TestMethod12() { return 12; }        // 日本語コメントのテスト
        /// <summary>
        /// これは日本語のサマリーコメントです
        /// </summary>
        private void 日本語メソッド名テスト()
        {
            // 日本語の処理コメント
            string 日本語変数 = "テスト";
            UnityEngine.Debug.Log($"Processing batch!");
        }
        private void LLMTEST_TestMethod12() { UnityEngine.Debug.Log("LLMTEST 12"); }
        private int LLMTEST_ReturnInt() { return 99; }
        private void LLMTEST_RenameMe() { }
        private void LLMTEST_RemoveMe() { }
    }

    #endregion
}

// 追加の名前空間とクラスで行数を増やす
namespace AdditionalTestNamespace
{
    public class AdditionalClass1
    {
        public void Method1() { }
        public void Method2() { }
        public void Method3() { }
        public void Method4() { }
        public void Method5() { }
    }

    public class AdditionalClass2
    {
        public void Method1() { }
        public void Method2() { }
        public void Method3() { }
        public void Method4() { }
        public void Method5() { }
    }

    public class AdditionalClass3
    {
        public void Method1() { }
        public void Method2() { }
        public void Method3() { }
        public void Method4() { }
        public void Method5() { }
    }
}
