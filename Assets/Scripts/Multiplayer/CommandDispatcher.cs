using UnityEngine;

namespace DLS.Multiplayer
{
	/// <summary>
	/// Processes incoming NetMessage commands on the Unity main thread.
	/// Guards against re-broadcast loops via <see cref="IsApplyingRemote"/>.
	/// </summary>
	public class CommandDispatcher : MonoBehaviour
	{
		public static CommandDispatcher Instance { get; private set; }

		static bool _applyingRemote;

		/// <summary>True while a remote command is being applied locally (prevents re-broadcast).</summary>
		public static bool IsApplyingRemote => _applyingRemote;

		void Awake()
		{
			if (Instance != null && Instance != this) { Destroy(this); return; }
			Instance = this;
		}

		/// <summary>Called by NetworkManager (host path) — apply locally and relay to other clients.</summary>
		public void DispatchFromHost(int senderId, NetMessage msg)
		{
			switch (msg.Type)
			{
				case MessageType.PlayerJoined:
				{
					PlayerJoinedPayload p = PlayerJoinedPayload.Deserialize(msg.Payload);
					NetworkSession.Instance?.AddPlayer(new PlayerInfo { Id = p.PlayerId, Name = p.Username });
					break;
				}
				case MessageType.PlayerLeft:
				{
					PlayerLeftPayload p = PlayerLeftPayload.Deserialize(msg.Payload);
					NetworkSession.Instance?.RemovePlayer(p.PlayerId);
					break;
				}
				case MessageType.PlaceChip:
				case MessageType.DeleteChip:
				case MessageType.AddWire:
				case MessageType.DeleteWire:
				case MessageType.SetPinState:
				case MessageType.MoveChip:
				case MessageType.SetProperty:
				{
					_applyingRemote = true;
					try   { ApplyCircuitCommand(msg); }
					finally { _applyingRemote = false; }

					// Relay to all other authenticated clients
					NetworkManager.Instance?.SendToAllExcept(senderId, msg);
					break;
				}
				default:
					break;
			}
		}

		/// <summary>Called by NetworkManager (client path) — apply locally only.</summary>
		public void DispatchFromRemote(NetMessage msg)
		{
			switch (msg.Type)
			{
				case MessageType.PlaceChip:
				case MessageType.DeleteChip:
				case MessageType.AddWire:
				case MessageType.DeleteWire:
				case MessageType.SetPinState:
				case MessageType.MoveChip:
				case MessageType.SetProperty:
				{
					_applyingRemote = true;
					try   { ApplyCircuitCommand(msg); }
					finally { _applyingRemote = false; }
					break;
				}
				default:
					break;
			}
		}

		void ApplyCircuitCommand(NetMessage msg)
		{
			// NOTE: Full implementation of live circuit mutation requires direct access to
			// ChipInteractionController and the active DevChipInstance.  The hooks in
			// ChipInteractionController.cs broadcast commands when _isLocalAction is true;
			// here we apply commands that arrive from remote peers.
			// The actual object lookup uses StableIdRegistry when GameObjects are involved.

			switch (msg.Type)
			{
				case MessageType.PlaceChip:
				{
					PlaceChipPayload p = PlaceChipPayload.Deserialize(msg.Payload);
					// TODO: hook networking here — place chip at p.Position with name p.ChipName and assign p.ChipId as NetworkId
					break;
				}
				case MessageType.DeleteChip:
				{
					DeleteChipPayload p = DeleteChipPayload.Deserialize(msg.Payload);
					// TODO: hook networking here — look up chip via StableIdRegistry and delete it
					break;
				}
				case MessageType.AddWire:
				{
					AddWirePayload p = AddWirePayload.Deserialize(msg.Payload);
					// TODO: hook networking here — connect pins identified by p.SourceChipId/p.TargetChipId and assign p.WireId
					break;
				}
				case MessageType.DeleteWire:
				{
					DeleteWirePayload p = DeleteWirePayload.Deserialize(msg.Payload);
					// TODO: hook networking here — look up wire via StableIdRegistry and delete it
					break;
				}
				case MessageType.SetPinState:
				{
					SetPinStatePayload p = SetPinStatePayload.Deserialize(msg.Payload);
					// TODO: hook networking here — set pin state on dev pin identified by p.ChipId/p.PinIndex
					break;
				}
				case MessageType.MoveChip:
				{
					MoveChipPayload p = MoveChipPayload.Deserialize(msg.Payload);
					// TODO: hook networking here — move chip to p.NewPosition
					break;
				}
				case MessageType.SetProperty:
				{
					SetPropertyPayload p = SetPropertyPayload.Deserialize(msg.Payload);
					// TODO: hook networking here — apply property change p.Key = p.Value on object p.ObjectId
					break;
				}
			}
		}
	}
}
