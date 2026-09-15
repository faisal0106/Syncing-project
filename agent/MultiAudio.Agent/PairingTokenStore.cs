using System;
using System.IO;
using System.Security.Cryptography;

namespace MultiAudio.Agent
{
    /// <summary>
    /// Generates and persists the pairing token used for the HELLO
    /// handshake (rules.md #9, PROTOCOL.md "Authentication").
    ///
    /// V1 approach (see docs/design.md §16 "First-Run Experience"):
    /// the token is a random value written to a file under the user's
    /// local-app-data directory the first time the agent starts, and
    /// re-used on subsequent runs. The web UI retrieves it via
    /// ControlServer's loopback-only GET /pairing-token endpoint.
    ///
    /// This is intentionally simple for V1. It is NOT a substitute for
    /// Origin validation — ControlServer still checks Origin on every
    /// WebSocket upgrade — it only solves "how does the browser learn
    /// the token" for a same-machine first run.
    /// </summary>
    public static class PairingTokenStore
    {
        private static string TokenFilePath
        {
            get
            {
                var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var dir = Path.Combine(baseDir, "MultiAudio");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "pairing.token");
            }
        }

        public static string GetOrCreateToken()
        {
            var path = TokenFilePath;

            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(existing))
                    return existing;
            }

            var token = GenerateToken();
            File.WriteAllText(path, token);
            return token;
        }

        private static string GenerateToken()
        {
            // 32 random bytes, base64url-encoded — long enough to not be
            // guessable, short enough to be readable if a user ever needs
            // to paste it manually.
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }
    }
}
