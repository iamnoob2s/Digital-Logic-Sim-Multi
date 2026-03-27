using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DLS.Multiplayer.UI
{
	/// <summary>
	/// Always-visible session overlay displayed while a multiplayer session is active.
	/// Shows host/client role, connected players, and average ping.
	/// </summary>
	public class SessionHUD : MonoBehaviour
	{
		[Tooltip("Text component to display HUD information.")]
		public Text HudText;

		[Tooltip("Root object shown/hidden based on session state.")]
		public GameObject HudRoot;

		const float UpdateInterval = 1f;
		float _nextUpdateTime;

		void Update()
		{
			bool inSession = NetworkSession.Instance != null && NetworkSession.Instance.IsInSession;

			if (HudRoot && HudRoot.activeSelf != inSession)
				HudRoot.SetActive(inSession);

			if (!inSession) return;

			if (Time.time >= _nextUpdateTime)
			{
				_nextUpdateTime = Time.time + UpdateInterval;
				RefreshHUD();
			}
		}

		void RefreshHUD()
		{
			if (HudText == null) return;

			NetworkSession session = NetworkSession.Instance;
			if (session == null) return;

			string role = session.IsHost ? "Host" : "Client";

			StringBuilder sb = new();
			sb.Append("🟢 ");
			sb.Append(role);
			sb.Append(" | Players: ");

			List<PlayerInfo> players = session.Players;
			for (int i = 0; i < players.Count; i++)
			{
				if (i > 0) sb.Append(", ");
				sb.Append(players[i].Name);
			}

			// Average ping (host shows per-client ping, client shows self ping)
			int avgPing = ComputeAveragePing();
			if (avgPing >= 0)
			{
				sb.Append(" | Ping: ");
				sb.Append(avgPing);
				sb.Append("ms");
			}

			HudText.text = sb.ToString();
		}

		static int ComputeAveragePing()
		{
			// Ping tracking is stored per-player via PlayerInfo.PingMs
			NetworkSession session = NetworkSession.Instance;
			if (session == null || session.Players.Count == 0) return -1;

			int total = 0;
			int count = 0;
			foreach (PlayerInfo p in session.Players)
			{
				if (p.Id == session.LocalPlayerId) continue; // skip self
				total += p.PingMs;
				count++;
			}
			return count == 0 ? -1 : total / count;
		}
	}
}
