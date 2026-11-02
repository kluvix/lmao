using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using GorillaGameModes;
using GorillaLocomotion;
using Pigless.Visuals;
using UnityEngine;

namespace Pigless.Sheesh;

internal sealed class TagTools : IDisposable
{
	private const float AimDistance = 60f;

	private readonly ManualLogSource log;

	private readonly PressEdge shotEdge = new PressEdge();

	private readonly RaycastHit[] hits = new RaycastHit[64];

	private readonly HashSet<int> visited = new HashSet<int>();

	private readonly object[] validationArgs = new object[6];

	private readonly object[] localTagArgs = new object[4];

	private readonly object[] addArgs = new object[2];

	private Type infectionType;

	private Type propType;

	private PropertyInfo activeManager;

	private PropertyInfo networkHandler;

	private MethodInfo validateTag;

	private MethodInfo localTag;

	private MethodInfo addInfected;

	private FieldInfo infectedField;

	private FieldInfo infectedArrayField;

	private FieldInfo tagModeField;

	private FieldInfo endingField;

	private FieldInfo propOwner;

	private bool apiReady;

	private bool apiUnavailable;

	private bool disposed;

	private float nextAction;

	private float statusUntil;

	private float nextLog;

	private float nextLabel;

	private WorldUi ui;

	private Transform beam;

	private Transform dot;

	private Transform statusPanel;

	private Component statusLabel;

	internal bool GunEnabled;

	internal string Status { get; private set; } = "TAG GUN OFF";

	internal TagTools(ManualLogSource logger)
	{
		log = logger ?? throw new ArgumentNullException("logger");
	}

	private void Message(string text, float duration = 3f)
	{
		Status = text;
		statusUntil = Time.unscaledTime + duration;
		nextLabel = 0f;
		log.LogInfo("Tag tools result: " + text + ".");
	}

	private bool EnsureApi()
	{
		if (apiReady)
		{
			return true;
		}
		if (apiUnavailable)
		{
			return false;
		}
		try
		{
			Assembly assembly = typeof(VRRig).Assembly;
			infectionType = assembly.GetType("GorillaTagManager", throwOnError: true);
			propType = assembly.GetType("PropHuntTaggableProp", throwOnError: false);
			propOwner = propType?.GetField("ownerRig", BindingFlags.Instance | BindingFlags.Public);
			Type type = assembly.GetType("GorillaGameManager", throwOnError: true);
			activeManager = typeof(GameMode).GetProperty("ActiveGameMode", BindingFlags.Public | BindingFlags.Static);
			networkHandler = typeof(GameMode).GetProperty("ActiveNetworkHandler", BindingFlags.NonPublic | BindingFlags.Static);
			validateTag = typeof(GorillaTagger).GetMethod("TryToTag", BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[6]
			{
				typeof(Collider),
				typeof(bool),
				typeof(bool),
				typeof(float),
				typeof(NetPlayer).MakeByRefType(),
				typeof(NetPlayer).MakeByRefType()
			}, null);
			localTag = type.GetMethod("LocalTag", BindingFlags.Instance | BindingFlags.Public, null, new Type[4]
			{
				typeof(NetPlayer),
				typeof(NetPlayer),
				typeof(bool),
				typeof(bool)
			}, null);
			addInfected = infectionType.GetMethod("AddInfectedPlayer", BindingFlags.Instance | BindingFlags.Public, null, new Type[2]
			{
				typeof(NetPlayer),
				typeof(bool)
			}, null);
			infectedField = infectionType.GetField("currentInfected", BindingFlags.Instance | BindingFlags.Public);
			infectedArrayField = infectionType.GetField("currentInfectedArray", BindingFlags.Instance | BindingFlags.Public);
			tagModeField = infectionType.GetField("isCurrentlyTag", BindingFlags.Instance | BindingFlags.Public);
			endingField = infectionType.GetField("waitingToStartNextInfectionGame", BindingFlags.Instance | BindingFlags.Public);
			if (activeManager == null || networkHandler == null || validateTag == null || localTag == null || addInfected == null || infectedField == null || infectedArrayField == null || tagModeField == null || endingField == null)
			{
				throw new MissingMemberException("The supplied game's tag API is incomplete.");
			}
			apiReady = true;
			log.LogInfo("Tag tools API ready: existing-host infection authority and normal validated tag requests. No authority changes.");
			return true;
		}
		catch (Exception error)
		{
			apiUnavailable = true;
			Fail("Tag API initialization failed", error);
			return false;
		}
	}

