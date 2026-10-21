using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The warning in front of the player while the dragon gets ready to breathe
// (DragonBoss shows it at the start of the breath and hides it when the
// breath is over). It is the time the sigil shield can be raised in.
//
// Under the panel's own text it says which shield this breath needs -- "Raise
// a WATER shield: show the CIRCLE" -- and SigilShield adds "Wrong shield!"
// above that while a wrong shape is shown. Once the right shield is up the
// line goes.
public class AttackWarningUI : MonoBehaviour
{
	const float WrongShowTime = 1.5f; // seconds "Wrong shield!" stays after the wrong shape is last shown
	const float MessageFontSize = 30f;
	static readonly Color MessageColor = Color.white;
	const string WrongColor = "#FFE14D";

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

		BuildMessage();
		HideWarning();
	}

	public RawImage warningVisual;

	public bool visible { get; private set; }

	// The shield this breath needs: one of the dragon's own element.
	public static Element Needed => Instance != null ? Instance.needed : Element.Fire;

	Element needed;
	bool shielded;        // the right shield is up: no more line
	float wrongUntil;     // "Wrong shield!" shows until then
	TextMeshProUGUI message;

	// Both are safe to call with no warning in the scene (or after it is gone).
	public static void HideWarning()
	{
		if (Instance == null || Instance.warningVisual == null) return;
		Instance.warningVisual.gameObject.SetActive(false);
		Instance.visible = false;
	}

	// neededShield: the element of the breath coming, which only a shield of
	// the same element stops.
	public static void ShowWarning(Element neededShield)
	{
		if (Instance == null || Instance.warningVisual == null) return;
		Instance.needed = neededShield;
		Instance.shielded = false;
		Instance.wrongUntil = 0f;
		Instance.warningVisual.gameObject.SetActive(true);
		Instance.visible = true;
		Instance.UpdateMessage();
	}

	// A shape for another element was shown.
	public static void SayWrongShield()
	{
		if (Instance == null) return;
		Instance.wrongUntil = Time.time + WrongShowTime;
		Instance.UpdateMessage();
	}

	// The right shield is up.
	public static void SayShielded()
	{
		if (Instance == null) return;
		Instance.shielded = true;
		Instance.UpdateMessage();
	}

	void Update()
	{
		// "Wrong shield!" goes again a moment after the wrong shape is put away.
		if (wrongUntil > 0f && Time.time > wrongUntil)
		{
			wrongUntil = 0f;
			UpdateMessage();
		}
	}

	void UpdateMessage()
	{
		if (message == null) return;
		if (shielded)
		{
			message.text = "";
			return;
		}

		string hint = "Raise a " + ElementLook.Name(needed).ToUpper() + " shield:\nshow the "
		            + SpellSign.Sigils.SigilShield.ShapeName(needed);
		message.text = (wrongUntil > 0f ? "<color=" + WrongColor + "><b>Wrong shield!</b></color>\n" : "") + hint;
	}

	// The line of text, in the lower part of the warning panel.
	void BuildMessage()
	{
		if (warningVisual == null) return;

		var go = new GameObject("Shield Message", typeof(RectTransform));
		go.transform.SetParent(warningVisual.transform, false);
		var rect = (RectTransform)go.transform;
		rect.anchorMin = new Vector2(0f, 0f);
		rect.anchorMax = new Vector2(1f, 0.5f);
		rect.offsetMin = new Vector2(12f, 12f);
		rect.offsetMax = new Vector2(-12f, 0f);

		message = go.AddComponent<TextMeshProUGUI>();
		message.fontSize = MessageFontSize;
		message.color = MessageColor;
		message.alignment = TextAlignmentOptions.Bottom;
		message.textWrappingMode = TextWrappingModes.Normal;
		message.raycastTarget = false;
	}
}
