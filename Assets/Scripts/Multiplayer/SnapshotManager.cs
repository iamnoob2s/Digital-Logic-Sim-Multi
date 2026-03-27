using System;
using System.Text;
using DLS.Description;
using DLS.Game;
using DLS.SaveSystem;
using UnityEngine;

namespace DLS.Multiplayer
{
	/// <summary>
	/// Handles join-in-progress snapshots: serialising the current circuit and reloading it on remote peers.
	/// </summary>
	public class SnapshotManager : MonoBehaviour
	{
		public static SnapshotManager Instance { get; private set; }

		void Awake()
		{
			if (Instance != null && Instance != this) { Destroy(this); return; }
			Instance = this;
		}

		/// <summary>
		/// Serialises the currently viewed chip to JSON bytes.
		/// Called on the host when a new client connects.
		/// </summary>
		public byte[] CreateSnapshot()
		{
			try
			{
				Project project = Project.ActiveProject;
				if (project == null) return Array.Empty<byte>();

				ChipDescription desc = DescriptionCreator.CreateChipDescription(project.ViewedChip);
				string json = Saver.CreateSerializedChipDescription(desc);
				return Encoding.UTF8.GetBytes(json);
			}
			catch (Exception e)
			{
				Debug.LogError($"[Net] SnapshotManager.CreateSnapshot error: {e.Message}");
				return Array.Empty<byte>();
			}
		}

		/// <summary>
		/// Deserialises snapshot bytes and updates the chip library with the received description.
		/// Called on a client when it receives a FullSnapshot message.
		/// Must be called from the Unity main thread.
		/// </summary>
		public void ApplySnapshot(byte[] data)
		{
			if (data == null || data.Length == 0) return;

			try
			{
				string json = Encoding.UTF8.GetString(data);
				ChipDescription desc = Serializer.DeserializeChipDescription(json);

				Project project = Project.ActiveProject;
				if (project == null) return;

				// Update the chip description in the library using the available API
				project.chipLibrary.NotifyChipSaved(desc);

				// Rebuild StableIdRegistry for all objects in the loaded chip
				StableIdRegistry.Instance?.Clear();

				// TODO: hook networking here — trigger a full reload of the viewed chip using
				// DevChipInstance.LoadFromDescriptionTest(desc, project.chipLibrary) and
				// update Simulator state to match the received snapshot.
			}
			catch (Exception e)
			{
				Debug.LogError($"[Net] SnapshotManager.ApplySnapshot error: {e.Message}");
			}
		}
	}
}
