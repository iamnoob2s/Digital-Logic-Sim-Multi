using System;
using DLS.Multiplayer;
using UnityEngine;
using UnityEngine.UI;

namespace DLS.Multiplayer.UI
{
	/// <summary>
	/// Multiplayer connect/host menu panel.
	/// Keyboard shortcut M toggles the panel.
	/// </summary>
	public class MultiplayerMenuUI : MonoBehaviour
	{
		[Header("Panel")]
		public GameObject Panel;

		[Header("Input fields")]
		public InputField IpPortField;
		public InputField PasswordField;
		public InputField UsernameField;

		[Header("Buttons")]
		public Button HostButton;
		public Button JoinButton;
		public Button CancelButton;

		[Header("Status")]
		public Text StatusText;

		void Awake()
		{
			if (HostButton)   HostButton.onClick.AddListener(OnHostClicked);
			if (JoinButton)   JoinButton.onClick.AddListener(OnJoinClicked);
			if (CancelButton) CancelButton.onClick.AddListener(OnCancelClicked);

			if (Panel) Panel.SetActive(false);

			SubscribeToNetworkEvents();
		}

		void OnDestroy()
		{
			UnsubscribeFromNetworkEvents();
		}

		void Update()
		{
			if (Input.GetKeyDown(KeyCode.M))
				Toggle();
		}

		// ---- Public ----

		public void Toggle()
		{
			if (Panel) Panel.SetActive(!Panel.activeSelf);
		}

		// ---- Button handlers ----

		void OnHostClicked()
		{
			string raw = IpPortField ? IpPortField.text.Trim() : string.Empty;
			string password = PasswordField ? PasswordField.text : string.Empty;

			if (!TryParsePort(raw, out int port))
			{
				SetStatus("Invalid port. Enter a port number or ip:port.");
				return;
			}

			SetStatus($"Hosting on port {port}…");
			NetworkManager.Instance?.StartHost(port, password);
		}

		void OnJoinClicked()
		{
			string raw      = IpPortField   ? IpPortField.text.Trim()   : string.Empty;
			string password = PasswordField  ? PasswordField.text        : string.Empty;
			string username = UsernameField  ? UsernameField.text.Trim() : "Player";

			if (string.IsNullOrEmpty(username)) username = "Player";

			if (!TryParseIpPort(raw, out string ip, out int port))
			{
				SetStatus("Invalid address. Format: ip:port");
				return;
			}

			SetStatus("Connecting…");
			NetworkManager.Instance?.Connect(ip, port, password, username);
		}

		void OnCancelClicked()
		{
			if (NetworkSession.Instance != null && NetworkSession.Instance.IsInSession)
			{
				if (NetworkSession.Instance.IsHost)
					NetworkManager.Instance?.StopHost();
				else
					NetworkManager.Instance?.Disconnect();
			}

			SetStatus("Disconnected.");
		}

		// ---- Network event handlers ----

		void SubscribeToNetworkEvents()
		{
			if (NetworkManager.Instance == null) return;
			NetworkManager.Instance.OnConnectionFailed += OnConnectionFailed;
			NetworkManager.Instance.OnDisconnected      += OnNetDisconnected;
		}

		void UnsubscribeFromNetworkEvents()
		{
			if (NetworkManager.Instance == null) return;
			NetworkManager.Instance.OnConnectionFailed -= OnConnectionFailed;
			NetworkManager.Instance.OnDisconnected      -= OnNetDisconnected;
		}

		void OnConnectionFailed(string reason)
		{
			SetStatus($"Failed: {reason}");
		}

		void OnNetDisconnected()
		{
			SetStatus("Disconnected.");
		}

		// ---- Helpers ----

		void SetStatus(string text)
		{
			if (StatusText) StatusText.text = text;
		}

		static bool TryParsePort(string raw, out int port)
		{
			// Accept "port" or "ip:port" (host only needs port)
			if (raw.Contains(':'))
			{
				string[] parts = raw.Split(':');
				return int.TryParse(parts[parts.Length - 1], out port) && port > 0 && port < 65536;
			}
			return int.TryParse(raw, out port) && port > 0 && port < 65536;
		}

		static bool TryParseIpPort(string raw, out string ip, out int port)
		{
			ip   = string.Empty;
			port = 0;

			int lastColon = raw.LastIndexOf(':');
			if (lastColon < 0) return false;

			ip = raw.Substring(0, lastColon).Trim();
			string portStr = raw.Substring(lastColon + 1).Trim();

			if (string.IsNullOrEmpty(ip)) return false;
			return int.TryParse(portStr, out port) && port > 0 && port < 65536;
		}
	}
}
