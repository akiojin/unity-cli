using NUnit.Framework;
using UnityCliBridge.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace UnityCliBridge.Tests.Helpers
{
    [TestFixture]
    public class ResponseTests
    {
        [Test]
        public void Success_ShouldReturnCorrectJsonWithoutData()
        {
            // Act
            var result = Response.Success();
            var json = JObject.Parse(result);
            
            // Assert
            Assert.AreEqual("success", json["status"].Value<string>());
            Assert.IsFalse(json.ContainsKey("data"));
        }
        
        [Test]
        public void Success_ShouldReturnCorrectJsonWithData()
        {
            // Arrange
            var data = new { message = "test", value = 42 };
            
            // Act
            var result = Response.Success(data);
            var json = JObject.Parse(result);
            
            // Assert
            Assert.AreEqual("success", json["status"].Value<string>());
            Assert.AreEqual("test", json["data"]["message"].Value<string>());
            Assert.AreEqual(42, json["data"]["value"].Value<int>());
        }
        
        [Test]
        public void Error_ShouldReturnCorrectJsonWithMessage()
        {
            // Act
            var result = Response.Error("Something went wrong");
            var json = JObject.Parse(result);
            
            // Assert
            Assert.AreEqual("error", json["status"].Value<string>());
            Assert.AreEqual("Something went wrong", json["error"].Value<string>());
            Assert.IsFalse(json.ContainsKey("code"));
            Assert.IsFalse(json.ContainsKey("details"));
        }
        
        [Test]
        public void Error_ShouldReturnCorrectJsonWithCode()
        {
            // Act
            var result = Response.Error("Connection failed", "CONN_001", null);
            var json = JObject.Parse(result);
            
            // Assert
            Assert.AreEqual("error", json["status"].Value<string>());
            Assert.AreEqual("Connection failed", json["error"].Value<string>());
            Assert.AreEqual("CONN_001", json["code"].Value<string>());
        }
        
        [Test]
        public void Error_ShouldReturnCorrectJsonWithDetails()
        {
            // Arrange
            var details = new { port = 6400, attempts = 3 };
            
            // Act
            var result = Response.Error("Connection failed", "CONN_001", details);
            var json = JObject.Parse(result);
            
            // Assert
            Assert.AreEqual("error", json["status"].Value<string>());
            Assert.AreEqual("Connection failed", json["error"].Value<string>());
            Assert.AreEqual("CONN_001", json["code"].Value<string>());
            Assert.AreEqual(6400, json["details"]["port"].Value<int>());
            Assert.AreEqual(3, json["details"]["attempts"].Value<int>());
        }
        
        [Test]
        public void Pong_ShouldReturnCorrectJson()
        {
            // Act
            var result = Response.Pong();
            var json = JObject.Parse(result);
            
            // Assert
            Assert.AreEqual("success", json["status"].Value<string>());
            Assert.AreEqual("pong", json["data"]["message"].Value<string>());
            Assert.IsNotNull(json["data"]["timestamp"]);
            
            // Verify timestamp is valid ISO 8601
            var timestamp = json["data"]["timestamp"].Value<string>();
            Assert.DoesNotThrow(() => System.DateTime.Parse(timestamp));
        }
        
        [Test]
        public void Response_ShouldHandleNullData()
        {
            // Act
            var result = Response.Success(null);
            var json = JObject.Parse(result);
            
            // Assert
            Assert.AreEqual("success", json["status"].Value<string>());
            Assert.IsFalse(json.ContainsKey("data"));
        }
        
        [Test]
        public void Response_ShouldHandleComplexObjects()
        {
            // Arrange
            var complexData = new
            {
                gameObjects = new[] { "Player", "Enemy", "Ground" },
                settings = new Dictionary<string, object>
                {
                    ["difficulty"] = "hard",
                    ["level"] = 5
                }
            };
            
            // Act
            var result = Response.Success(complexData);
            var json = JObject.Parse(result);
            
            // Assert
            Assert.AreEqual(3, json["data"]["gameObjects"].Count());
            Assert.AreEqual("hard", json["data"]["settings"]["difficulty"].Value<string>());
            Assert.AreEqual(5, json["data"]["settings"]["level"].Value<int>());
        }

        [Test]
        public void SuccessResult_ShouldIncludeWarningsWhenProvided()
        {
            var warnings = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    ["code"] = "PLAY_MODE_RUNTIME_CHANGES",
                    ["message"] = "Changes are temporary during Play Mode",
                    ["severity"] = "warning"
                }
            };

            var result = Response.SuccessResult("cmd-42", new { ok = true }, warnings);
            var json = JObject.Parse(result);

            Assert.AreEqual("success", json["status"].Value<string>());
            Assert.AreEqual(1, json["warnings"].Count());
            Assert.AreEqual("warning", json["warnings"][0]["severity"].Value<string>());
            Assert.AreEqual("PLAY_MODE_RUNTIME_CHANGES", json["warnings"][0]["code"].Value<string>());
        }

        [Test]
        public void AppendWarnings_ShouldAttachToSuccessPayload()
        {
            var baseResponse = Response.SuccessResult("cmd-77", new { ok = true });
            var warnings = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    ["code"] = "PLAY_MODE_RUNTIME_CHANGES",
                    ["message"] = "temporary",
                    ["severity"] = "warning"
                }
            };

            var updated = Response.AppendWarnings(baseResponse, warnings);
            var json = JObject.Parse(updated);

            Assert.AreEqual("success", json["status"].Value<string>());
            Assert.AreEqual(1, json["warnings"].Count());
        }
    }
}
