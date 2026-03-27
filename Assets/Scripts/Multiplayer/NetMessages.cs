using System;
using System.IO;
using UnityEngine;

namespace DLS.Multiplayer
{
	public enum MessageType : byte
	{
		Handshake         = 0x01,
		HandshakeChallenge = 0x02,
		HandshakeResponse  = 0x03,
		HandshakeAccept    = 0x04,
		HandshakeReject    = 0x05,
		FullSnapshot       = 0x10,
		SnapshotReady      = 0x11,
		PlaceChip          = 0x20,
		DeleteChip         = 0x21,
		AddWire            = 0x22,
		DeleteWire         = 0x23,
		SetPinState        = 0x24,
		MoveChip           = 0x25,
		SetProperty        = 0x26,
		Ping               = 0x30,
		Pong               = 0x31,
		PlayerJoined       = 0x32,
		PlayerLeft         = 0x33,
		Disconnect         = 0x34,
	}

	public class NetMessage
	{
		public MessageType Type;
		public byte[] Payload;
		public int SenderId; // set by NetworkManager when received from a client

		public NetMessage(MessageType type, byte[] payload)
		{
			Type = type;
			Payload = payload ?? Array.Empty<byte>();
		}
	}

	// ---- Payload structs ----

	public struct HandshakePayload
	{
		public byte ProtocolVersion;
		public string Username;
		public byte[] NonceClient;

		public const byte CurrentVersion = 1;

		public byte[] Serialize()
		{
			using MemoryStream ms = new();
			using BinaryWriter w = new(ms);
			w.Write(ProtocolVersion);
			NetSerializer.WriteString(w, Username);
			w.Write((byte)NonceClient.Length);
			w.Write(NonceClient);
			return ms.ToArray();
		}

		public static HandshakePayload Deserialize(byte[] data)
		{
			using MemoryStream ms = new(data);
			using BinaryReader r = new(ms);
			return new HandshakePayload
			{
				ProtocolVersion = r.ReadByte(),
				Username        = NetSerializer.ReadString(r),
				NonceClient     = r.ReadBytes(r.ReadByte()),
			};
		}
	}

	public struct HandshakeChallengePayload
	{
		public byte[] NonceServer;

		public byte[] Serialize()
		{
			using MemoryStream ms = new();
			using BinaryWriter w = new(ms);
			w.Write((byte)NonceServer.Length);
			w.Write(NonceServer);
			return ms.ToArray();
		}

		public static HandshakeChallengePayload Deserialize(byte[] data)
		{
			using MemoryStream ms = new(data);
			using BinaryReader r = new(ms);
			return new HandshakeChallengePayload { NonceServer = r.ReadBytes(r.ReadByte()) };
		}
	}

	public struct HandshakeResponsePayload
	{
		public byte[] Token; // HMAC-SHA256(password, nonceServer || nonceClient)

		public byte[] Serialize()
		{
			using MemoryStream ms = new();
			using BinaryWriter w = new(ms);
			w.Write((byte)Token.Length);
			w.Write(Token);
			return ms.ToArray();
		}

		public static HandshakeResponsePayload Deserialize(byte[] data)
		{
			using MemoryStream ms = new(data);
			using BinaryReader r = new(ms);
			return new HandshakeResponsePayload { Token = r.ReadBytes(r.ReadByte()) };
		}
	}

	public struct HandshakeAcceptPayload
	{
		public int AssignedPlayerId;
		public string ServerVersion;

		public byte[] Serialize()
		{
			using MemoryStream ms = new();
			using BinaryWriter w = new(ms);
			w.Write(AssignedPlayerId);
			NetSerializer.WriteString(w, ServerVersion);
			return ms.ToArray();
		}

		public static HandshakeAcceptPayload Deserialize(byte[] data)
		{
			using MemoryStream ms = new(data);
			using BinaryReader r = new(ms);
			return new HandshakeAcceptPayload
			{
				AssignedPlayerId = r.ReadInt32(),
				ServerVersion    = NetSerializer.ReadString(r),
			};
		}
	}

	public struct HandshakeRejectPayload
	{
		public string Reason;

		public byte[] Serialize()
		{
			using MemoryStream ms = new();
			using BinaryWriter w = new(ms);
			NetSerializer.WriteString(w, Reason);
			return ms.ToArray();
		}

		public static HandshakeRejectPayload Deserialize(byte[] data)
		{
			using MemoryStream ms = new(data);
			using BinaryReader r = new(ms);
			return new HandshakeRejectPayload { Reason = NetSerializer.ReadString(r) };
		}
	}

	public struct PlaceChipPayload
	{
		public Guid ChipId;
		public string ChipName;
		public Vector2 Position;

		public byte[] Serialize()
		{
			using MemoryStream ms = new();
			using BinaryWriter w = new(ms);
			NetSerializer.WriteGuid(w, ChipId);
			NetSerializer.WriteString(w, ChipName);
			NetSerializer.WriteVector2(w, Position);
			return ms.ToArray();
		}

		public static PlaceChipPayload Deserialize(byte[] data)
		{
			using MemoryStream ms = new(data);
			using BinaryReader r = new(ms);
			return new PlaceChipPayload
			{
				ChipId   = NetSerializer.ReadGuid(r),
				ChipName = NetSerializer.ReadString(r),
				Position = NetSerializer.ReadVector2(r),
			};
		}
	}

	public struct DeleteChipPayload
	{
		public Guid ChipId;

		public byte[] Serialize()
		{
			using MemoryStream ms = new();
			using BinaryWriter w = new(ms);
			NetSerializer.WriteGuid(w, ChipId);
			return ms.ToArray();
		}

