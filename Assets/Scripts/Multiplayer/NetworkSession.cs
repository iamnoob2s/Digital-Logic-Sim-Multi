using System.Collections.Generic;
using UnityEngine;

namespace DLS.Multiplayer
{
/// <summary>Holds per-session state shared across multiplayer subsystems.</summary>
public class NetworkSession
{
public static NetworkSession Instance { get; private set; }

public bool IsHost        { get; private set; }
public bool IsConnected   { get; private set; }
public bool IsInSession   { get; private set; }

public string LocalPlayerName { get; private set; }
public int    LocalPlayerId   { get; private set; }

public List<PlayerInfo> Players { get; } = new();

int _nextSequenceNumber;
public int NextSequenceNumber => _nextSequenceNumber++;

public static void CreateInstance()
{
Instance ??= new NetworkSession();
}

public void StartSession(bool isHost)
{
IsHost      = isHost;
IsConnected = true;
IsInSession = true;
Players.Clear();
_nextSequenceNumber = 0;
}

public void EndSession()
{
IsHost      = false;
IsConnected = false;
IsInSession = false;
Players.Clear();
}

public void AddPlayer(PlayerInfo info)
{
if (!Players.Exists(p => p.Id == info.Id))
Players.Add(info);
}

public void RemovePlayer(int id)
{
Players.RemoveAll(p => p.Id == id);
}

public void SetLocalPlayer(int id, string name)
{
LocalPlayerId   = id;
LocalPlayerName = name;
}

/// <summary>Updates the cached world-space cursor position for a remote player.</summary>
public void UpdatePlayerCursor(int playerId, Vector2 worldPos)
{
// Players list is only written on the main thread so iteration is safe
foreach (PlayerInfo p in Players)
{
if (p.Id == playerId)
{
p.CursorWorldPos = worldPos;
p.HasCursor      = true;
return;
}
}
}
}

public class PlayerInfo
{
public int     Id;
public string  Name;
public int     PingMs;
/// <summary>Latest known world-space mouse position for this player. Updated via <see cref="MouseMove"/> messages.</summary>
public Vector2 CursorWorldPos;
/// <summary>True once we have received at least one cursor update.</summary>
public bool    HasCursor;
}
}
