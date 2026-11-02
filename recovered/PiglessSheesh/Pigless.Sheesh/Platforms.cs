using System;
using BepInEx.Logging;
using Pigless.Visuals;
using UnityEngine;

namespace Pigless.Sheesh;

internal sealed class Platforms : IDisposable
{
	private readonly WorldUi ui;

	private readonly Transform left;

	private readonly Transform right;

	private bool leftHeld;

	private bool rightHeld;

	internal Platforms(ManualLogSource logger)
	{
		ui = new WorldUi("Pigless Sheesh / platform objects", logger);
		UnityEngine.Object.DontDestroyOnLoad(ui.Root.gameObject);
		left = Make("Left platform", Theme.White);
		right = Make("Right platform", Theme.White);
		Clear();
	}

	private Transform Make(string name, Color accent)
	{
		Transform transform = ui.Group(name);
		GameObject gameObject = ui.Primitive("Solid surface", PrimitiveType.Cube, Vector3.zero, new Vector3(0.32f, 0.028f, 0.32f), Theme.Dark, collision: true, transform);
		int num = LayerMask.NameToLayer("Gorilla Object");
		gameObject.layer = ((num >= 0) ? num : 0);
		ui.Panel("Inset top", new Vector2(0.3f, 0.3f), 0.035f, 0.004f, new Vector3(0f, 0.017f, 0f), Theme.Brown, transform).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
		for (int i = 0; i < 4; i++)
		{
			bool flag = i < 2;
			float num2 = ((i % 2 != 0) ? 1 : (-1));
			ui.Primitive("Light strip", PrimitiveType.Cube, flag ? new Vector3(0f, 0.019f, num2 * 0.148f) : new Vector3(num2 * 0.148f, 0.019f, 0f), flag ? new Vector3(0.26f, 0.004f, 0.008f) : new Vector3(0.008f, 0.004f, 0.26f), accent, collision: false, transform);
		}
		return transform;
	}

	internal void Tick(bool enabled, bool leftGrip, bool rightGrip, Transform leftHand, Transform rightHand)
	{
		TickHand(left, enabled && leftGrip && leftHand != null, leftHand, ref leftHeld);
		TickHand(right, enabled && rightGrip && rightHand != null, rightHand, ref rightHeld);
	}

	private static void TickHand(Transform platform, bool held, Transform hand, ref bool wasHeld)
	{
		if (held && !wasHeld)
		{
			platform.position = hand.position + Vector3.down * 0.06f;
			platform.rotation = Quaternion.Euler(0f, hand.eulerAngles.y, 0f);
			platform.gameObject.SetActive(value: true);
		}
		else if (!held & wasHeld)
		{
			platform.gameObject.SetActive(value: false);
		}
		wasHeld = held;
	}

	internal void SetSize(float metres)
	{
		float num = Mathf.Clamp(metres, 0.22f, 0.55f) / 0.32f;
		Transform transform = left;
		Vector3 localScale = (right.localScale = new Vector3(num, 1f, num));
		transform.localScale = localScale;
	}

	internal void SetVisible(bool visible)
	{
		Renderer[] componentsInChildren = left.GetComponentsInChildren<Renderer>(includeInactive: true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			componentsInChildren[i].enabled = visible;
		}
		componentsInChildren = right.GetComponentsInChildren<Renderer>(includeInactive: true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			componentsInChildren[i].enabled = visible;
		}
	}

	internal void Clear()
	{
		leftHeld = (rightHeld = false);
		left.gameObject.SetActive(value: false);
		right.gameObject.SetActive(value: false);
	}

	public void Dispose()
	{
		ui.Dispose();
	}
}
