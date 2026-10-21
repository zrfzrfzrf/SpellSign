using System;
using UnityEngine;

public class ColorChanger : MonoBehaviour
{
	private void Awake()
	{
		allRenderers = GetComponentsInChildren<Renderer>();

		ChangeColorFromIndex(0);

		SetVisibility(false);
	}

	[Serializable]
	public class RendererColor
	{
		public Renderer[] renderers;
		public int materialIndex = 0;
		public Material mat;
	}

	[Serializable]
	public class ColorMode
	{
		public string name;
		public RendererColor[] parts;
	}

	private Renderer[] allRenderers;

	public void SetVisibility(bool vis)
	{
		foreach (Renderer renderer in allRenderers)
		{
			renderer.enabled = vis;
		}
	}

	[SerializeField]
	private ColorMode[] colorModes;

	public void ChangeColorFromIndex(int colorIndex)
	{
		ColorMode chosenMode = colorModes[colorIndex];

		for (int i = 0; i < chosenMode.parts.Length; i++)
		{
			for (int j = 0; j < chosenMode.parts[i].renderers.Length; j++)
			{
				Renderer renderer = chosenMode.parts[i].renderers[j];
				int index = chosenMode.parts[i].materialIndex;
				Material mat = chosenMode.parts[i].mat;

				// sharedMaterials: renderer.materials would make a new copy of every
				// material on each call, and none of them would ever be cleaned up.
				Material[] mats = renderer.sharedMaterials;
				mats[index] = mat;
				renderer.sharedMaterials = mats;
			}
		}
	}
}