	internal void Tick(bool available, bool menuOpen, GTPlayer player, Transform rightHand, bool trigger, bool rightGrip)
	{
		if (disposed)
		{
			return;
		}
		bool flag = available && GunEnabled && !menuOpen && rightGrip && player != null && player.mainCamera != null && rightHand != null;
		bool flag2 = shotEdge.Poll(flag, trigger);
		if (!flag)
		{
			if (ui != null)
			{
				ui.Root.gameObject.SetActive(value: false);
			}
			if (GunEnabled && Time.unscaledTime >= statusUntil)
			{
				Status = ((!available) ? "WAITING FOR CONTROLLERS" : (menuOpen ? "CLOSE MENU/CAMERA TO AIM" : ((player == null || rightHand == null) ? "WAITING FOR LOCAL PLAYER" : "HOLD RIGHT GRIP TO AIM")));
			}
			return;
		}
		try
		{
			EnsureVisuals();
			ui.Root.gameObject.SetActive(value: true);
			Ray ray = new Ray(rightHand.position + rightHand.forward * 0.08f, rightHand.forward);
			Vector3 vector = ray.origin + ray.direction * 60f;
			VRRig vRRig = null;
			Collider targetCollider = null;
			Vector3 vector2 = vector;
			EnsureApi();
			int num = Physics.RaycastNonAlloc(ray, hits, 60f, -1, QueryTriggerInteraction.Collide);
			float num2 = float.PositiveInfinity;
			if (num < hits.Length)
			{
				for (int i = 0; i < num; i++)
				{
					RaycastHit raycastHit = hits[i];
					if (!(raycastHit.collider == null) && !(raycastHit.distance >= num2) && !IsOwnCollider(raycastHit.collider, player))
					{
						VRRig vRRig2 = ResolveRig(raycastHit.collider);
						if ((!(vRRig2 != null) || !IsLocal(vRRig2)) && (!(vRRig2 == null) || !raycastHit.collider.isTrigger))
						{
							num2 = raycastHit.distance;
							vector = raycastHit.point;
							vector2 = raycastHit.point;
							vRRig = vRRig2;
							targetCollider = raycastHit.collider;
						}
					}
				}
			}
			else if (flag2)
			{
				Message("AIM BLOCKED - TRY AGAIN");
			}
			Vector3 vector3 = vector - ray.origin;
			beam.position = (ray.origin + vector) * 0.5f;
			beam.rotation = Quaternion.FromToRotation(Vector3.up, vector3.normalized);
			beam.localScale = new Vector3(0.002f, vector3.magnitude * 0.5f, 0.002f);
			dot.position = vector;
			dot.localScale = Vector3.one * ((vRRig != null) ? 0.035f : 0.018f);
			if (flag2 && num < hits.Length)
			{
				if (vRRig == null)
				{
					Message("AIM AT A GORILLA");
				}
				else
				{
					Attempt(vRRig, vector2, targetCollider);
				}
			}
			if (Time.unscaledTime >= statusUntil)
			{
				Status = AimStatus(vRRig, vector2);
			}
			Transform transform = player.mainCamera.transform;
			Vector3 vector4 = rightHand.position + Vector3.up * 0.13f + transform.forward * 0.1f;
			statusPanel.SetPositionAndRotation(vector4, Quaternion.LookRotation(vector4 - transform.position, transform.up));
			if (Time.unscaledTime >= nextLabel)
			{
				nextLabel = Time.unscaledTime + 0.12f;
				ui.SetText(statusLabel, Status);
			}
			ui.RefreshLayout();
		}
		catch (Exception error)
		{
			GunEnabled = false;
			if (ui != null)
			{
				ui.Root.gameObject.SetActive(value: false);
			}
			Message("TAG TOOLS ERROR - CHECK LOG");
			Fail("Tag gun failed", error);
		}
	}

