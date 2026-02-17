using NUnit.Framework;
using UnityCliBridge.Models;

namespace UnityCliBridge.Tests.Models
{
    [TestFixture]
    public class McpStatusTests
    {
        [Test]
        public void McpStatus_ShouldHaveCorrectValues()
        {
            // Assert
            Assert.AreEqual(0, (int)McpStatus.NotConfigured);
            Assert.AreEqual(1, (int)McpStatus.Disconnected);
            Assert.AreEqual(2, (int)McpStatus.Connecting);
            Assert.AreEqual(3, (int)McpStatus.Connected);
            Assert.AreEqual(4, (int)McpStatus.Error);
        }
        
        [Test]
        public void McpStatus_ShouldBeConvertibleToString()
        {
            // Arrange
            var status = McpStatus.Connected;
            
            // Act
            var statusString = status.ToString();
            
            // Assert
            Assert.AreEqual("Connected", statusString);
        }
        
        [Test]
        public void McpStatus_ShouldBeComparable()
        {
            // Arrange
            var status1 = McpStatus.NotConfigured;
            var status2 = McpStatus.Connected;
            
            // Assert
            Assert.AreNotEqual(status1, status2);
            Assert.IsTrue(status1 < status2);
        }
    }
}