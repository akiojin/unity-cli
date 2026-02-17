using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace UnityCliBridge.Tests.PlayMode
{
    /// <summary>
    /// PlayMode開始環境ですでに Play Mode に入っている前提で長時間フレームを進めるテスト。
    /// EnterPlayMode/ExitPlayMode を使用しないため、UTRの制約に抵触しない。
    /// </summary>
    public class LongRunningInPlayContextTests
    {
        private const int FramesToWait = 180 * 60; // 約3分（60fps換算）

        [UnityTest]
        public IEnumerator PlayMode_RunsLongWithoutTimeoutOrExit()
        {
            // 確実にPlay Modeであることを前提
            Assert.IsTrue(Application.isPlaying, "Test must run in Play Mode");

            for (int i = 0; i < FramesToWait; i++)
            {
                yield return null;
            }

            Assert.Pass("Stayed in Play Mode for ~3 minutes without timeout or exit.");
        }
    }
}