	private VRRig ResolveRig(Collider collider)
	{
		VRRig componentInParent = collider.GetComponentInParent<VRRig>();
		if (componentInParent != null || propType == null || propOwner == null)
		{
			return componentInParent;
		}
		Component componentInParent2 = collider.GetComponentInParent(propType);
		if (!(componentInParent2 != null))
		{
			return null;
		}
		return propOwner.GetValue(componentInParent2) as VRRig;
	}

	private static bool IsOwnCollider(Collider collider, GTPlayer player)
	{
		GorillaTagger instance = GorillaTagger.Instance;
		if (!(player != null) || (!(collider == player.headCollider) && !(collider == player.bodyCollider) && !(collider.GetComponentInParent<GTPlayer>() == player)))
		{
			if (instance != null)
			{
				if (!(collider == instance.headCollider) && !(collider == instance.bodyCollider))
				{
					return collider.GetComponentInParent<GorillaTagger>() == instance;
				}
				return true;
			}
			return false;
		}
		return true;
	}

	private string AimStatus(VRRig target, Vector3 point)
	{
		NetworkSystem instance = NetworkSystem.Instance;
		if (instance == null || !instance.InRoom)
		{
			return "JOIN A ROOM FIRST";
		}
		if (apiUnavailable)
		{
			return "TAG API UNAVAILABLE - CHECK LOG";
		}
		if (target == null)
		{
			return "HOLD RIGHT GRIP + AIM";
		}
		object obj = activeManager?.GetValue(null, null);
		if (obj == null)
		{
			return "WAITING FOR GAME MODE";
		}
		if (instance.IsMasterClient && infectionType.IsInstanceOfType(obj) && !(bool)tagModeField.GetValue(obj))
		{
			return "HOST TARGET - PRESS TRIGGER";
		}
		GorillaTagger instance2 = GorillaTagger.Instance;
		if (instance2 == null || instance2.headCollider == null)
		{
			return "WAITING FOR LOCAL PLAYER";
		}
		float num = ((GTPlayer.Instance != null) ? GTPlayer.Instance.scale : 1f);
		if ((instance2.headCollider.transform.position - point).sqrMagnitude >= instance2.maxTagDistance * instance2.maxTagDistance * num)
		{
			return "MOVE CLOSER - NORMAL TAG RANGE";
		}
		if (Time.time <= instance2.taggedTime + instance2.tagCooldown)
		{
			return "TAG COOLDOWN";
		}
		return "NEAR TARGET - PRESS TRIGGER";
	}

	private static bool IsLocal(VRRig rig)
	{
		NetPlayer creator = rig.Creator;
		NetworkSystem instance = NetworkSystem.Instance;
		if (!rig.isOfflineVRRig && !(rig == VRRig.LocalRig))
		{
			if (creator != null)
			{
				if (!creator.IsLocal)
				{
					if (instance != null)
					{
						return IsLocalPlayer(creator, instance);
					}
					return false;
				}
				return true;
			}
			return false;
		}
		return true;
	}

	private bool BeginAction()
	{
		if (Time.unscaledTime < nextAction)
		{
			Message("WAIT A MOMENT", 1f);
			return false;
		}
		nextAction = Time.unscaledTime + 0.5f;
		return true;
	}

	private static bool IsLocalPlayer(NetPlayer player, NetworkSystem network)
	{
		if (!player.IsLocal)
		{
			if (network.LocalPlayer != null)
			{
				return player.ActorNumber == network.LocalPlayer.ActorNumber;
			}
			return false;
		}
		return true;
	}

