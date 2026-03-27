using System.Collections.Generic;
using Seb.Helpers;
using UnityEngine;

namespace DLS.Multiplayer
{
	/// <summary>
	/// Broadcasts the local player's world-space mouse position to all peers and renders
	/// remote players' cursors as coloured circles with name labels using Unity's OnGUI.
	///
	/// Send rate is capped at <see cref="SendHz"/> to avoid flooding the network.
	/// </summary>
	public class PlayerCursorManager : MonoBehaviour
	{
		public static PlayerCursorManager Instance { get; private set; }

		/// <summary>Maximum mouse-move broadcasts per second per client.</summary>
		public const float SendHz = 20f;

		/// <summary>
		/// Total player cap (clients + host) reflecting the current <see cref="NetworkManager.MaxClients"/> setting.
		/// Evaluated at runtime so inspector changes are reflected immediately.
		/// </summary>
		public static int MaxPlayers => NetworkManager.MaxClients + 1;

		// Pre-allocated framed byte array for MouseMove messages (reused every broadcast)
		// Frame: [4-byte length][1-byte type][12-byte payload] = 17 bytes
		static readonly byte[] _mouseMoveFrame = new byte[4 + 1 + MouseMovePayload.SerializedSize];

		// Distinct colours assigned round-robin to players (excluding local player)
		static readonly Color[] PlayerColours =
		{
			new(0.20f, 0.72f, 1.00f),  // blue
			new(1.00f, 0.55f, 0.10f),  // orange
			new(0.30f, 0.90f, 0.40f),  // green
			new(1.00f, 0.30f, 0.30f),  // red
			new(0.85f, 0.30f, 1.00f),  // purple
			new(1.00f, 0.95f, 0.20f),  // yellow
			new(0.20f, 0.95f, 0.85f),  // teal
			new(1.00f, 0.50f, 0.70f),  // pink
			new(0.80f, 1.00f, 0.20f),  // lime
			new(0.60f, 0.40f, 0.20f),  // brown
			new(0.70f, 0.70f, 0.70f),  // grey
			new(1.00f, 0.80f, 0.50f),  // gold
			new(0.20f, 0.50f, 1.00f),  // deep blue
			new(0.50f, 0.90f, 0.60f),  // mint
			new(1.00f, 0.40f, 0.60f),  // rose
		};

		// Static so it survives if the component is recreated (e.g. on scene reload)
		static readonly Dictionary<int, int> _playerColourIndex = new();
		static int _colourCounter;

		float _nextSendTime;

		// Reusable GUI style to avoid allocations in OnGUI
		GUIStyle _labelStyle;
		Texture2D _dotTexture;

		void Awake()
		{
			if (Instance != null && Instance != this) { Destroy(this); return; }
			Instance = this;

			// Static-init the frame header (written once, payload overwritten each send)
			int bodyLen = 1 + MouseMovePayload.SerializedSize; // type byte + payload
			_mouseMoveFrame[0] = (byte)(bodyLen);
			_mouseMoveFrame[1] = (byte)(bodyLen >> 8);
			_mouseMoveFrame[2] = (byte)(bodyLen >> 16);
			_mouseMoveFrame[3] = (byte)(bodyLen >> 24);
			_mouseMoveFrame[4] = (byte)MessageType.MouseMove;
		}

		void OnEnable()
		{
			if (NetworkManager.Instance != null)
				NetworkManager.Instance.OnDisconnected += OnSessionEnded;
		}

		void OnDisable()
		{
			if (NetworkManager.Instance != null)
				NetworkManager.Instance.OnDisconnected -= OnSessionEnded;
		}

		void OnDestroy()
		{
			if (_dotTexture != null) Destroy(_dotTexture);
			if (Instance == this) Instance = null;
		}

		/// <summary>Called when the session ends; clears colour assignments so reconnecting players get consistent colours.</summary>
		static void OnSessionEnded()
		{
			_playerColourIndex.Clear();
			_colourCounter = 0;
		}

		// ---- Sending ----

