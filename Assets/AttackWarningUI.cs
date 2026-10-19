using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public class AttackWarningUI : MonoBehaviour
{
	public static AttackWarningUI Instance { get; private set; }

	void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(gameObject);
			return;
		}
		Instance = this;
		DontDestroyOnLoad(gameObject); // optional: survive scene loads

		HideWarning();
	}

	public RawImage warningVisual;

	public bool visible { get; private set; }

	public static void HideWarning()
	{
		Instance.warningVisual.gameObject.SetActive(false);
		Instance.visible = false;
	}

	public static void ShowWarning()
	{
		Instance.warningVisual.gameObject.SetActive(true);
		Instance.visible = true;
	}
}
