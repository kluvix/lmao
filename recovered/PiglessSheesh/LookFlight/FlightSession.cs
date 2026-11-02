using System;
using System.Collections.Generic;
using BepInEx.Logging;
using GorillaLocomotion;
using UnityEngine;

namespace LookFlight;

internal sealed class FlightSession
{
	private readonly ManualLogSource logger;

	private GTPlayer player;

	private Rigidbody body;

	private bool savedGravity;

	private bool savedCollisions;

	private bool savedDisableMovement;

	private LayerMask savedLocomotionLayers;

	private Vector3 flightVelocity;

	private bool restoreFailed;

	private readonly Dictionary<Collider, bool> colliderStates = new Dictionary<Collider, bool>();

	internal bool Active { get; private set; }

	internal bool IsValid
	{
		get
		{
			if (Active && player != null && body != null && player == GTPlayer.Instance)
			{
				return body == player.playerRigidBody;
			}
			return false;
		}
	}

	internal Camera HeadCamera
	{
		get
		{
			if (!IsValid)
			{
				return null;
			}
			return player.mainCamera;
		}
	}

	internal FlightSession(UnityEngine.Object owner, ManualLogSource logger)
	{
		if (owner == null)
		{
			throw new ArgumentNullException("owner");
		}
		this.logger = logger ?? throw new ArgumentNullException("logger");
	}

	internal bool TryStart(GTPlayer candidate)
	{
		if (Active)
		{
			if (IsValid && candidate == player)
			{
				return true;
			}
			Stop();
		}
		if (candidate == null || candidate != GTPlayer.Instance || candidate.playerRigidBody == null)
		{
			logger.LogWarning("Flight could not start: local player or Rigidbody is not ready.");
			return false;
		}
		Rigidbody playerRigidBody = candidate.playerRigidBody;
		if (playerRigidBody.isKinematic)
		{
			logger.LogWarning("Flight could not start: the game currently owns a kinematic player Rigidbody.");
			return false;
		}
		bool useGravity = playerRigidBody.useGravity;
		bool detectCollisions = playerRigidBody.detectCollisions;
		bool disableMovement = candidate.disableMovement;
		LayerMask locomotionEnabledLayers = candidate.locomotionEnabledLayers;
		Dictionary<Collider, bool> dictionary = new Dictionary<Collider, bool>();
		if (candidate.headCollider != null)
		{
			dictionary[candidate.headCollider] = candidate.headCollider.enabled;
		}
		if (candidate.bodyCollider != null)
		{
			dictionary[candidate.bodyCollider] = candidate.bodyCollider.enabled;
		}
		player = candidate;
		body = playerRigidBody;
		savedGravity = useGravity;
		savedCollisions = detectCollisions;
		savedDisableMovement = disableMovement;
		savedLocomotionLayers = locomotionEnabledLayers;
		colliderStates.Clear();
		foreach (KeyValuePair<Collider, bool> item in dictionary)
		{
			colliderStates.Add(item.Key, item.Value);
		}
		flightVelocity = Vector3.zero;
		Active = true;
		try
		{
			player.locomotionEnabledLayers = 0;
			player.disableMovement = true;
			foreach (KeyValuePair<Collider, bool> colliderState in colliderStates)
			{
				if (colliderState.Key != null)
				{
					colliderState.Key.enabled = false;
				}
			}
			body.useGravity = false;
			body.detectCollisions = false;
			body.linearVelocity = Vector3.zero;
			body.angularVelocity = Vector3.zero;
			player.SetPlayerVelocity(Vector3.zero);
			logger.LogInfo("Flight ON. Player: " + player.name + "; collision query mask disabled.");
			return true;
		}
		catch (Exception ex)
		{
			logger.LogError("Flight start failed; restoring saved player state: " + ex);
			Stop();
			return false;
		}
	}

	internal void SetVelocity(Vector3 velocity)
	{
		if (!IsValid)
		{
			Stop();
			return;
		}
		body.useGravity = false;
		body.linearVelocity = velocity;
		body.angularVelocity = Vector3.zero;
		flightVelocity = velocity;
	}

	internal void Stop(bool preserveMomentum = false)
	{
		if (!Active)
		{
			return;
		}
		bool flag = preserveMomentum && IsValid && !body.isKinematic;
		Vector3 exitVelocity = (flag ? flightVelocity : Vector3.zero);
		Active = false;
		restoreFailed = false;
		if (body != null)
		{
			Restore("Rigidbody velocity", delegate
			{
				body.linearVelocity = exitVelocity;
			});
			Restore("Rigidbody angular velocity", delegate
			{
				body.angularVelocity = Vector3.zero;
			});
		}
		if (player != null)
		{
			Restore("locomotion collision mask", delegate
			{
				player.locomotionEnabledLayers = savedLocomotionLayers;
			});
			Restore("movement state", delegate
			{
				player.disableMovement = savedDisableMovement;
			});
			Restore("player velocity history", delegate
			{
				player.SetPlayerVelocity(exitVelocity);
			});
		}
		foreach (KeyValuePair<Collider, bool> colliderState in colliderStates)
		{
			if (!(colliderState.Key == null))
			{
				Collider collider = colliderState.Key;
				bool enabled = colliderState.Value;
				Restore("player collider", delegate
				{
					collider.enabled = enabled;
				});
			}
		}
		colliderStates.Clear();
		if (body != null)
		{
			Restore("Rigidbody gravity", delegate
			{
				body.useGravity = savedGravity;
			});
			Restore("Rigidbody collisions", delegate
			{
				body.detectCollisions = savedCollisions;
			});
		}
		player = null;
		body = null;
		flightVelocity = Vector3.zero;
		logger.LogInfo(restoreFailed ? "Flight OFF; some player state could not be restored (see warnings)." : (flag ? "Flight OFF; momentum carried forward, saved movement, collision and gravity states restored." : "Flight OFF; saved movement, collision and gravity states restored."));
	}

	private void Restore(string state, Action restore)
	{
		try
		{
			restore();
		}
		catch (Exception ex)
		{
			restoreFailed = true;
			logger.LogWarning("Could not restore " + state + ": " + ex);
		}
	}
}
