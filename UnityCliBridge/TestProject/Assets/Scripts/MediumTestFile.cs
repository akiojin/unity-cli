using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using System.Collections.Generic;

namespace TestNamespace
{
    // 中サイズテストファイル（約200行）
    public class MediumTestFile : MonoBehaviour
    {
        [Header("Test Settings")]
        public float testFloat = 1.0f;
        public int testInt = 100;
        public string testString = "Medium test";
        public bool testBool = true;
        
        [SerializeField] private List<GameObject> gameObjects;
        [SerializeField] private Transform[] transforms;
        private Dictionary<int, string> testDictionary;
        private Coroutine testCoroutine;
        
        public delegate void TestDelegate(int value);
        public event TestDelegate OnTestEvent;
        
        public enum TestEnum
        {
            Option1,
            Option2,
            Option3,
            Option4,
            Option5
        }
        
        public TestEnum currentEnum = TestEnum.Option1;
        
        void Start()
        {
            InitializeComponents();
            SetupTestData();
            StartTestCoroutine();
        }
        
        void Update()
        {
            HandleInput();
            UpdateLogic();
            CheckConditions();
        }
        
        private void InitializeComponents()
        {
            gameObjects = new List<GameObject>();
            testDictionary = new Dictionary<int, string>();
            
            for (int i = 0; i < 10; i++)
            {
                GameObject go = new GameObject("TestObject_" + i);
                gameObjects.Add(go);
                testDictionary.Add(i, "Value_" + i);
            }
        }
        
        private void SetupTestData()
        {
            testFloat = Random.Range(0f, 100f);
            testInt = Random.Range(0, 1000);
            testString = "Generated at " + System.DateTime.Now.ToString();
            testBool = Random.Range(0, 2) == 1;
        }
        
        private void HandleInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.mKey.wasPressedThisFrame)
            {
                ToggleTestBool();
            }

            if (keyboard.nKey.wasPressedThisFrame)
            {
                IncrementTestInt();
            }

            if (keyboard.bKey.wasPressedThisFrame)
            {
                TriggerTestEvent();
            }
        }
        
        private void UpdateLogic()
        {
            testFloat += Time.deltaTime;
            
            if (testFloat > 100f)
            {
                testFloat = 0f;
            }
            
            UpdateEnum();
        }
        
        private void CheckConditions()
        {
            if (testInt > 500 && testBool)
            {
                PerformSpecialAction();
            }
            
            if (gameObjects.Count > 15)
            {
                RemoveExcessObjects();
            }
        }
        
        private void UpdateEnum()
        {
            int enumCount = System.Enum.GetValues(typeof(TestEnum)).Length;
            int nextValue = ((int)currentEnum + 1) % enumCount;
            currentEnum = (TestEnum)nextValue;
        }
        
        public void ToggleTestBool()
        {
            testBool = !testBool;
            UnityEngine.Debug.LogFormat("Test bool toggled to: " + testBool);
        }
        
        public void IncrementTestInt()
        {
            testInt++;
            OnTestEvent?.Invoke(testInt);
        }
        
        private void TriggerTestEvent()
        {
            OnTestEvent?.Invoke(testInt);
            UnityEngine.Debug.LogFormat("Test event triggered with value: " + testInt);
        }
        
        private void PerformSpecialAction()
        {
            UnityEngine.Debug.LogFormat("Special action performed!");
            CreateAdditionalObjects();
        }
        
        private void CreateAdditionalObjects()
        {
            for (int i = 0; i < 3; i++)
            {
                GameObject go = new GameObject("SpecialObject_" + i);
                gameObjects.Add(go);
            }
        }
        
        private void RemoveExcessObjects()
        {
            while (gameObjects.Count > 10)
            {
                GameObject toRemove = gameObjects[gameObjects.Count - 1];
                gameObjects.RemoveAt(gameObjects.Count - 1);
                if (toRemove != null)
                {
                    DestroyImmediate(toRemove);
                }
            }
        }
        
        private void StartTestCoroutine()
        {
            if (testCoroutine != null)
            {
                StopCoroutine(testCoroutine);
            }
            testCoroutine = StartCoroutine(TestCoroutineMethod());
        }
        
        private IEnumerator TestCoroutineMethod()
        {
            while (true)
            {
                yield return new WaitForSeconds(2.0f);
                
                PerformPeriodicAction();
                
                yield return new WaitForSeconds(1.0f);
                
                if (!testBool)
                {
                    yield break;
                }
            }
        }
        
        private void PerformPeriodicAction()
        {
            testString = "Updated at " + Time.time;
            
            if (testDictionary.Count < 20)
            {
                testDictionary.Add(testDictionary.Count, "NewValue_" + Time.time);
            }
        }
        
        public void ResetTestData()
        {
            testFloat = 1.0f;
            testInt = 100;
            testString = "Reset";
            testBool = true;
            currentEnum = TestEnum.Option1;
            
            ClearGameObjects();
            testDictionary.Clear();
        }
        
        private void ClearGameObjects()
        {
            foreach (GameObject go in gameObjects)
            {
                if (go != null)
                {
                    DestroyImmediate(go);
                }
            }
            gameObjects.Clear();
        }
        
        public string GetTestInfo()
        {
            return $"Float: {testFloat}, Int: {testInt}, String: {testString}, Bool: {testBool}, Enum: {currentEnum}";
        }
        
        void OnDestroy()
        {
            if (testCoroutine != null)
            {
                StopCoroutine(testCoroutine);
            }
            ClearGameObjects();
            OnTestEvent = null;
        }
        
        void OnApplicationQuit()
        {
            UnityEngine.Debug.LogFormat("Medium test file application quit");
        }
    }
}