		void Update()
		{
			if (NetworkSession.Instance == null || !NetworkSession.Instance.IsInSession) return;
			if (Time.time < _nextSendTime) return;

			_nextSendTime = Time.time + 1f / SendHz;
			BroadcastMousePosition();
		}

		static void BroadcastMousePosition()
		{
			NetworkSession session = NetworkSession.Instance;
			if (session == null) return;

			MouseMovePayload payload = new()
			{
				PlayerId      = session.LocalPlayerId,
				WorldPosition = InputHelper.MousePosWorld,
			};

			// Write payload directly into the pre-allocated frame buffer
			payload.SerializeInto(_mouseMoveFrame, 5);

			if (session.IsHost)
			{
				// Host broadcasts to all authenticated clients
				NetworkManager.Instance?.BroadcastRaw(_mouseMoveFrame);
			}
			else
			{
				// Client sends to host; host will relay
				NetworkManager.Instance?.SendRawToHost(_mouseMoveFrame);
			}
		}

		// ---- Receiving ----

		/// <summary>Called on the main thread by CommandDispatcher when a MouseMove message arrives.</summary>
		public void OnRemoteMouseMove(int playerId, Vector2 worldPos)
		{
			NetworkSession.Instance?.UpdatePlayerCursor(playerId, worldPos);
		}

		// ---- Rendering ----

		void OnGUI()
		{
			if (NetworkSession.Instance == null || !NetworkSession.Instance.IsInSession) return;

			Camera cam = Camera.main;
			if (cam == null) return;

			EnsureGUIResources();

			List<PlayerInfo> players = NetworkSession.Instance.Players;
			int localId = NetworkSession.Instance.LocalPlayerId;

			foreach (PlayerInfo player in players)
			{
				if (player.Id == localId || !player.HasCursor) continue;

				// Convert world-space cursor position to screen position
				Vector3 screenPos = cam.WorldToScreenPoint(new Vector3(player.CursorWorldPos.x, player.CursorWorldPos.y, 0f));

				// Unity GUI uses top-left origin; screen uses bottom-left
				float guiY = Screen.height - screenPos.y;

				if (screenPos.z < 0) continue; // cursor is behind the camera

				Color col = GetPlayerColour(player.Id);

				// Draw a filled circle (dot) for the cursor
				const float dotRadius = 6f;
				GUI.color = col;
				GUI.DrawTexture(new Rect(screenPos.x - dotRadius, guiY - dotRadius, dotRadius * 2, dotRadius * 2), _dotTexture);

				// Draw player name below the dot
				GUI.color = col;
				_labelStyle.normal.textColor = col;
				GUI.Label(new Rect(screenPos.x + dotRadius + 2f, guiY - 8f, 120f, 20f), player.Name, _labelStyle);
			}

			// Reset GUI colour
			GUI.color = Color.white;
		}

		void EnsureGUIResources()
		{
			if (_dotTexture == null)
			{
				_dotTexture = CreateCircleTexture(16, Color.white);
			}

			if (_labelStyle == null)
			{
				_labelStyle = new GUIStyle(GUI.skin.label)
				{
					fontSize  = 11,
					fontStyle = FontStyle.Bold,
				};
				_labelStyle.normal.textColor = Color.white;
			}
		}

		static Texture2D CreateCircleTexture(int size, Color col)
		{
			Texture2D tex = new(size, size, TextureFormat.RGBA32, false);
			tex.filterMode = FilterMode.Bilinear;

			float r     = size / 2f;
			float rSq   = r * r;
			Color clear = new(0, 0, 0, 0);

			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float dx = x - r + 0.5f;
					float dy = y - r + 0.5f;
					tex.SetPixel(x, y, dx * dx + dy * dy <= rSq ? col : clear);
				}
			}

			tex.Apply();
			return tex;
		}

		// ---- Helpers ----

		static Color GetPlayerColour(int playerId)
		{
			if (!_playerColourIndex.TryGetValue(playerId, out int idx))
			{
				idx = _colourCounter % PlayerColours.Length;
				_colourCounter++;
				_playerColourIndex[playerId] = idx;
			}
			return PlayerColours[idx];
		}
	}
}
