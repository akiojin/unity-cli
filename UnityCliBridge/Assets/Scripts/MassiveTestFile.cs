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

namespace MassiveTestNamespace
{
    // 超大規模テストファイル（5000行以上）
    // このファイルは様々な複雑なパターンと構造を含みます
    
    #region Interfaces
    
    public interface IDataProcessor
    {
        void ProcessData(object data);
        Task ProcessDataAsync(object data);
        bool ValidateData(object data);
        string GetProcessorName();
    }
    
    public interface IDataValidator
    {
        bool Validate(object data);
        List<string> GetValidationErrors();
        void ClearValidationErrors();
    }
    
    public interface IDataTransformer
    {
        T Transform<T>(object input) where T : class;
        object TransformGeneric(object input, Type targetType);
        bool CanTransform(Type sourceType, Type targetType);
    }
    
    public interface IEventDispatcher
    {
        void RegisterListener(string eventName, Action<object> callback);
        void UnregisterListener(string eventName, Action<object> callback);
        void DispatchEvent(string eventName, object eventData);
        void ClearAllListeners();
    }
    
    public interface IResourceManager
    {
        T LoadResource<T>(string path) where T : UnityEngine.Object;
        void UnloadResource(string path);
        Task<T> LoadResourceAsync<T>(string path) where T : UnityEngine.Object;
        void PreloadResources(string[] paths);
        void ClearCache();
    }
    
    #endregion
    
    #region Enumerations
    
    public enum ProcessingState
    {
        Idle,
        Initializing,
        Processing,
        Paused,
        Completed,
        Failed,
        Cancelled
    }
    
    public enum DataType
    {
        Integer,
        Float,
        String,
        Boolean,
        Vector2,
        Vector3,
        Quaternion,
        Color,
        Texture,
        AudioClip,
        Mesh,
        Material,
        GameObject,
        Component,
        ScriptableObject
    }
    
    public enum LogLevel
    {
        Verbose,
        Debug,
        Info,
        Warning,
        Error,
        Fatal
    }
    
    public enum NetworkState
    {
        Disconnected,
        Connecting,
        Connected,
        Authenticating,
        Authenticated,
        Disconnecting,
        Error,
        Reconnecting,
        Suspended,
        Resuming
    }
    #endregion
    
    #region Abstract Classes
    
    public abstract class BaseProcessor : IDataProcessor
    {
        protected string processorName;
        protected ProcessingState currentState;
        protected List<string> errorMessages;
        protected Dictionary<string, object> metadata;
        
        public BaseProcessor(string name)
        {
            processorName = name;
            currentState = ProcessingState.Idle;
            errorMessages = new List<string>();
            metadata = new Dictionary<string, object>();
        }
        
        public abstract void ProcessData(object data);
        
        public virtual async Task ProcessDataAsync(object data)
        {
            await Task.Run(() => ProcessData(data));
        }
        
        public virtual bool ValidateData(object data)
        {
            return data != null;
        }
        
        public string GetProcessorName()
        {
            return processorName;
        }
        
        protected virtual void LogMessage(string message, LogLevel level = LogLevel.Info)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string logEntry = $"[{timestamp}] [{level}] [{processorName}] {message}";
            