		public static DeleteChipPayload Deserialize(byte[] data)
		{
			using MemoryStream ms = new(data);
			using BinaryReader r = new(ms);
			return new DeleteChipPayload { ChipId = NetSerializer.ReadGuid(r) };
		}
	}

	public struct AddWirePayload
	{
		public Guid WireId;
		public Guid SourceChipId;
		public int SourcePinIndex;
		public Guid TargetChipId;
		public int TargetPinIndex;
		public Vector2[] Points;

		public byte[] Serialize()
		{
			using MemoryStream ms = new();
			using BinaryWriter w = new(ms);
			NetSerializer.WriteGuid(w, WireId);
			NetSerializer.WriteGuid(w, SourceChipId);
			w.Write(SourcePinIndex);
			NetSerializer.WriteGuid(w, TargetChipId);
			w.Write(TargetPinIndex);
			w.Write(Points != null ? Points.Length : 0);
			if (Points != null)
				foreach (Vector2 pt in Points)
					NetSerializer.WriteVector2(w, pt);
			return ms.ToArray();
		}

		public static AddWirePayload Deserialize(byte[] data)
		{
			using MemoryStream ms = new(data);
			using BinaryReader r = new(ms);
			var p = new AddWirePayload
			{
				WireId         = NetSerializer.ReadGuid(r),
				SourceChipId   = NetSerializer.ReadGuid(r),
				SourcePinIndex = r.ReadInt32(),
				TargetChipId   = NetSerializer.ReadGuid(r),
				TargetPinIndex = r.ReadInt32(),
			};
			int count = r.ReadInt32();
			p.Points = new Vector2[count];
			for (int i = 0; i < count; i++) p.Points[i] = NetSerializer.ReadVector2(r);
			return p;
		}
	}

	public struct DeleteWirePayload
	{
		public Guid WireId;

		public byte[] Serialize()
		{
			using MemoryStream ms = new();
			using BinaryWriter w = new(ms);
			NetSerializer.WriteGuid(w, WireId);
			return ms.ToArray();
		}

		public static DeleteWirePayload Deserialize(byte[] data)
		{
			using MemoryStream ms = new(data);
			using BinaryReader r = new(ms);
			return new DeleteWirePayload { WireId = NetSerializer.ReadGuid(r) };
		}
	}

	public struct SetPinStatePayload
	{
		public Guid ChipId;
		public int PinIndex;
		public uint State;

		public byte[] Serialize()
		{
			using MemoryStream ms = new();
			using BinaryWriter w = new(ms);
			NetSerializer.WriteGuid(w, ChipId);
			w.Write(PinIndex);
			w.Write(State);
			return ms.ToArray();
		}

		public static SetPinStatePayload Deserialize(byte[] data)
		{
			using MemoryStream ms = new(data);
			using BinaryReader r = new(ms);
			return new SetPinStatePayload
			{
				ChipId   = NetSerializer.ReadGuid(r),
				PinIndex = r.ReadInt32(),
				State    = r.ReadUInt32(),
			};
		}
	}

	public struct MoveChipPayload
	{
		public Guid ChipId;
		public Vector2 NewPosition;

		public byte[] Serialize()
		{
			using MemoryStream ms = new();
			using BinaryWriter w = new(ms);
			NetSerializer.WriteGuid(w, ChipId);
			NetSerializer.WriteVector2(w, NewPosition);
			return ms.ToArray();
		}

		public static MoveChipPayload Deserialize(byte[] data)
		{
			using MemoryStream ms = new(data);
			using BinaryReader r = new(ms);
			return new MoveChipPayload
			{
				ChipId      = NetSerializer.ReadGuid(r),
				NewPosition = NetSerializer.ReadVector2(r),
			};
		}
	}

	public struct SetPropertyPayload
	{
		public Guid ObjectId;
		public string Key;
		public string Value;

		public byte[] Serialize()
		{
			using MemoryStream ms = new();
			using BinaryWriter w = new(ms);
			NetSerializer.WriteGuid(w, ObjectId);
			NetSerializer.WriteString(w, Key);
			NetSerializer.WriteString(w, Value);
			return ms.ToArray();
		}

		public static SetPropertyPayload Deserialize(byte[] data)
		{
			using MemoryStream ms = new(data);
			using BinaryReader r = new(ms);
			return new SetPropertyPayload
			{
				ObjectId = NetSerializer.ReadGuid(r),
				Key      = NetSerializer.ReadString(r),
				Value    = NetSerializer.ReadString(r),
			};
		}
	}

	public struct PlayerJoinedPayload
	{
		public int PlayerId;
		public string Username;

		public byte[] Serialize()
		{
			using MemoryStream ms = new();
			using BinaryWriter w = new(ms);
			w.Write(PlayerId);
			NetSerializer.WriteString(w, Username);
			return ms.ToArray();
		}

		public static PlayerJoinedPayload Deserialize(byte[] data)
		{
			using MemoryStream ms = new(data);
			using BinaryReader r = new(ms);
			return new PlayerJoinedPayload
			{
				PlayerId = r.ReadInt32(),
				Username = NetSerializer.ReadString(r),
			};
		}
	}

	public struct PlayerLeftPayload
	{
		public int PlayerId;

		public byte[] Serialize()
		{
			using MemoryStream ms = new();
			using BinaryWriter w = new(ms);
			w.Write(PlayerId);
			return ms.ToArray();
		}

		public static PlayerLeftPayload Deserialize(byte[] data)
		{
			using MemoryStream ms = new(data);
			using BinaryReader r = new(ms);
			return new PlayerLeftPayload { PlayerId = r.ReadInt32() };
		}
	}
}
