using System;
using System.Security.Cryptography;

namespace DLS.Multiplayer
{
	/// <summary>Static helpers for the HMAC-SHA256 handshake authentication.</summary>
	public static class AuthHelper
	{
		private const int NonceSize = 16;

		/// <summary>Generate a cryptographically random 16-byte nonce.</summary>
		public static byte[] GenerateNonce()
		{
			byte[] nonce = new byte[NonceSize];
			using RandomNumberGenerator rng = RandomNumberGenerator.Create();
			rng.GetBytes(nonce);
			return nonce;
		}

		/// <summary>
		/// Computes HMAC-SHA256(password, nonceServer || nonceClient).
		/// </summary>
		public static byte[] ComputeToken(string password, byte[] nonceServer, byte[] nonceClient)
		{
			byte[] key     = System.Text.Encoding.UTF8.GetBytes(password ?? string.Empty);
			byte[] message = new byte[nonceServer.Length + nonceClient.Length];
			Buffer.BlockCopy(nonceServer, 0, message, 0,                nonceServer.Length);
			Buffer.BlockCopy(nonceClient, 0, message, nonceServer.Length, nonceClient.Length);

			using HMACSHA256 hmac = new(key);
			return hmac.ComputeHash(message);
		}

		/// <summary>
		/// Constant-time comparison to prevent timing attacks.
		/// Returns true only if both arrays have the same length and identical contents.
		/// </summary>
		public static bool VerifyToken(byte[] expected, byte[] actual)
		{
			if (expected == null || actual == null || expected.Length != actual.Length)
				return false;

			int diff = 0;
			for (int i = 0; i < expected.Length; i++)
				diff |= expected[i] ^ actual[i];

			return diff == 0;
		}
	}
}
