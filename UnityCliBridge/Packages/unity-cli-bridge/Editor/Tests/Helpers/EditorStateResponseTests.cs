using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityCliBridge.Helpers;

namespace UnityCliBridge.Tests.Helpers
{
    /// <summary>
    /// Tests for editor state in response
    /// </summary>
    public class EditorStateResponseTests
    {
        [Test]
        public void SuccessResult_ShouldIncludeEditorState()
        {
            // Act
            var result = Response.SuccessResult(new { test = "data" });
            var json = JObject.Parse(result);
            
            // Assert
            Assert.IsNotNull(json["editorState"], "Response should include editorState");
            Assert.IsNotNull(json["editorState"]["isPlaying"], "EditorState should include isPlaying");
            Assert.IsNotNull(json["editorState"]["isPaused"], "EditorState should include isPaused");
            Assert.AreEqual("success", json["status"].ToString());
            Assert.IsNotNull(json["result"]);
        }
        
        [Test]
        public void SuccessResultWithId_ShouldIncludeEditorState()
        {
            // Act
            var result = Response.SuccessResult("test-id", new { test = "data" });
            var json = JObject.Parse(result);
            
            // Assert
            Assert.IsNotNull(json["editorState"], "Response should include editorState");
            Assert.IsNotNull(json["editorState"]["isPlaying"], "EditorState should include isPlaying");
            Assert.IsNotNull(json["editorState"]["isPaused"], "EditorState should include isPaused");
            Assert.AreEqual("test-id", json["id"].ToString());
            Assert.AreEqual("success", json["status"].ToString());
            Assert.IsNotNull(json["result"]);
        }
        
        [Test]
        public void EditorState_ShouldReturnBooleanValues()
        {
            // Act
            var result = Response.SuccessResult(new { test = "data" });
            var json = JObject.Parse(result);
            
            // Assert
            Assert.IsTrue(json["editorState"]["isPlaying"].Type == JTokenType.Boolean, "isPlaying should be a boolean");
            Assert.IsTrue(json["editorState"]["isPaused"].Type == JTokenType.Boolean, "isPaused should be a boolean");
        }
    }
}