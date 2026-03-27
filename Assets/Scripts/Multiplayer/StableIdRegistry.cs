using System;
using System.Collections.Generic;
using UnityEngine;

namespace DLS.Multiplayer
{
	/// <summary>
	/// Singleton registry that maps stable network GUIDs to Unity GameObjects (chips, wires, pins).
	/// All access must occur on the Unity main thread.
	/// </summary>
	public class StableIdRegistry : MonoBehaviour
	{
		public static StableIdRegistry Instance { get; private set; }

		readonly Dictionary<Guid, GameObject> _chips = new();
		readonly Dictionary<Guid, GameObject> _wires = new();
		readonly Dictionary<Guid, GameObject> _pins  = new();

		void Awake()
		{
			if (Instance != null && Instance != this)
			{
				Destroy(this);
				return;
			}
			Instance = this;
		}

		// ---- Chips ----

		public void RegisterChip(Guid id, GameObject go)   => _chips[id] = go;
		public void UnregisterChip(Guid id)                => _chips.Remove(id);
		public bool TryGetChip(Guid id, out GameObject go) => _chips.TryGetValue(id, out go);

		// ---- Wires ----

		public void RegisterWire(Guid id, GameObject go)   => _wires[id] = go;
		public void UnregisterWire(Guid id)                => _wires.Remove(id);
		public bool TryGetWire(Guid id, out GameObject go) => _wires.TryGetValue(id, out go);

		// ---- Pins ----

		public void RegisterPin(Guid id, GameObject go)   => _pins[id] = go;
		public void UnregisterPin(Guid id)                => _pins.Remove(id);
		public bool TryGetPin(Guid id, out GameObject go) => _pins.TryGetValue(id, out go);

		// ---- Generic (tries chips, then wires, then pins) ----

		/// <summary>Registers an object in the chips dictionary by default. Use the type-specific methods for wires and pins.</summary>
		public void Register(Guid id, GameObject go)
		{
			_chips[id] = go;
		}

		public void Unregister(Guid id)
		{
			_chips.Remove(id);
			_wires.Remove(id);
			_pins.Remove(id);
		}

		public bool TryGet(Guid id, out GameObject go)
		{
			if (_chips.TryGetValue(id, out go)) return true;
			if (_wires.TryGetValue(id, out go)) return true;
			if (_pins.TryGetValue(id, out go))  return true;
			return false;
		}

		public void Clear()
		{
			_chips.Clear();
			_wires.Clear();
			_pins.Clear();
		}
	}
}
