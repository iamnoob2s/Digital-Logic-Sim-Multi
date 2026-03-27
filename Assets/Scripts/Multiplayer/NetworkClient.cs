using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace DLS.Multiplayer
{
/// <summary>
/// Represents one connected client on the host side.
/// Socket reads run on a dedicated background thread.
/// MouseMove messages are routed to a separate queue so they never block command processing.
/// </summary>
public class NetworkClient
{
public int           PlayerId        { get; set; }
public string        Username        { get; set; }
public TcpClient     TcpClient       { get; set; }
public NetworkStream Stream          { get; set; }
public Thread        ReadThread      { get; private set; }
/// <summary>TickCount64 (ms) when the last ping was sent. Thread-safe — uses Environment.TickCount64.</summary>
public long          LastPingTick    { get; set; }
public float         LastPingTime    { get; set; } // updated on main thread only
public bool          IsAuthenticated { get; set; }
public int           PingMs          { get; set; }

volatile bool _running;
readonly object _sendLock = new();

// Pre-allocated pong frame to avoid per-ping allocation on the read thread
static readonly byte[] _pongFrame = NetSerializer.WriteMessage(MessageType.Pong, Array.Empty<byte>());

public void StartReading(
ConcurrentQueue<(int playerId, NetMessage msg)> incomingQueue,
ConcurrentQueue<(int playerId, NetMessage msg)> mouseMoveQueue)
{
_running   = true;
ReadThread = new Thread(() => ReadLoop(incomingQueue, mouseMoveQueue))
{
IsBackground = true,
Name         = $"Net-Client-{PlayerId}",
};
ReadThread.Start();
}

public void Send(byte[] data)
{
if (Stream == null || !_running) return;
try
{
lock (_sendLock)
{
Stream.Write(data, 0, data.Length);
}
}
catch (Exception e)
{
Debug.LogError($"[Net] Client {PlayerId} send error: {e.Message}");
}
}

public void Disconnect()
{
_running = false;
try { Stream?.Close(); }    catch { /* ignored */ }
try { TcpClient?.Close(); } catch { /* ignored */ }
}

void ReadLoop(
ConcurrentQueue<(int playerId, NetMessage msg)> queue,
ConcurrentQueue<(int playerId, NetMessage msg)> mouseMoveQueue)
{
byte[] buffer = new byte[65536];
int    offset = 0;

while (_running)
{
try
{
int read = Stream.Read(buffer, offset, buffer.Length - offset);
if (read == 0)
{
queue.Enqueue((PlayerId, new NetMessage(MessageType.Disconnect, Array.Empty<byte>())));
break;
}

offset += read;

while (NetSerializer.TryReadMessage(buffer, offset, out NetMessage msg, out int consumed))
{
switch (msg.Type)
{
case MessageType.Pong:
PingMs = (int)(Environment.TickCount64 - LastPingTick);
break;
case MessageType.Ping:
Send(_pongFrame);
break;
case MessageType.MouseMove:
// Route to the dedicated cursor queue (never blocks commands)
mouseMoveQueue.Enqueue((PlayerId, msg));
break;
default:
queue.Enqueue((PlayerId, msg));
break;
}

offset -= consumed;
if (offset > 0) Buffer.BlockCopy(buffer, consumed, buffer, 0, offset);
}
}
catch (Exception e)
{
if (_running) Debug.LogError($"[Net] Client {PlayerId} read error: {e.Message}");
queue.Enqueue((PlayerId, new NetMessage(MessageType.Disconnect, Array.Empty<byte>())));
break;
}
}
}
}
}
