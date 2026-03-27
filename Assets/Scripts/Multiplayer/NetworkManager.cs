using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace DLS.Multiplayer
{
	/// <summary>
	/// Core networking MonoBehaviour.  Handles both host and client roles.
	/// All socket I/O happens on background threads; commands are dispatched on the Unity main thread via Update().
	/// </summary>
	public class NetworkManager : MonoBehaviour
	{
		public static NetworkManager Instance { get; private set; }

		/// <summary>
		/// Maximum number of simultaneously connected clients (excluding the host).
		/// Range: 1–15 clients, giving 2–16 total players including the host.
		/// Default 7 = 8 players total.
		/// </summary>
		[SerializeField, Range(1, 15)]
		int maxClients = 7;

		/// <summary>Static accessor used by <see cref="PlayerCursorManager"/> and other subsystems.</summary>
		public static int MaxClients => Instance != null ? Instance.maxClients : 7;

		// ---- Events ----
		public event Action<string>      OnConnectionFailed;
		public event Action              OnDisconnected;
		public event Action<PlayerInfo>  OnPlayerJoined;
		public event Action<int>         OnPlayerLeft;

		// ---- State ----
		bool _isRunning;
		string _password;

		// ---- Host state ----
		TcpListener _listener;
		Thread _acceptThread;
		readonly List<NetworkClient> _clients = new();
		readonly object _clientsLock = new();
		int _nextPlayerId = 1;

		// ---- Client state ----
		TcpClient _clientSocket;
		Thread _clientReadThread;
		NetworkStream _clientStream;

		// Shared receive buffers — one per connection role (accessed only from their respective read thread)
		byte[] _clientRecvBuffer = new byte[65536];
		int _clientRecvOffset;

		// ---- Queues ----
		// Regular game commands and session messages (drained on main thread, max 50/frame)
		readonly ConcurrentQueue<NetMessage> _incomingQueue = new();
		// Host path: carries sender id alongside the message
		readonly ConcurrentQueue<(int senderId, NetMessage msg)> _hostIncomingQueue = new();
		// MouseMove messages are stored separately so they never get stale in a backed-up queue
		readonly ConcurrentQueue<(int senderId, NetMessage msg)> _mouseMoveQueue = new();

		// Pre-allocated ping/pong frames (written once, never modified)
		static readonly byte[] _pingFrame = NetSerializer.WriteMessage(MessageType.Ping, Array.Empty<byte>());
		static readonly byte[] _pongFrame = NetSerializer.WriteMessage(MessageType.Pong, Array.Empty<byte>());

		// ---- Ping tracking ----
		const float PingInterval = 5f;
		const float PingTimeout  = 15f;
		float _nextPingTime;

		void Awake()
		{
			if (Instance != null && Instance != this) { Destroy(this); return; }
			Instance = this;
			NetworkSession.CreateInstance();
		}

		void OnDestroy()
		{
			StopHost();
			Disconnect();
		}

		void OnApplicationQuit()
		{
			StopHost();
			Disconnect();
		}

		// ---- Public API ----

		public void StartHost(int port, string password)
		{
			if (_isRunning)
			{
				Debug.LogError("[Net] Already running — call StopHost() first.");
				return;
			}

			_password  = password;
			_isRunning = true;

			NetworkSession.Instance.StartSession(true);
			NetworkSession.Instance.SetLocalPlayer(0, NetworkSession.Instance.LocalPlayerName ?? "Host");
			NetworkSession.Instance.AddPlayer(new PlayerInfo { Id = 0, Name = NetworkSession.Instance.LocalPlayerName ?? "Host" });

			try
			{
				_listener = new TcpListener(IPAddress.Any, port);
				_listener.Start();
				Debug.Log($"[Net] Hosting on port {port}");
			}
			catch (Exception e)
			{
				Debug.LogError($"[Net] Failed to start host: {e.Message}");
				_isRunning = false;
				NetworkSession.Instance.EndSession();
				return;
			}

			_acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "Net-Accept" };
			_acceptThread.Start();
		}

		public void StopHost()
		{
			if (!_isRunning) return;
			_isRunning = false;

			try { _listener?.Stop(); } catch { /* ignored */ }
			_listener = null;

			lock (_clientsLock)
			{
				foreach (NetworkClient c in _clients) c.Disconnect();
				_clients.Clear();
			}

			NetworkSession.Instance?.EndSession();
		}

		/// <summary>Connects as a client. Runs the handshake on a background thread and fires events on completion.</summary>
		public void Connect(string ip, int port, string password, string username)
		{
			if (_isRunning)
			{
				Debug.LogError("[Net] Already connected — call Disconnect() first.");
				return;
			}

			_password  = password;
			_isRunning = true;

			NetworkSession.Instance.SetLocalPlayer(-1, username);

			Thread t = new(() => ConnectWorker(ip, port, username)) { IsBackground = true, Name = "Net-Connect" };
			t.Start();
		}

		public void Disconnect()
		{
			if (!_isRunning && _clientSocket == null) return;
			_isRunning = false;

			if (_clientStream != null)
			{
				byte[] bye = NetSerializer.WriteMessage(MessageType.Disconnect, Array.Empty<byte>());
				try { _clientStream.Write(bye, 0, bye.Length); } catch { /* ignored */ }
			}

			try { _clientSocket?.Close(); } catch { /* ignored */ }
			_clientSocket = null;
			_clientStream  = null;

			NetworkSession.Instance?.EndSession();
			OnDisconnected?.Invoke();
		}

		// ---- Send helpers ----

		public void SendToHost(NetMessage msg)
		{
			if (_clientStream == null) return;
			byte[] data = NetSerializer.WriteMessage(msg.Type, msg.Payload);
			try { _clientStream.Write(data, 0, data.Length); }
			catch (Exception e) { Debug.LogError($"[Net] SendToHost error: {e.Message}"); }
		}

		public void SendToAll(NetMessage msg)
		{
			byte[] data = NetSerializer.WriteMessage(msg.Type, msg.Payload);
			BroadcastRaw(data);
		}

		public void SendToAllExcept(int playerId, NetMessage msg)
		{
			byte[] data = NetSerializer.WriteMessage(msg.Type, msg.Payload);
			lock (_clientsLock)
			{
				foreach (NetworkClient c in _clients)
					if (c.IsAuthenticated && c.PlayerId != playerId) c.Send(data);
			}
		}

		/// <summary>Broadcasts a pre-framed byte array to all authenticated clients. Zero allocations on the hot path.</summary>
		public void BroadcastRaw(byte[] framedData)
		{
			lock (_clientsLock)
			{
				foreach (NetworkClient c in _clients)
					if (c.IsAuthenticated) c.Send(framedData);
			}
		}

		/// <summary>Sends a pre-framed byte array directly to the host stream. Zero allocations on the hot path.</summary>
		public void SendRawToHost(byte[] framedData)
		{
			if (_clientStream == null) return;
			try { _clientStream.Write(framedData, 0, framedData.Length); }
			catch (Exception e) { Debug.LogError($"[Net] SendRawToHost error: {e.Message}"); }
		}

		/// <summary>Sends a command: if host, broadcast to all clients; if client, send to host.</summary>
		public void SendCommand(NetMessage msg)
		{
			if (NetworkSession.Instance == null || !NetworkSession.Instance.IsInSession) return;
			if (NetworkSession.Instance.IsHost) SendToAll(msg);
			else SendToHost(msg);
		}

		// ---- Unity Update ----

		void Update()
		{
			// Mouse-move messages are drained first (latest position per sender) to keep cursors snappy
			DrainMouseMoveQueue();
			DrainHostQueue();
			DrainClientQueue();
			HandlePings();
		}

		/// <summary>
		/// Drain mouse-move messages, keeping only the most recent position per player.
		/// This prevents cursor updates from backing up in the queue when the frame rate is low.
		/// </summary>
		void DrainMouseMoveQueue()
		{
			// Collect latest positions keyed by sender (discards stale entries)
			while (_mouseMoveQueue.TryDequeue(out (int senderId, NetMessage msg) item))
			{
				if (item.msg.Payload.Length >= MouseMovePayload.SerializedSize)
				{
					MouseMovePayload p = MouseMovePayload.Deserialize(item.msg.Payload);
					PlayerCursorManager.Instance?.OnRemoteMouseMove(p.PlayerId, p.WorldPosition);

					// Host relays mouse-move to all other clients with zero extra allocations
					if (NetworkSession.Instance != null && NetworkSession.Instance.IsHost)
					{
						SendToAllExcept(item.senderId, item.msg);
					}
				}
			}
		}

		void DrainHostQueue()
		{
			int processed = 0;
			while (processed < 50 && _hostIncomingQueue.TryDequeue(out (int senderId, NetMessage msg) item))
			{
				processed++;
				CommandDispatcher.Instance?.DispatchFromHost(item.senderId, item.msg);
			}
		}

		void DrainClientQueue()
		{
			int processed = 0;
			while (processed < 50 && _incomingQueue.TryDequeue(out NetMessage msg))
			{
				processed++;
				HandleClientMessage(msg);
			}
		}

		void HandleClientMessage(NetMessage msg)
		{
			switch (msg.Type)
			{
				case MessageType.PlayerJoined:
				{
					PlayerJoinedPayload p = PlayerJoinedPayload.Deserialize(msg.Payload);
					PlayerInfo info = new() { Id = p.PlayerId, Name = p.Username };
					NetworkSession.Instance?.AddPlayer(info);
					OnPlayerJoined?.Invoke(info);
					break;
				}
				case MessageType.PlayerLeft:
				{
					PlayerLeftPayload p = PlayerLeftPayload.Deserialize(msg.Payload);
					NetworkSession.Instance?.RemovePlayer(p.PlayerId);
					OnPlayerLeft?.Invoke(p.PlayerId);
					break;
				}
				case MessageType.Disconnect:
				{
					Disconnect();
					break;
				}
				case MessageType.FullSnapshot:
				{
					SnapshotManager.Instance?.ApplySnapshot(msg.Payload);
					// After snapshot applied, signal ready
					byte[] ready = NetSerializer.WriteMessage(MessageType.SnapshotReady, Array.Empty<byte>());
					SendRawToHost(ready);
					break;
				}
				default:
					CommandDispatcher.Instance?.DispatchFromRemote(msg);
					break;
			}
		}

		void HandlePings()
		{
			if (!_isRunning) return;
			if (Time.time < _nextPingTime) return;
			_nextPingTime = Time.time + PingInterval;

			if (NetworkSession.Instance == null) return;

			if (NetworkSession.Instance.IsHost)
			{
				List<NetworkClient> toRemove = null;

				lock (_clientsLock)
				{
					foreach (NetworkClient c in _clients)
					{
						if (!c.IsAuthenticated) continue;

						if (c.LastPingTime > 0 && Time.time - c.LastPingTime > PingTimeout)
						{
							toRemove ??= new List<NetworkClient>();
							toRemove.Add(c);
							continue;
						}

						c.LastPingTime  = Time.time;
						c.LastPingTick  = Environment.TickCount64;
						c.Send(_pingFrame);
					}

					if (toRemove != null)
					{
						foreach (NetworkClient c in toRemove)
						{
							_clients.Remove(c);
							c.Disconnect();
							BroadcastPlayerLeft(c.PlayerId);
						}
					}
				}
			}
			else if (_clientStream != null)
			{
				try { _clientStream.Write(_pingFrame, 0, _pingFrame.Length); } catch { /* ignored */ }
			}
		}

		void BroadcastPlayerLeft(int playerId)
		{
			PlayerLeftPayload p = new() { PlayerId = playerId };
			NetMessage msg = new(MessageType.PlayerLeft, p.Serialize());
			SendToAll(msg);
			NetworkSession.Instance?.RemovePlayer(playerId);
		}

		// ---- Host accept loop (background thread) ----

		void AcceptLoop()
		{
			while (_isRunning)
			{
				try
				{
					TcpClient tcp = _listener.AcceptTcpClient();
					lock (_clientsLock)
					{
						if (_clients.Count >= maxClients)
						{
							tcp.Close();
							continue;
						}
					}
					Thread handshakeThread = new(() => HandleNewClientHandshake(tcp))
					{
						IsBackground = true,
						Name         = "Net-Handshake",
					};
					handshakeThread.Start();
				}
				catch (SocketException)
				{
					break; // listener stopped
				}
				catch (Exception e)
				{
					if (_isRunning) Debug.LogError($"[Net] AcceptLoop error: {e.Message}");
				}
			}
		}

		void HandleNewClientHandshake(TcpClient tcp)
		{
			NetworkStream stream = tcp.GetStream();
			stream.ReadTimeout  = 10000;
			stream.WriteTimeout = 10000;

			try
			{
				// 1. Receive Handshake from client
				NetMessage handshakeMsg = ReadNextMessage(stream);
				if (handshakeMsg == null || handshakeMsg.Type != MessageType.Handshake)
				{
					tcp.Close();
					return;
				}
				HandshakePayload hs = HandshakePayload.Deserialize(handshakeMsg.Payload);

				// 2. Send challenge with server nonce
				byte[] nonceServer = AuthHelper.GenerateNonce();
				HandshakeChallengePayload challenge = new() { NonceServer = nonceServer };
				byte[] challengeBytes = NetSerializer.WriteMessage(MessageType.HandshakeChallenge, challenge.Serialize());
				stream.Write(challengeBytes, 0, challengeBytes.Length);

				// 3. Receive response token
				NetMessage responseMsg = ReadNextMessage(stream);
				if (responseMsg == null || responseMsg.Type != MessageType.HandshakeResponse)
				{
					tcp.Close();
					return;
				}
				HandshakeResponsePayload response = HandshakeResponsePayload.Deserialize(responseMsg.Payload);

				// 4. Verify token
				byte[] expected = AuthHelper.ComputeToken(_password, nonceServer, hs.NonceClient);
				if (!AuthHelper.VerifyToken(expected, response.Token))
				{
					HandshakeRejectPayload reject = new() { Reason = "Invalid password." };
					byte[] rejectBytes = NetSerializer.WriteMessage(MessageType.HandshakeReject, reject.Serialize());
					stream.Write(rejectBytes, 0, rejectBytes.Length);
					tcp.Close();
					return;
				}

				// 5. Assign player id and accept
				int playerId;
				lock (_clientsLock) { playerId = _nextPlayerId++; }

				HandshakeAcceptPayload accept = new()
				{
					AssignedPlayerId = playerId,
					ServerVersion    = HandshakePayload.CurrentVersion.ToString(),
				};
				byte[] acceptBytes = NetSerializer.WriteMessage(MessageType.HandshakeAccept, accept.Serialize());
				stream.Write(acceptBytes, 0, acceptBytes.Length);

				// 6. Create and register the client
				stream.ReadTimeout  = -1; // no timeout for ongoing connection
				stream.WriteTimeout = -1;

				NetworkClient client = new()
				{
					PlayerId        = playerId,
					Username        = hs.Username,
					TcpClient       = tcp,
					Stream          = stream,
					IsAuthenticated = true,
				};

				lock (_clientsLock) { _clients.Add(client); }

				// Notify session about new player (main thread via queue)
				PlayerJoinedPayload joinPayload = new() { PlayerId = playerId, Username = hs.Username };
				_hostIncomingQueue.Enqueue((0, new NetMessage(MessageType.PlayerJoined, joinPayload.Serialize())));

				// Broadcast to existing clients
				NetMessage joinMsg = new(MessageType.PlayerJoined, joinPayload.Serialize());
				SendToAllExcept(playerId, joinMsg);

				// Send snapshot
				byte[] snapshot = SnapshotManager.Instance != null
					? SnapshotManager.Instance.CreateSnapshot()
					: Array.Empty<byte>();
				byte[] snapMsg = NetSerializer.WriteMessage(MessageType.FullSnapshot, snapshot);
				client.Send(snapMsg);

				// Begin reading
				client.StartReading(_hostIncomingQueue, _mouseMoveQueue);
			}
			catch (Exception e)
			{
				if (_isRunning) Debug.LogError($"[Net] Handshake error: {e.Message}");
				try { tcp.Close(); } catch { /* ignored */ }
			}
		}

		// ---- Client connect worker (background thread) ----

		void ConnectWorker(string ip, int port, string username)
		{
			try
			{
				_clientSocket = new TcpClient();
				_clientSocket.Connect(ip, port);
				_clientStream = _clientSocket.GetStream();
				_clientStream.ReadTimeout  = 10000;
				_clientStream.WriteTimeout = 10000;

				// 1. Send Handshake
				byte[] nonceClient = AuthHelper.GenerateNonce();
				HandshakePayload hs = new()
				{
					ProtocolVersion = HandshakePayload.CurrentVersion,
					Username        = username,
					NonceClient     = nonceClient,
				};
				byte[] hsBytes = NetSerializer.WriteMessage(MessageType.Handshake, hs.Serialize());
				_clientStream.Write(hsBytes, 0, hsBytes.Length);

				// 2. Receive challenge
				NetMessage challengeMsg = ReadNextMessage(_clientStream);
				if (challengeMsg == null || challengeMsg.Type != MessageType.HandshakeChallenge)
				{
					FailConnect("Server sent unexpected message during handshake.");
					return;
				}
				HandshakeChallengePayload challenge = HandshakeChallengePayload.Deserialize(challengeMsg.Payload);

				// 3. Compute and send token
				byte[] token = AuthHelper.ComputeToken(_password, challenge.NonceServer, nonceClient);
				HandshakeResponsePayload response = new() { Token = token };
				byte[] respBytes = NetSerializer.WriteMessage(MessageType.HandshakeResponse, response.Serialize());
				_clientStream.Write(respBytes, 0, respBytes.Length);

				// 4. Receive accept/reject
				NetMessage resultMsg = ReadNextMessage(_clientStream);
				if (resultMsg == null)
				{
					FailConnect("Connection closed during handshake.");
					return;
				}

				if (resultMsg.Type == MessageType.HandshakeReject)
				{
					HandshakeRejectPayload reject = HandshakeRejectPayload.Deserialize(resultMsg.Payload);
					FailConnect(reject.Reason);
					return;
				}

				if (resultMsg.Type != MessageType.HandshakeAccept)
				{
					FailConnect("Unexpected handshake result.");
					return;
				}

				HandshakeAcceptPayload acceptPayload = HandshakeAcceptPayload.Deserialize(resultMsg.Payload);

				// Success — apply session state on main thread
				_incomingQueue.Enqueue(new NetMessage(MessageType.HandshakeAccept, resultMsg.Payload)
				{
					SenderId = acceptPayload.AssignedPlayerId,
				});

				_clientStream.ReadTimeout  = -1;
				_clientStream.WriteTimeout = -1;

				NetworkSession.Instance.StartSession(false);
				NetworkSession.Instance.SetLocalPlayer(acceptPayload.AssignedPlayerId, username);

				// Start reading loop
				_clientReadThread = new Thread(ClientReadLoop)
				{
					IsBackground = true,
					Name         = "Net-ClientRead",
				};
				_clientReadThread.Start();
			}
			catch (Exception e)
			{
				FailConnect(e.Message);
			}
		}

		void FailConnect(string reason)
		{
			_isRunning = false;
			try { _clientSocket?.Close(); } catch { /* ignored */ }
			_clientSocket = null;
			_clientStream  = null;

			// Enqueue failure notification for main thread
			_incomingQueue.Enqueue(new NetMessage(MessageType.HandshakeReject,
				new HandshakeRejectPayload { Reason = reason }.Serialize()));
		}

		void ClientReadLoop()
		{
			byte[] buffer = new byte[65536];
			int    offset = 0;

			while (_isRunning && _clientStream != null)
			{
				try
				{
					int read = _clientStream.Read(buffer, offset, buffer.Length - offset);
					if (read == 0) { _incomingQueue.Enqueue(new NetMessage(MessageType.Disconnect, Array.Empty<byte>())); break; }

					offset += read;

					while (NetSerializer.TryReadMessage(buffer, offset, out NetMessage msg, out int consumed))
					{
						// Route mouse-move messages to their dedicated queue
						if (msg.Type == MessageType.MouseMove)
							_mouseMoveQueue.Enqueue((0, msg));
						else if (msg.Type == MessageType.Ping)
							try { _clientStream.Write(_pongFrame, 0, _pongFrame.Length); } catch { /* ignored */ }
						else if (msg.Type == MessageType.Pong)
							{ /* RTT tracking done by host-side NetworkClient */ }
						else
							_incomingQueue.Enqueue(msg);

						offset -= consumed;
						if (offset > 0) Buffer.BlockCopy(buffer, consumed, buffer, 0, offset);
					}
				}
				catch (Exception e)
				{
					if (_isRunning) Debug.LogError($"[Net] ClientReadLoop error: {e.Message}");
					_incomingQueue.Enqueue(new NetMessage(MessageType.Disconnect, Array.Empty<byte>()));
					break;
				}
			}
		}

		// ---- Utility ----

		/// <summary>Synchronously reads the next complete framed message from <paramref name="stream"/>.</summary>
		static NetMessage ReadNextMessage(NetworkStream stream)
		{
			byte[] header = new byte[4];
			if (!ReadExact(stream, header, 4)) return null;

			int bodyLength = BitConverter.ToInt32(header, 0);
			if (bodyLength < 1) return null;

			byte[] body = new byte[bodyLength];
			if (!ReadExact(stream, body, bodyLength)) return null;

			MessageType type    = (MessageType)body[0];
			int payloadLength   = bodyLength - 1;
			byte[] payload      = new byte[payloadLength];
			if (payloadLength > 0) Buffer.BlockCopy(body, 1, payload, 0, payloadLength);

			return new NetMessage(type, payload);
		}

		static bool ReadExact(NetworkStream stream, byte[] buf, int count)
		{
			int total = 0;
			while (total < count)
			{
				int read = stream.Read(buf, total, count - total);
				if (read == 0) return false;
				total += read;
			}
			return true;
		}
	}
}
