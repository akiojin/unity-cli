using System;
using System.Security.Cryptography;

namespace UnityCliBridge.Core
{
    /// <summary>Credentials belong to this Editor domain, never to a command or scene.</summary>
    internal static class BridgeAuthentication
    {
        internal const string OptOutVariable = "UNITY_CLI_ALLOW_UNAUTHENTICATED";
        internal static readonly string Token = CreateToken();

        private static string CreateToken()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        internal static bool IsAuthorized(string supplied)
        {
            return IsAuthorized(supplied, Environment.GetEnvironmentVariable(OptOutVariable));
        }

        internal static bool IsAuthorized(string supplied, string optOut)
        {
            // A wrong credential is never silently downgraded to legacy access.
            if (string.IsNullOrEmpty(supplied)) return optOut == "1";
            if (supplied.Length != Token.Length) return false;
            var difference = 0;
            for (var i = 0; i < Token.Length; i++) difference |= supplied[i] ^ Token[i];
            return difference == 0;
        }
    }
}