            switch (level)
            {
                case LogLevel.Error:
                case LogLevel.Fatal:
                    Debug.LogError(logEntry);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(logEntry);
                    break;
                default:
                    Debug.Log(logEntry);
                    break;
            }
        }
    }
    
    public abstract class DataContainer<T> where T : class
    {
        protected T data;
        protected bool isDirty;
        protected DateTime lastModified;
        protected string containerID;
        
        public DataContainer()
        {
            containerID = Guid.NewGuid().ToString();
            lastModified = DateTime.Now;
            isDirty = false;
        }
        
        public virtual T GetData()
        {
            return data;
        }
        
        public virtual void SetData(T newData)
        {
            data = newData;
            isDirty = true;
            lastModified = DateTime.Now;
        }
        
        public abstract bool Validate();
        public abstract void Reset();
        public abstract DataContainer<T> Clone();
    }
    
    #endregion
    
    #region Concrete Classes
    
    [Serializable]
    public class GameData : DataContainer<Dictionary<string, object>>
    {
        [SerializeField, Tooltip("Auto-generated tooltip")] private int version;
        [SerializeField, Tooltip("Auto-generated tooltip")] private string saveFileName;
        [SerializeField, Tooltip("Auto-generated tooltip")] private bool autoSave;
        [SerializeField, Tooltip("Auto-generated tooltip")] private float autoSaveInterval;
        
        private Coroutine autoSaveCoroutine;
        
        public GameData() : base()
        {
            data = new Dictionary<string, object>();
            version = 1;
            saveFileName = "gamedata.save";
            autoSave = true;
            autoSaveInterval = 60f;
        }
        
        public override bool Validate()
        {
            if (data == null) return false;
            if (string.IsNullOrEmpty(saveFileName)) return false;
            if (autoSaveInterval <= 0) return false;
            return true;
        }
        
        public override void Reset()
        {
            data.Clear();
            isDirty = false;
            lastModified = DateTime.Now;
        }
        
        public override DataContainer<Dictionary<string, object>> Clone()
        {
            var clone = new GameData();
            clone.version = this.version;
            clone.saveFileName = this.saveFileName;
            clone.autoSave = this.autoSave;
            clone.autoSaveInterval = this.autoSaveInterval;
            clone.data = new Dictionary<string, object>(this.data);
            return clone;
        }
        
        public void SaveToFile()
        {
            try
            {
                string json = JsonUtility.ToJson(this);
                File.WriteAllText(saveFileName, json);
                isDirty = false;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save game data: {e.Message}");
            }
        }
        
        public void LoadFromFile()
        {
            try
            {
                if (File.Exists(saveFileName))
                {
                    string json = File.ReadAllText(saveFileName);
                    JsonUtility.FromJsonOverwrite(json, this);
                    isDirty = false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load game data: {e.Message}");
            }
        }
    }
    
    public class NetworkManager : MonoBehaviour, IEventDispatcher
    {
        [Header("Network Configuration")]
        [SerializeField, Tooltip("Auto-generated tooltip")] private string serverAddress = "localhost";
        [SerializeField] private int serverPort = 7777;
        [SerializeField] private float connectionTimeout = 30f;
        [SerializeField] private int maxRetryAttempts = 3;
        
        [Header("Network State")]
        [SerializeField] private NetworkState currentState;
        [SerializeField] private bool isHost;
        [SerializeField] private int connectedClients;
        
        private Dictionary<string, List<Action<object>>> eventListeners;
        private Queue<NetworkMessage> messageQueue;
        private object networkLock = new object();
        
        public class NetworkMessage
        {
            public string messageType;
            public object payload;
            public DateTime timestamp;
            public string senderID;
            public string receiverID;
            
            public NetworkMessage(string type, object data)
            {
                messageType = type;
                payload = data;
                timestamp = DateTime.Now;
                senderID = SystemInfo.deviceUniqueIdentifier;
            }
        }
        
        void Awake()
        {
            eventListeners = new Dictionary<string, List<Action<object>>>();
            messageQueue = new Queue<NetworkMessage>();
            currentState = NetworkState.Disconnected;
        }
        
        void Start()
        {
            StartCoroutine(NetworkUpdateLoop());
            StartCoroutine(MessageProcessingLoop());
        }
        
        public void Connect()
        {
            if (currentState != NetworkState.Disconnected) return;
            
            StartCoroutine(ConnectToServer());
        }
        
        private IEnumerator ConnectToServer()
        {
            currentState = NetworkState.Connecting;
            DispatchEvent("OnConnecting", null);
            
            float startTime = Time.time;
            int retryCount = 0;
            
            while (retryCount < maxRetryAttempts)
            {
                // Simulate connection attempt
                yield return new WaitForSeconds(2f);
                
                bool connectionSuccessful = UnityEngine.Random.Range(0f, 1f) > 0.3f;
                
                if (connectionSuccessful)
                {
                    currentState = NetworkState.Connected;
                    DispatchEvent("OnConnected", null);
                    yield break;
                }
                
                retryCount++;
                
                if (Time.time - startTime > connectionTimeout)
                {
                    currentState = NetworkState.Error;
                    DispatchEvent("OnConnectionTimeout", null);
                    yield break;
                }
                
                yield return new WaitForSeconds(1f);
            }
            
            currentState = NetworkState.Error;
            DispatchEvent("OnConnectionFailed", null);
        }
        
        public void Disconnect()
        {
            if (currentState == NetworkState.Disconnected) return;
            
            StartCoroutine(DisconnectFromServer());
        }
        
        private IEnumerator DisconnectFromServer()
        {
            currentState = NetworkState.Disconnecting;
            DispatchEvent("OnDisconnecting", null);
            
            yield return new WaitForSeconds(1f);
            
            currentState = NetworkState.Disconnected;
            DispatchEvent("OnDisconnected", null);
        }
        
        public void SendMessage(NetworkMessage message)
        {
            lock (networkLock)
            {
                messageQueue.Enqueue(message);
            }
        }
        
        private IEnumerator NetworkUpdateLoop()
        {
            while (true)
            {
                if (currentState == NetworkState.Connected)
                {
                    // Simulate network update
                    yield return new WaitForSeconds(0.1f);
                }
                else
                {
                    yield return new WaitForSeconds(1f);
                }
            }
        }
        
        private IEnumerator MessageProcessingLoop()
        {
            while (true)
            {
                if (messageQueue.Count > 0)
                {
                    NetworkMessage message = null;
                    
                    lock (networkLock)
                    {
                        if (messageQueue.Count > 0)
                        {
                            message = messageQueue.Dequeue();
                        }
                    }
                    
                    if (message != null)
                    {
                        ProcessMessage(message);
                    }
                }
                
                yield return new WaitForSeconds(0.05f);
            }
        }
        
        private void ProcessMessage(NetworkMessage message)
        {
            DispatchEvent($"OnMessage_{message.messageType}", message);
        }
        
        #region IEventDispatcher Implementation
        
        public void RegisterListener(string eventName, Action<object> callback)
        {
            if (!eventListeners.ContainsKey(eventName))
            {
                eventListeners[eventName] = new List<Action<object>>();
            }
            
            eventListeners[eventName].Add(callback);
        }
        
        public void UnregisterListener(string eventName, Action<object> callback)
        {
            if (eventListeners.ContainsKey(eventName))
            {
                eventListeners[eventName].Remove(callback);
            }
        }
        
        public void DispatchEvent(string eventName, object eventData)
        {
            if (eventListeners.ContainsKey(eventName))
            {
                foreach (var callback in eventListeners[eventName])
                {
                    callback?.Invoke(eventData);
                }
            }
        }
        
        public void ClearAllListeners()
        {
            eventListeners.Clear();
        }
        
        #endregion
    }
    
    [System.Serializable]
    public class PlayerData
    {
        public string playerID;
        public string playerName;
        public int level;
        public float experience;
        public int health;
        public int maxHealth;
        public int mana;
        public int maxMana;
        public Vector3 position;
        public Quaternion rotation;
        public List<string> inventory;
        public Dictionary<string, int> stats;
        public DateTime lastLoginTime;
        public float totalPlayTime;
        
        public PlayerData()
        {
            playerID = Guid.NewGuid().ToString();
            playerName = "Player";
            level = 1;
            experience = 0f;
            health = 100;
            maxHealth = 100;
            mana = 50;
            maxMana = 50;
            position = Vector3.zero;
            rotation = Quaternion.identity;
            inventory = new List<string>();
            stats = new Dictionary<string, int>
            {
                { "Strength", 10 },
                { "Dexterity", 10 },
                { "Intelligence", 10 },
                { "Vitality", 10 },
                { "Wisdom", 10 },
                { "Luck", 10 }
            };
            lastLoginTime = DateTime.Now;
            totalPlayTime = 0f;
        }
        
        public void LevelUp()
        {
            level++;
            maxHealth += 10;
            maxMana += 5;
            health = maxHealth;
            mana = maxMana;
            
            foreach (var key in stats.Keys.ToList())
            {
                stats[key] += UnityEngine.Random.Range(1, 3);
            }
        }
        
        public void AddExperience(float amount)
        {
            experience += amount;
            
            float requiredExp = GetRequiredExperience();
            while (experience >= requiredExp)
            {
                experience -= requiredExp;
                LevelUp();
                requiredExp = GetRequiredExperience();
            }
        }
        
        public float GetRequiredExperience()
        {
            return level * 100 * Mathf.Pow(1.5f, level - 1);
        }
        
        public void TakeDamage(int damage)
        {
            health = Mathf.Max(0, health - damage);
        }
        
        public void Heal(int amount)
        {
            health = Mathf.Min(maxHealth, health + amount);
        }
        
        public void UseMana(int amount)
        {
            mana = Mathf.Max(0, mana - amount);
        }
        
        public void RestoreMana(int amount)
        {
            mana = Mathf.Min(maxMana, mana + amount);
        }
        
        public bool IsAlive()
        {
            return health > 0;
        }
        
        public float GetHealthPercentage()
        {
            return (float)health / maxHealth;
        }
        
        public float GetManaPercentage()
        {
            return (float)mana / maxMana;
        }
    }
    
    public class GameManager : MonoBehaviour
    {
        private static GameManager instance;
        public static GameManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindObjectOfType<GameManager>();
                    if (instance == null)
                    {
                        GameObject go = new GameObject("GameManager");
                        instance = go.AddComponent<GameManager>();
                    }
                }
                return instance;
            }
        }
        
        [Header("Game Configuration")]
        [SerializeField] private string gameVersion = "1.0.0";
        [SerializeField] private bool debugMode = false;
        [SerializeField] private float targetFrameRate = 60f;
        
        [Header("Game State")]
        [SerializeField] private bool isGamePaused;
        [SerializeField] private bool isGameOver;
        [SerializeField] private float gameTime;
        [SerializeField] private int currentScore;
        [SerializeField] private int highScore;
        
        [Header("Player Management")]
        [SerializeField] public PlayerData currentPlayer;
        [SerializeField] private List<PlayerData> allPlayers;
        
        [Header("Resource Management")]
        [SerializeField] private Dictionary<string, UnityEngine.Object> loadedResources;
        [SerializeField] private List<string> preloadPaths;
        
        public UnityEvent OnGameStart;
        public UnityEvent OnGamePause;
        public UnityEvent OnGameResume;
        public UnityEvent OnGameOver;
        public UnityEvent<int> OnScoreChanged;
        public UnityEvent<PlayerData> OnPlayerDataChanged;
        
        private Coroutine gameLoopCoroutine;
        private Coroutine autoSaveCoroutine;
        
        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            
            instance = this;
            DontDestroyOnLoad(gameObject);
            
            InitializeGame();
        }
        
        void Start()
        {
            Application.targetFrameRate = (int)targetFrameRate;
            
            gameLoopCoroutine = StartCoroutine(GameLoop());
            autoSaveCoroutine = StartCoroutine(AutoSaveLoop());
            
            OnGameStart?.Invoke();
        }
        
        private void InitializeGame()
        {
            isGamePaused = false;
            isGameOver = false;
            gameTime = 0f;
            currentScore = 0;
            highScore = PlayerPrefs.GetInt("HighScore", 0);
            
            currentPlayer = new PlayerData();
            allPlayers = new List<PlayerData>();
            loadedResources = new Dictionary<string, UnityEngine.Object>();
            preloadPaths = new List<string>();
            
            OnGameStart = new UnityEvent();
            OnGamePause = new UnityEvent();
            OnGameResume = new UnityEvent();
            OnGameOver = new UnityEvent();
            OnScoreChanged = new UnityEvent<int>();
            OnPlayerDataChanged = new UnityEvent<PlayerData>();
        }
        
        private IEnumerator GameLoop()
        {
            while (!isGameOver)
            {
                if (!isGamePaused)
                {
                    gameTime += Time.deltaTime;
                    UpdateGame();
                }
                
                yield return null;
            }
        }
        
        private void UpdateGame()
        {
            // Game update logic
            if (currentPlayer != null && currentPlayer.IsAlive())
            {
                // Update player
                currentPlayer.totalPlayTime += Time.deltaTime;
                
                // Check for level up conditions
                if (UnityEngine.Random.Range(0f, 1f) < 0.001f)
                {
                    currentPlayer.AddExperience(UnityEngine.Random.Range(10f, 50f));
                    OnPlayerDataChanged?.Invoke(currentPlayer);
                }
                
                // Regenerate mana
                if (currentPlayer.mana < currentPlayer.maxMana)
                {
                    currentPlayer.RestoreMana(1);
                }
            }
        }
        
        private IEnumerator AutoSaveLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(60f);
                
                if (!isGamePaused && !isGameOver)
                {
                    SaveGame();
                }
            }
        }
        
        public void PauseGame()
        {
            if (!isGamePaused && !isGameOver)
            {
                isGamePaused = true;
                Time.timeScale = 0f;
                OnGamePause?.Invoke();
            }
        }
        
        public void ResumeGame()
        {
            if (isGamePaused && !isGameOver)
            {
                isGamePaused = false;
                Time.timeScale = 1f;
                OnGameResume?.Invoke();
            }
        }
        
        public void EndGame()
        {
            if (!isGameOver)
            {
                isGameOver = true;
                Time.timeScale = 0f;
                
                if (currentScore > highScore)
                {
                    highScore = currentScore;
                    PlayerPrefs.SetInt("HighScore", highScore);
                    PlayerPrefs.Save();
                }
                
                OnGameOver?.Invoke();
            }
        }
        
        public void AddScore(int points)
        {
            currentScore += points;
            OnScoreChanged?.Invoke(currentScore);
        }
        
        public void SaveGame()
        {
            try
            {
                GameData saveData = new GameData();
                saveData.SetData(new Dictionary<string, object>
                {
                    { "PlayerData", JsonUtility.ToJson(currentPlayer) },
                    { "GameTime", gameTime },
                    { "Score", currentScore },
                    { "SaveTime", DateTime.Now.ToString() }
                });
                
                saveData.SaveToFile();
                
                if (debugMode)
                {
                    Debug.Log("Game saved successfully");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save game: {e.Message}");
            }
        }
        
        public void LoadGame()
        {
            try
            {
                GameData saveData = new GameData();
                saveData.LoadFromFile();
                
                var data = saveData.GetData();
                if (data != null && data.ContainsKey("PlayerData"))
                {
                    string playerJson = data["PlayerData"] as string;
                    if (!string.IsNullOrEmpty(playerJson))
                    {
                        JsonUtility.FromJsonOverwrite(playerJson, currentPlayer);
                    }
                    
                    if (data.ContainsKey("GameTime"))
                    {
                        gameTime = Convert.ToSingle(data["GameTime"]);
                    }
                    
                    if (data.ContainsKey("Score"))
                    {
                        currentScore = Convert.ToInt32(data["Score"]);
                    }
                    
                    OnPlayerDataChanged?.Invoke(currentPlayer);
                    OnScoreChanged?.Invoke(currentScore);
                }
                
                if (debugMode)
                {
                    Debug.Log("Game loaded successfully");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load game: {e.Message}");
            }
        }
        
        public T LoadResource<T>(string path) where T : UnityEngine.Object
        {
            if (loadedResources.ContainsKey(path))
            {
                return loadedResources[path] as T;
            }
            
            T resource = Resources.Load<T>(path);
            if (resource != null)
            {
                loadedResources[path] = resource;
            }
            
            return resource;
        }
        
        public void UnloadResource(string path)
        {
            if (loadedResources.ContainsKey(path))
            {
                loadedResources.Remove(path);
            }
        }
        
        public void UnloadAllResources()
        {
            loadedResources.Clear();
            Resources.UnloadUnusedAssets();
            System.GC.Collect();
        }
    }
    
    public class AIController : MonoBehaviour
    {
        [Header("AI Configuration")]
        [SerializeField] private float detectionRange = 10f;
        [SerializeField] private float attackRange = 2f;
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float rotationSpeed = 10f;
        [SerializeField] private float attackCooldown = 1f;
        
        [Header("AI State")]
        [SerializeField] private AIState currentState;
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 patrolDestination;
        [SerializeField] private float lastAttackTime;
        
        public enum AIState
        {
            Idle,
            Patrolling,
            Chasing,
            Attacking,
            Fleeing,
            Dead
        }
        
        private Rigidbody rb;
        private Animator animator;
        private UnityEngine.AI.NavMeshAgent navAgent;
        private List<Transform> waypoints;
        private int currentWaypointIndex;
        
        void Start()
        {
            rb = GetComponent<Rigidbody>();
            animator = GetComponent<Animator>();
            navAgent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            
            waypoints = new List<Transform>();
            currentWaypointIndex = 0;
            currentState = AIState.Idle;
            
            StartCoroutine(AIBehaviorLoop());
        }
        
        private IEnumerator AIBehaviorLoop()
        {
            while (currentState != AIState.Dead)
            {
                switch (currentState)
                {
                    case AIState.Idle:
                        yield return HandleIdleState();
                        break;
                    case AIState.Patrolling:
                        yield return HandlePatrolState();
                        break;
                    case AIState.Chasing:
                        yield return HandleChaseState();
                        break;
                    case AIState.Attacking:
                        yield return HandleAttackState();
                        break;
                    case AIState.Fleeing:
                        yield return HandleFleeState();
                        break;
                }
                
                yield return new WaitForSeconds(0.1f);
            }
        }
        
        private IEnumerator HandleIdleState()
        {
            // Look for targets
            Collider[] colliders = Physics.OverlapSphere(transform.position, detectionRange);
            
            foreach (var collider in colliders)
            {
                if (collider.CompareTag("Player"))
                {
                    target = collider.transform;
                    currentState = AIState.Chasing;
                    yield break;
                }
            }
            
            // Start patrolling if no target found
            if (waypoints.Count > 0)
            {
                currentState = AIState.Patrolling;
            }
            
            yield return new WaitForSeconds(1f);
        }
        
        private IEnumerator HandlePatrolState()
        {
            if (waypoints.Count == 0)
            {
                currentState = AIState.Idle;
                yield break;
            }
            
            Transform waypoint = waypoints[currentWaypointIndex];
            navAgent.SetDestination(waypoint.position);
            
            while (Vector3.Distance(transform.position, waypoint.position) > 1f)
            {
                // Check for targets while patrolling
                Collider[] colliders = Physics.OverlapSphere(transform.position, detectionRange);
                
                foreach (var collider in colliders)
                {
                    if (collider.CompareTag("Player"))
                    {
                        target = collider.transform;
                        currentState = AIState.Chasing;
                        yield break;
                    }
                }
                
                yield return new WaitForSeconds(0.5f);
            }
            
            currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Count;
            yield return new WaitForSeconds(2f);
        }
        
        private IEnumerator HandleChaseState()
        {
            if (target == null)
            {
                currentState = AIState.Idle;
                yield break;
            }
            
            navAgent.SetDestination(target.position);
            
            float distanceToTarget = Vector3.Distance(transform.position, target.position);
            
            if (distanceToTarget <= attackRange)
            {
                currentState = AIState.Attacking;
            }
            else if (distanceToTarget > detectionRange * 1.5f)
            {
                target = null;
                currentState = AIState.Idle;
            }
            
            yield return new WaitForSeconds(0.1f);
        }
        
        private IEnumerator HandleAttackState()
        {
            if (target == null)
            {
                currentState = AIState.Idle;
                yield break;
            }
            
            navAgent.isStopped = true;
            
            // Face the target
            Vector3 direction = (target.position - transform.position).normalized;
            direction.y = 0;
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            
            // Attack if cooldown is over
            if (Time.time - lastAttackTime >= attackCooldown)
            {
                PerformAttack();
                lastAttackTime = Time.time;
            }
            
            float distanceToTarget = Vector3.Distance(transform.position, target.position);
            
            if (distanceToTarget > attackRange)
            {
                navAgent.isStopped = false;
                currentState = AIState.Chasing;
            }
            
            yield return new WaitForSeconds(0.1f);
        }
        
        private IEnumerator HandleFleeState()
        {
            if (target == null)
            {
                currentState = AIState.Idle;
                yield break;
            }
            
            // Run away from target
            Vector3 fleeDirection = (transform.position - target.position).normalized;
            Vector3 fleePosition = transform.position + fleeDirection * 10f;
            
            navAgent.SetDestination(fleePosition);
            
            float distanceToTarget = Vector3.Distance(transform.position, target.position);
            
            if (distanceToTarget > detectionRange * 2f)
            {
                target = null;
                currentState = AIState.Idle;
            }
            
            yield return new WaitForSeconds(0.5f);
        }
        
        private void PerformAttack()
        {
            if (animator != null)
            {
                animator.SetTrigger("Attack");
            }
            
            // Deal damage to target
            PlayerData playerData = target.GetComponent<PlayerData>();
            if (playerData != null)
            {
                int damage = UnityEngine.Random.Range(5, 15);
                playerData.TakeDamage(damage);
            }
        }
        
        public void TakeDamage(int damage)
        {
            // Handle damage
            if (animator != null)
            {
                animator.SetTrigger("Hit");
            }
            
            // Check if should flee
            if (UnityEngine.Random.Range(0f, 1f) < 0.3f)
            {
                currentState = AIState.Fleeing;
            }
        }
        
        public void Die()
        {
            currentState = AIState.Dead;
            
            if (animator != null)
            {
                animator.SetTrigger("Death");
            }
            
            navAgent.enabled = false;
            rb.isKinematic = true;
            
            Destroy(gameObject, 5f);
        }
        
        void OnDrawGizmosSelected()
        {
            // Detection range
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRange);
            
            // Attack range
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
            
            // Current target
            if (target != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position, target.position);
            }
        }
    }
    
    public class InventorySystem : MonoBehaviour
    {
        [System.Serializable]
        public class InventoryItem
        {
            public string itemID;
            public string itemName;
            public string description;
            public Sprite icon;
            public int quantity;
            public int maxStack;
            public float weight;
            public ItemType type;
            public Dictionary<string, object> properties;
            
            public enum ItemType
            {
                Weapon,
                Armor,
                Consumable,
                Material,
                Quest,
                Misc
            }
            
            public InventoryItem(string id, string name, ItemType itemType)
            {
                itemID = id;
                itemName = name;
                type = itemType;
                quantity = 1;
                maxStack = 99;
                weight = 1f;
                properties = new Dictionary<string, object>();
            }
            
            public bool CanStack()
            {
                return quantity < maxStack;
            }
            
            public void Use()
            {
                switch (type)
                {
                    case ItemType.Consumable:
                        // Use consumable logic
                        quantity--;
                        break;
                    case ItemType.Weapon:
                    case ItemType.Armor:
                        // Equip logic
                        break;
                }
            }
        }
        
        [Header("Inventory Configuration")]
        [SerializeField] private int maxSlots = 30;
        [SerializeField] private float maxWeight = 100f;
        [SerializeField] private bool allowOverweight = false;
        
        [Header("Inventory State")]
        [SerializeField] private List<InventoryItem> items;
        [SerializeField] private float currentWeight;
        
        public UnityEvent<InventoryItem> OnItemAdded;
        public UnityEvent<InventoryItem> OnItemRemoved;
        public UnityEvent<InventoryItem> OnItemUsed;
        public UnityEvent OnInventoryFull;
        public UnityEvent OnOverweight;
        
        void Awake()
        {
            items = new List<InventoryItem>();
            currentWeight = 0f;
            
            OnItemAdded = new UnityEvent<InventoryItem>();
            OnItemRemoved = new UnityEvent<InventoryItem>();
            OnItemUsed = new UnityEvent<InventoryItem>();
            OnInventoryFull = new UnityEvent();
            OnOverweight = new UnityEvent();
        }
        
        public bool AddItem(InventoryItem newItem)
        {
            // Check weight
            if (!allowOverweight && currentWeight + newItem.weight > maxWeight)
            {
                OnOverweight?.Invoke();
                return false;
            }
            
            // Try to stack with existing items
            foreach (var item in items)
            {
                if (item.itemID == newItem.itemID && item.CanStack())
                {
                    int spaceLeft = item.maxStack - item.quantity;
                    int toAdd = Mathf.Min(spaceLeft, newItem.quantity);
                    
                    item.quantity += toAdd;
                    currentWeight += newItem.weight * toAdd;
                    newItem.quantity -= toAdd;
                    
                    OnItemAdded?.Invoke(item);
                    
                    if (newItem.quantity <= 0)
                    {
                        return true;
                    }
                }
            }
            
            // Add as new item if there's space
            if (items.Count < maxSlots)
            {
                items.Add(newItem);
                currentWeight += newItem.weight * newItem.quantity;
                OnItemAdded?.Invoke(newItem);
                return true;
            }
            
            OnInventoryFull?.Invoke();
            return false;
        }
        
        public bool RemoveItem(string itemID, int quantity = 1)
        {
            InventoryItem itemToRemove = items.Find(item => item.itemID == itemID);
            
            if (itemToRemove != null && itemToRemove.quantity >= quantity)
            {
                itemToRemove.quantity -= quantity;
                currentWeight -= itemToRemove.weight * quantity;
                
                if (itemToRemove.quantity <= 0)
                {
                    items.Remove(itemToRemove);
                }
                
                OnItemRemoved?.Invoke(itemToRemove);
                return true;
            }
            
            return false;
        }
        
        public void UseItem(string itemID)
        {
            InventoryItem item = items.Find(i => i.itemID == itemID);
            
            if (item != null)
            {
                item.Use();
                OnItemUsed?.Invoke(item);
                
                if (item.quantity <= 0)
                {
                    items.Remove(item);
                    OnItemRemoved?.Invoke(item);
                }
            }
        }
        
        public InventoryItem GetItem(string itemID)
        {
            return items.Find(item => item.itemID == itemID);
        }
        
        public List<InventoryItem> GetAllItems()
        {
            return new List<InventoryItem>(items);
        }
        
        public int GetItemCount(string itemID)
        {
            InventoryItem item = items.Find(i => i.itemID == itemID);
            return item != null ? item.quantity : 0;
        }
        
        public float GetCurrentWeight()
        {
            return currentWeight;
        }
        
        public float GetWeightPercentage()
        {
            return currentWeight / maxWeight;
        }
        
        public bool HasSpace()
        {
            return items.Count < maxSlots;
        }
        
        public bool HasItem(string itemID, int quantity = 1)
        {
            return GetItemCount(itemID) >= quantity;
        }
        
        public void SortInventory()
        {
            items.Sort((a, b) => 
            {
                int typeComparison = a.type.CompareTo(b.type);
                if (typeComparison != 0) return typeComparison;
                return string.Compare(a.itemName, b.itemName);
            });
        }
        
        public void ClearInventory()
        {
            items.Clear();
            currentWeight = 0f;
        }
        
        public string SerializeInventory()
        {
            return JsonUtility.ToJson(items);
        }
        
        public void DeserializeInventory(string json)
        {
            items = JsonUtility.FromJson<List<InventoryItem>>(json);
            RecalculateWeight();
        }
        
        private void RecalculateWeight()
        {
            currentWeight = 0f;
            foreach (var item in items)
            {
                currentWeight += item.weight * item.quantity;
            }
        }
    }
    
    public class QuestSystem : MonoBehaviour
    {
        [System.Serializable]
        public class Quest
        {
            public string questID;
            public string questName;
            public string description;
            public QuestType type;
            public QuestStatus status;
            public List<QuestObjective> objectives;
            public QuestReward reward;
            public int requiredLevel;
            public List<string> prerequisites;
            public float timeLimit;
            public DateTime startTime;
            
            public enum QuestType
            {
                Main,
                Side,
                Daily,
                Weekly,
                Event
            }
            
            public enum QuestStatus
            {
                Locked,
                Available,
                Active,
                Completed,
                Failed,
                Abandoned
            }
            
            public Quest(string id, string name, QuestType questType)
            {
                questID = id;
                questName = name;
                type = questType;
                status = QuestStatus.Available;
                objectives = new List<QuestObjective>();
                prerequisites = new List<string>();
                requiredLevel = 1;
                timeLimit = -1f;
            }
            
            public bool IsComplete()
            {
                foreach (var objective in objectives)
                {
                    if (!objective.isCompleted)
                    {
                        return false;
                    }
                }
                return true;
            }
            
            public float GetProgress()
            {
                if (objectives.Count == 0) return 0f;
                
                int completed = 0;
                foreach (var objective in objectives)
                {
                    if (objective.isCompleted) completed++;
                }
                
                return (float)completed / objectives.Count;
            }
            
            public bool HasExpired()
            {
                if (timeLimit <= 0) return false;
                
                TimeSpan elapsed = DateTime.Now - startTime;
                return elapsed.TotalSeconds > timeLimit;
            }
        }
        
        [System.Serializable]
        public class QuestObjective
        {
            public string objectiveID;
            public string description;
            public ObjectiveType type;
            public string targetID;
            public int requiredCount;
            public int currentCount;
            public bool isCompleted;
            public bool isOptional;
            
            public enum ObjectiveType
            {
                Kill,
                Collect,
                Deliver,
                Talk,
                Reach,
                Survive,
                Escort,
                Defend,
                Custom
            }
            
            public QuestObjective(string id, string desc, ObjectiveType objType, int required)
            {
                objectiveID = id;
                description = desc;
                type = objType;
                requiredCount = required;
                currentCount = 0;
                isCompleted = false;
                isOptional = false;
            }
            
            public void UpdateProgress(int amount = 1)
            {
                currentCount = Mathf.Min(currentCount + amount, requiredCount);
                
                if (currentCount >= requiredCount)
                {
                    isCompleted = true;
                }
            }
            
            public float GetProgress()
            {
                if (requiredCount <= 0) return 1f;
                return (float)currentCount / requiredCount;
            }
        }
        
        [System.Serializable]
        public class QuestReward
        {
            public int experience;
            public int gold;
            public List<string> items;
            public Dictionary<string, int> currencies;
            public List<string> unlockedQuests;
            
            public QuestReward()
            {
                experience = 0;
                gold = 0;
                items = new List<string>();
                currencies = new Dictionary<string, int>();
                unlockedQuests = new List<string>();
            }
        }
        
        [Header("Quest System Configuration")]
        [SerializeField] private int maxActiveQuests = 10;
        [SerializeField] private bool autoTrackMainQuests = true;
        
        [Header("Quest State")]
        [SerializeField] private List<Quest> allQuests;
        [SerializeField] private List<Quest> activeQuests;
        [SerializeField] private Quest trackedQuest;
        
        public UnityEvent<Quest> OnQuestStarted;
        public UnityEvent<Quest> OnQuestCompleted;
        public UnityEvent<Quest> OnQuestFailed;
        public UnityEvent<Quest> OnQuestAbandoned;
        public UnityEvent<QuestObjective> OnObjectiveCompleted;
        public UnityEvent<Quest> OnQuestTracked;
        
        void Awake()
        {
            allQuests = new List<Quest>();
            activeQuests = new List<Quest>();
            
            OnQuestStarted = new UnityEvent<Quest>();
            OnQuestCompleted = new UnityEvent<Quest>();
            OnQuestFailed = new UnityEvent<Quest>();
            OnQuestAbandoned = new UnityEvent<Quest>();
            OnObjectiveCompleted = new UnityEvent<QuestObjective>();
            OnQuestTracked = new UnityEvent<Quest>();
            
            LoadQuests();
        }
        
        void Start()
        {
            StartCoroutine(QuestUpdateLoop());
        }
        
        private void LoadQuests()
        {
            // Load quest data from resources or database
            // This is a placeholder implementation
            
            // Create sample quests
            Quest mainQuest = new Quest("main_001", "The Hero's Journey", Quest.QuestType.Main);
            mainQuest.description = "Embark on an epic adventure to save the kingdom.";
            mainQuest.objectives.Add(new QuestObjective("obj_001", "Defeat 10 enemies", QuestObjective.ObjectiveType.Kill, 10));
            mainQuest.objectives.Add(new QuestObjective("obj_002", "Collect 5 magic crystals", QuestObjective.ObjectiveType.Collect, 5));
            mainQuest.reward = new QuestReward { experience = 1000, gold = 500 };
            
            Quest sideQuest = new Quest("side_001", "Lost and Found", Quest.QuestType.Side);
            sideQuest.description = "Help the villager find their lost pet.";
            sideQuest.objectives.Add(new QuestObjective("obj_003", "Find the lost pet", QuestObjective.ObjectiveType.Custom, 1));
            sideQuest.reward = new QuestReward { experience = 200, gold = 100 };
            
            Quest dailyQuest = new Quest("daily_001", "Daily Training", Quest.QuestType.Daily);
            dailyQuest.description = "Complete your daily training routine.";
            dailyQuest.objectives.Add(new QuestObjective("obj_004", "Practice combat moves", QuestObjective.ObjectiveType.Custom, 3));
            dailyQuest.timeLimit = 86400f; // 24 hours
            dailyQuest.reward = new QuestReward { experience = 50, gold = 25 };
            
            allQuests.Add(mainQuest);
            allQuests.Add(sideQuest);
            allQuests.Add(dailyQuest);
        }
        
        private IEnumerator QuestUpdateLoop()
        {
            while (true)
            {
                // Check for expired quests
                List<Quest> toRemove = new List<Quest>();
                
                foreach (var quest in activeQuests)
                {
                    if (quest.HasExpired())
                    {
                        quest.status = Quest.QuestStatus.Failed;
                        toRemove.Add(quest);
                        OnQuestFailed?.Invoke(quest);
                    }
                    else if (quest.IsComplete())
                    {
                        CompleteQuest(quest.questID);
                    }
                }
                
                foreach (var quest in toRemove)
                {
                    activeQuests.Remove(quest);
                }
                
                yield return new WaitForSeconds(1f);
            }
        }
        
        public bool StartQuest(string questID)
        {
            if (activeQuests.Count >= maxActiveQuests)
            {
                Debug.LogWarning("Cannot start quest: Maximum active quests reached");
                return false;
            }
            
            Quest quest = allQuests.Find(q => q.questID == questID);
            
            if (quest == null)
            {
                Debug.LogError($"Quest not found: {questID}");
                return false;
            }
            
            if (quest.status != Quest.QuestStatus.Available)
            {
                Debug.LogWarning($"Quest cannot be started: {quest.status}");
                return false;
            }
            
            // Check prerequisites
            foreach (var prereq in quest.prerequisites)
            {
                Quest prereqQuest = allQuests.Find(q => q.questID == prereq);
                if (prereqQuest == null || prereqQuest.status != Quest.QuestStatus.Completed)
                {
                    Debug.LogWarning($"Prerequisite not met: {prereq}");
                    return false;
                }
            }
            
            // Check level requirement
            PlayerData player = GameManager.Instance?.currentPlayer;
            if (player != null && player.level < quest.requiredLevel)
            {
                Debug.LogWarning($"Level requirement not met: {quest.requiredLevel}");
                return false;
            }
            
            quest.status = Quest.QuestStatus.Active;
            quest.startTime = DateTime.Now;
            activeQuests.Add(quest);
            
            if (autoTrackMainQuests && quest.type == Quest.QuestType.Main)
            {
                TrackQuest(questID);
            }
            
            OnQuestStarted?.Invoke(quest);
            
            return true;
        }
        
        public void CompleteQuest(string questID)
        {
            Quest quest = activeQuests.Find(q => q.questID == questID);
            
            if (quest == null) return;
            
            if (!quest.IsComplete())
            {
                Debug.LogWarning("Quest is not complete yet");
                return;
            }
            
            quest.status = Quest.QuestStatus.Completed;
            activeQuests.Remove(quest);
            
            // Grant rewards
            if (quest.reward != null)
            {
                PlayerData player = GameManager.Instance?.currentPlayer;
                if (player != null)
                {
                    player.AddExperience(quest.reward.experience);
                    // Add gold and items through inventory system
                }
                
                // Unlock new quests
                foreach (var unlockedQuestID in quest.reward.unlockedQuests)
                {
                    Quest unlockedQuest = allQuests.Find(q => q.questID == unlockedQuestID);
                    if (unlockedQuest != null)
                    {
                        unlockedQuest.status = Quest.QuestStatus.Available;
                    }
                }
            }
            
            OnQuestCompleted?.Invoke(quest);
            
            // Auto-track next main quest
            if (autoTrackMainQuests && quest.type == Quest.QuestType.Main)
            {
                Quest nextMainQuest = activeQuests.Find(q => q.type == Quest.QuestType.Main);
                if (nextMainQuest != null)
                {
                    TrackQuest(nextMainQuest.questID);
                }
            }
        }
        
        public void AbandonQuest(string questID)
        {
            Quest quest = activeQuests.Find(q => q.questID == questID);
            
            if (quest == null) return;
            
            quest.status = Quest.QuestStatus.Abandoned;
            activeQuests.Remove(quest);
            
            OnQuestAbandoned?.Invoke(quest);
        }
        
        public void UpdateObjective(string questID, string objectiveID, int progress = 1)
        {
            Quest quest = activeQuests.Find(q => q.questID == questID);
            
            if (quest == null) return;
            
            QuestObjective objective = quest.objectives.Find(o => o.objectiveID == objectiveID);
            
            if (objective == null || objective.isCompleted) return;
            
            objective.UpdateProgress(progress);
            
            if (objective.isCompleted)
            {
                OnObjectiveCompleted?.Invoke(objective);
            }
        }
        
        public void TrackQuest(string questID)
        {
            Quest quest = activeQuests.Find(q => q.questID == questID);
            
            if (quest != null)
            {
                trackedQuest = quest;
                OnQuestTracked?.Invoke(quest);
            }
        }
        
        public Quest GetTrackedQuest()
        {
            return trackedQuest;
        }
        
        public List<Quest> GetActiveQuests()
        {
            return new List<Quest>(activeQuests);
        }
        
        public List<Quest> GetAvailableQuests()
        {
            return allQuests.FindAll(q => q.status == Quest.QuestStatus.Available);
        }
        
        public List<Quest> GetCompletedQuests()
        {
            return allQuests.FindAll(q => q.status == Quest.QuestStatus.Completed);
        }
        
        public Quest GetQuest(string questID)
        {
            return allQuests.Find(q => q.questID == questID);
        }
    }
    
    #endregion
    
    #region Utility Classes
    
    public static class MathUtility
    {
        public static float Remap(float value, float from1, float to1, float from2, float to2)
        {
            return (value - from1) / (to1 - from1) * (to2 - from2) + from2;
        }
        
        public static float SmoothStep(float from, float to, float t)
        {
            t = Mathf.Clamp01(t);
            t = t * t * (3f - 2f * t);
            return from + (to - from) * t;
        }
        
        public static float SmootherStep(float from, float to, float t)
        {
            t = Mathf.Clamp01(t);
            t = t * t * t * (t * (6f * t - 15f) + 10f);
            return from + (to - from) * t;
        }
        
        public static Vector3 RandomPointInBounds(Bounds bounds)
        {
            return new Vector3(
                UnityEngine.Random.Range(bounds.min.x, bounds.max.x),
                UnityEngine.Random.Range(bounds.min.y, bounds.max.y),
                UnityEngine.Random.Range(bounds.min.z, bounds.max.z)
            );
        }
        
        public static Vector2 RandomPointInCircle(float radius)
        {
            float angle = UnityEngine.Random.Range(0f, 2f * Mathf.PI);
            float r = radius * Mathf.Sqrt(UnityEngine.Random.Range(0f, 1f));
            return new Vector2(r * Mathf.Cos(angle), r * Mathf.Sin(angle));
        }
        
        public static bool RandomBool(float probability = 0.5f)
        {
            return UnityEngine.Random.Range(0f, 1f) < probability;
        }
        
        public static T RandomElement<T>(T[] array)
        {
            if (array == null || array.Length == 0) return default(T);
            return array[UnityEngine.Random.Range(0, array.Length)];
        }
        
        public static T RandomElement<T>(List<T> list)
        {
            if (list == null || list.Count == 0) return default(T);
            return list[UnityEngine.Random.Range(0, list.Count)];
        }
        
        public static void Shuffle<T>(T[] array)
        {
            for (int i = array.Length - 1; i > 0; i--)
            {
                int randomIndex = UnityEngine.Random.Range(0, i + 1);
                T temp = array[i];
                array[i] = array[randomIndex];
                array[randomIndex] = temp;
            }
        }
        
        public static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int randomIndex = UnityEngine.Random.Range(0, i + 1);
                T temp = list[i];
                list[i] = list[randomIndex];
                list[randomIndex] = temp;
            }
        }
    }
    
    public static class StringUtility
    {
        public static string ToTitleCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            
            string[] words = input.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
                }
            }
            
            return string.Join(" ", words);
        }
        
        public static string ToCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            
            string result = ToTitleCase(input).Replace(" ", "");
            if (result.Length > 0)
            {
                result = char.ToLower(result[0]) + result.Substring(1);
            }
            
            return result;
        }
        
        public static string ToPascalCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return ToTitleCase(input).Replace(" ", "");
        }
        
        public static string ToSnakeCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return input.Replace(" ", "_").ToLower();
        }
        
        public static string ToKebabCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            return input.Replace(" ", "-").ToLower();
        }
        
        public static string Truncate(string input, int maxLength, string suffix = "...")
        {
            if (string.IsNullOrEmpty(input) || input.Length <= maxLength)
                return input;
            
            return input.Substring(0, maxLength - suffix.Length) + suffix;
        }
        
        public static bool IsValidEmail(string email)
        {
            if (string.IsNullOrEmpty(email)) return false;
            
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }
    }
    
    #endregion
    
    #region Extension Methods
    
    public static class VectorExtensions
    {
        public static Vector3 WithX(this Vector3 vector, float x)
        {
            return new Vector3(x, vector.y, vector.z);
        }
        
        public static Vector3 WithY(this Vector3 vector, float y)
        {
            return new Vector3(vector.x, y, vector.z);
        }
        
        public static Vector3 WithZ(this Vector3 vector, float z)
        {
            return new Vector3(vector.x, vector.y, z);
        }
        
        public static Vector2 ToVector2XZ(this Vector3 vector)
        {
            return new Vector2(vector.x, vector.z);
        }
        
        public static Vector3 ToVector3XZ(this Vector2 vector, float y = 0)
        {
            return new Vector3(vector.x, y, vector.y);
        }
        
        public static Vector3 Flat(this Vector3 vector)
        {
            return new Vector3(vector.x, 0, vector.z);
        }
        
        public static float DistanceXZ(this Vector3 a, Vector3 b)
        {
            return Vector2.Distance(a.ToVector2XZ(), b.ToVector2XZ());
        }
    }
    
    public static class TransformExtensions
    {
        public static void DestroyChildren(this Transform transform)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject.Destroy(transform.GetChild(i).gameObject);
            }
        }
        
        public static void DestroyChildrenImmediate(this Transform transform)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject.DestroyImmediate(transform.GetChild(i).gameObject);
            }
        }
        
        public static void SetActiveChildren(this Transform transform, bool active)
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                transform.GetChild(i).gameObject.SetActive(active);
            }
        }
        
        public static Transform FindDeepChild(this Transform parent, string name)
        {
            Transform result = parent.Find(name);
            if (result != null) return result;
            
            foreach (Transform child in parent)
            {
                result = child.FindDeepChild(name);
                if (result != null) return result;
            }
            
            return null;
        }
        
        public static T GetOrAddComponent<T>(this GameObject go) where T : Component
        {
            T comp = go.GetComponent<T>();
            if (comp == null)
            {
                comp = go.AddComponent<T>();
            }
            return comp;
        }
    }
    
    public static class ListExtensions
    {
        public static T GetRandom<T>(this List<T> list)
        {
            if (list == null || list.Count == 0) return default(T);
            return list[UnityEngine.Random.Range(0, list.Count)];
        }
        
        public static T GetRandom<T>(this T[] array)
        {
            if (array == null || array.Length == 0) return default(T);
            return array[UnityEngine.Random.Range(0, array.Length)];
        }
        
        public static void AddUnique<T>(this List<T> list, T item)
        {
            if (!list.Contains(item))
            {
                list.Add(item);
            }
        }
        
        public static bool IsNullOrEmpty<T>(this List<T> list)
        {
            return list == null || list.Count == 0;
        }
        
        public static bool IsNullOrEmpty<T>(this T[] array)
        {
            return array == null || array.Length == 0;
        }
    }
    
    #endregion
}