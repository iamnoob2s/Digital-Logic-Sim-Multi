using System;
using System.IO;
using UnityEngine;

namespace DLS.Multiplayer
{
	/// <summary>
	/// Binary serialization helpers for the multiplayer protocol.
	/// Wire format: [4-byte little-endian length][1-byte MessageType][payload bytes]
	/// </summary>
	public static class NetSerializer
	{
		// ---- Frame encode / decode ----

		/// <summary>Prepends a 4-byte length header and 1-byte type to <paramref name="payload"/>.</summary>
		public static byte[] WriteMessage(MessageType type, byte[] payload)
		{
			payload ??= Array.Empty<byte>();
			// total length = 1 (type) + payload
			int totalLength = 1 + payload.Length;
			using MemoryStream ms = new(4 + totalLength);
			using BinaryWriter w = new(ms);
			w.Write(totalLength);          // 4-byte length prefix
			w.Write((byte)type);           // 1-byte message type
			w.Write(payload);
			return ms.ToArray();
		}

		/// <summary>
		/// Attempts to read one complete framed message from <paramref name="buffer"/>.
		/// Returns true and sets <paramref name="msg"/> and <paramref name="consumed"/> on success.
		/// Returns false when not enough bytes are available yet.
		/// </summary>
		public static bool TryReadMessage(byte[] buffer, int available, out NetMessage msg, out int consumed)
		{
			msg = null;
			consumed = 0;

			// Need at least 4 bytes for the length header
			if (available < 4) return false;

			int bodyLength = BitConverter.ToInt32(buffer, 0);
			if (bodyLength < 1) return false; // malformed: need at least 1 byte for type

			int totalRequired = 4 + bodyLength;
			if (available < totalRequired) return false;

			MessageType type = (MessageType)buffer[4];
			int payloadLength = bodyLength - 1;
			byte[] payload = new byte[payloadLength];
			if (payloadLength > 0)
				Buffer.BlockCopy(buffer, 5, payload, 0, payloadLength);

			msg = new NetMessage(type, payload);
			consumed = totalRequired;
			return true;
		}

		// ---- Type helpers ----

		public static void WriteGuid(BinaryWriter w, Guid g)
		{
			w.Write(g.ToByteArray());
		}

		public static Guid ReadGuid(BinaryReader r)
		{
			return new Guid(r.ReadBytes(16));
		}

		public static void WriteVector2(BinaryWriter w, Vector2 v)
		{
			w.Write(v.x);
			w.Write(v.y);
		}

		public static Vector2 ReadVector2(BinaryReader r)
		{
			return new Vector2(r.ReadSingle(), r.ReadSingle());
		}

		/// <summary>Writes a UTF-8 string prefixed with a 2-byte length.</summary>
		public static void WriteString(BinaryWriter w, string s)
		{
			s ??= string.Empty;
			byte[] bytes = System.Text.Encoding.UTF8.GetBytes(s);
			w.Write((ushort)bytes.Length);
			w.Write(bytes);
		}

		public static string ReadString(BinaryReader r)
		{
			ushort len = r.ReadUInt16();
			byte[] bytes = r.ReadBytes(len);
			return System.Text.Encoding.UTF8.GetString(bytes);
		}
	}
}