	private static bool IsInfected(IList infected, NetPlayer player)
	{
		for (int i = 0; i < infected.Count; i++)
		{
			if (infected[i] is NetPlayer netPlayer && netPlayer.ActorNumber == player.ActorNumber)
			{
				return true;
			}
		}
		return false;
	}

	private bool RoomReady(out NetworkSystem network, out object manager)
	{
		network = NetworkSystem.Instance;
		manager = null;
		if (network == null || !network.InRoom)
		{
			Message("JOIN A ROOM FIRST");
			return false;
		}
		if (!EnsureApi())
		{
			Message("TAG API UNAVAILABLE - CHECK LOG");
			return false;
		}
		manager = activeManager.GetValue(null, null);
		if (manager == null)
		{
			Message("WAITING FOR GAME MODE");
			return false;
		}
		return true;
	}

	private bool HostInfection(NetworkSystem network, object manager, out IList infected)
	{
		infected = null;
		if (!network.IsMasterClient)
		{
			Message("HOST REQUIRED");
			return false;
		}
		if (!infectionType.IsInstanceOfType(manager) || (bool)tagModeField.GetValue(manager))
		{
			Message("INFECTION ONLY");
			return false;
		}
		if ((bool)endingField.GetValue(manager))
		{
			Message("ROUND RESTARTING");
			return false;
		}
		infected = infectedField.GetValue(manager) as IList;
		if (infected == null)
		{
			Message("INFECTION STATE UNAVAILABLE");
			return false;
		}
		return true;
	}

	private void Attempt(VRRig target, Vector3 hitPoint, Collider targetCollider)
	{
		if (!BeginAction() || !RoomReady(out var network, out var manager))
		{
			return;
		}
		NetPlayer creator = target.Creator;
		if (creator == null || creator.IsNull || !creator.IsValid || !creator.InRoom || IsLocalPlayer(creator, network))
		{
			Message("PLAYER UNAVAILABLE");
			return;
		}
		if (!GameMode.ParticipatingPlayers.Contains(creator))
		{
			Message("PLAYER NOT PARTICIPATING");
			return;
		}
		if (network.IsMasterClient && infectionType.IsInstanceOfType(manager) && !(bool)tagModeField.GetValue(manager))
		{
			if (HostInfection(network, manager, out var infected))
			{
				if (IsInfected(infected, creator))
				{
					Message("ALREADY TAGGED");
					return;
				}
				if (!(infectedArrayField.GetValue(manager) is Array array) || infected.Count >= array.Length)
				{
					Message("TAG CAPACITY REACHED");
					return;
				}
				addArgs[0] = creator;
				addArgs[1] = true;
				addInfected.Invoke(manager, addArgs);
				Message(IsInfected(infected, creator) ? "TAGGED" : "TAG NOT ACCEPTED");
				log.LogInfo("Tag gun host infection result: " + Status + " (actor " + creator.ActorNumber + ").");
			}
			return;
		}
		GorillaTagger instance = GorillaTagger.Instance;
		NetPlayer localPlayer = network.LocalPlayer;
		if (instance == null || instance.headCollider == null || localPlayer == null)
		{
			Message("WAITING FOR LOCAL PLAYER");
			return;
		}
		if (networkHandler.GetValue(null, null) == null)
		{
			Message("WAITING FOR ROOM AUTHORITY");
			return;
		}
		float num = ((GTPlayer.Instance != null) ? GTPlayer.Instance.scale : 1f);
		float maxTagDistance = instance.maxTagDistance;
		if ((instance.headCollider.transform.position - hitPoint).sqrMagnitude >= maxTagDistance * maxTagDistance * num)
		{
			Message("OUT OF RANGE");
			return;
		}
		if (Time.time <= instance.taggedTime + instance.tagCooldown)
		{
			Message("TAG COOLDOWN");
			return;
		}
		validationArgs[0] = targetCollider;
		validationArgs[1] = false;
		validationArgs[2] = false;
		validationArgs[3] = maxTagDistance;
		validationArgs[4] = null;
		validationArgs[5] = null;
		bool num2 = (bool)validateTag.Invoke(instance, validationArgs);
		NetPlayer netPlayer = validationArgs[4] as NetPlayer;
		if (!num2 || netPlayer == null)
		{
			Message("TAG NOT ALLOWED");
			return;
		}
		localTagArgs[0] = netPlayer;
		localTagArgs[1] = localPlayer;
		localTagArgs[2] = false;
		localTagArgs[3] = false;
		localTag.Invoke(manager, localTagArgs);
		GameMode.ReportTag(netPlayer);
		Message("TAG REQUEST SENT");
		log.LogInfo("Tag gun sent one game-validated tag request (actor " + netPlayer.ActorNumber + "); host decides acceptance.");
	}

