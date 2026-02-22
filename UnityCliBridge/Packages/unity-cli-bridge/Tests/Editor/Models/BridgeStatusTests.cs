using NUnit.Framework;
using UnityCliBridge.Models;

namespace UnityCliBridge.Tests.Models
{
    [TestFixture]
    public class BridgeStatusTests
    {
        [Test]
        public void BridgeStatus_ShouldHaveCorrectValues()
        {
            // Assert
            Assert.AreEqual(0, (int)BridgeStatus.NotConfigured);
            Assert.AreEqual(1, (int)BridgeStatus.Disconnected);
            Assert.AreEqual(2, (int)BridgeStatus.Connecting);
            Assert.AreEqual(3, (int)BridgeStatus.Connected);
            Assert.AreEqual(4, (int)BridgeStatus.Error);
        }
        
        [Test]
        public void BridgeStatus_ShouldBeConvertibleToString()
        {
            // Arrange
            var status = BridgeStatus.Connected;
            
            // Act
            var statusString = status.ToString();
            
            // Assert
            Assert.AreEqual("Connected", statusString);
        }
        
        [Test]
        public void BridgeStatus_ShouldBeComparable()
        {
            // Arrange
            var status1 = BridgeStatus.NotConfigured;
            var status2 = BridgeStatus.Connected;
            
            // Assert
            Assert.AreNotEqual(status1, status2);
            Assert.IsTrue(status1 < status2);
        }
    }
}