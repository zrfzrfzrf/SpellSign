using UnityEngine;
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

	public static void HideWarning()
	{
		Instance.warningVisual.gameObject.SetActive(false);
	}

	public static void ShowWarning()
	{
		Instance.warningVisual.gameObject.SetActive(true);
	}
}