	internal void TagAll()
	{
		if (disposed)
		{
			return;
		}
		try
		{
			if (!BeginAction() || !RoomReady(out var network, out var manager) || !HostInfection(network, manager, out var infected))
			{
				return;
			}
			NetPlayer[] array = GameMode.ParticipatingPlayers.ToArray();
			if (!(infectedArrayField.GetValue(manager) is Array array2))
			{
				Message("INFECTION STATE UNAVAILABLE");
				return;
			}
			visited.Clear();
			int num = 0;
			foreach (NetPlayer netPlayer in array)
			{
				if (!network.IsMasterClient || (bool)endingField.GetValue(manager) || infected.Count >= array2.Length)
				{
					break;
				}
				if (netPlayer != null && !netPlayer.IsNull && netPlayer.IsValid && netPlayer.InRoom && !IsLocalPlayer(netPlayer, network) && visited.Add(netPlayer.ActorNumber) && !IsInfected(infected, netPlayer))
				{
					addArgs[0] = netPlayer;
					addArgs[1] = true;
					addInfected.Invoke(manager, addArgs);
					if (IsInfected(infected, netPlayer))
					{
						num++;
					}
				}
			}
			nextAction = Time.unscaledTime + 1f;
			Message((num > 0) ? ("HOST TAGGED " + num + " PLAYER" + ((num == 1) ? "" : "S")) : ((infected.Count >= array2.Length) ? "TAG CAPACITY REACHED" : "NO ELIGIBLE PLAYERS"));
			log.LogInfo("Tag All completed one host-authorized pass: " + num + " new infection tags; local/already tagged players skipped.");
		}
		catch (Exception error)
		{
			Message("TAG ALL FAILED - CHECK LOG");
			Fail("Tag All failed", error);
		}
	}

	private void EnsureVisuals()
	{
		if (ui != null)
		{
			return;
		}
		try
		{
			ui = new WorldUi("Pigless Sheesh / tag gun", log);
			ui.Root.gameObject.layer = 5;
			UnityEngine.Object.DontDestroyOnLoad(ui.Root.gameObject);
			beam = ui.Primitive("Tag aim beam", PrimitiveType.Cylinder, Vector3.zero, Vector3.one, Theme.Brown).transform;
			dot = ui.Primitive("Tag aim dot", PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.025f, Theme.White).transform;
			statusPanel = ui.Group("Tag status");
			ui.Panel("Tag status face", new Vector2(0.27f, 0.033f), 0.007f, 0.004f, Vector3.zero, Theme.Dark, statusPanel);
			statusLabel = ui.Label(Status, new Vector3(0f, 0f, -0.004f), 0.009f, Theme.White, "MiddleCenter", 0.25f, statusPanel);
		}
		catch
		{
			ui?.Dispose();
			ui = null;
			throw;
		}
	}

	private void Fail(string message, Exception error)
	{
		if (!(Time.unscaledTime < nextLog))
		{
			nextLog = Time.unscaledTime + 5f;
			log.LogError(message + ": " + (error.InnerException ?? error));
		}
	}

	internal void Reset()
	{
		GunEnabled = false;
		shotEdge.Reset();
		visited.Clear();
		if (ui != null)
		{
			ui.Root.gameObject.SetActive(value: false);
		}
		Message("TAG GUN OFF", 0f);
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			Reset();
			ui?.Dispose();
			ui = null;
		}
	}
}
