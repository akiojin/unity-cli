using NUnit.Framework;
using UnityCliBridge.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace UnityCliBridge.Tests.Models
{
    [TestFixture]
    public class CommandTests
    {
        [Test]
        public void Command_ShouldSerializeCorrectly()
        {
            // Arrange
            var command = new Command
            {
                Id = "test-123",
                Type = "ping",
                Parameters = new JObject
                {
                    ["timeout"] = 5000
                }
            };
            
            // Act
            var json = JsonConvert.SerializeObject(command);
            var deserialized = JsonConvert.DeserializeObject<Command>(json);
            
            // Assert
            Assert.AreEqual(command.Id, deserialized.Id);
            Assert.AreEqual(command.Type, deserialized.Type);
            Assert.AreEqual(command.Parameters["timeout"].Value<int>(), 
                          deserialized.Parameters["timeout"].Value<int>());
        }
        
        [Test]
        public void Command_ShouldHandleNullParameters()
        {
            // Arrange
            var command = new Command
            {
                Id = "test-456",
                Type = "ping",
                Parameters = null
            };
            
            // Act
            var json = JsonConvert.SerializeObject(command);
            var deserialized = JsonConvert.DeserializeObject<Command>(json);
            
            // Assert
            Assert.IsNull(deserialized.Parameters);
        }
        
        [Test]
        public void Command_ShouldSetReceivedAtTimestamp()
        {
            // Arrange
            var beforeCreation = DateTime.Now;
            
            // Act
            var command = new Command();
            var afterCreation = DateTime.Now;
            
            // Assert
            Assert.GreaterOrEqual(command.ReceivedAt, beforeCreation);
            Assert.LessOrEqual(command.ReceivedAt, afterCreation);
        }
        
        [Test]
        public void Command_ToString_ShouldReturnFormattedString()
        {
            // Arrange
            var command = new Command
            {
                Id = "test-789",
                Type = "create_gameobject"
            };
            
            // Act
            var result = command.ToString();
            
            // Assert
            Assert.AreEqual("Command[test-789]: create_gameobject", result);
        }
        
        [Test]
        public void Command_ShouldDeserializeFromJson()
        {
            // Arrange
            var json = @"{
                ""id"": ""cmd-001"",
                ""type"": ""ping"",
                ""params"": {
                    ""echo"": ""hello""
                }
            }";
            
            // Act
            var command = JsonConvert.DeserializeObject<Command>(json);
            
            // Assert
            Assert.AreEqual("cmd-001", command.Id);
            Assert.AreEqual("ping", command.Type);
            Assert.AreEqual("hello", command.Parameters["echo"].Value<string>());
        }
    }
}