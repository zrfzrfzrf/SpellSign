using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The warning while the dragon gets ready to breathe (DragonBoss shows it at
// the start of the breath and hides it when the breath is over). It is the
// time the sigil shield can be raised in.
//
// Coloured text with no background, low and centred in the view -- out of
// the way of the dragon, which is up in the air for the breath:
//
//     GUARDIAN! PROTECT YOUR MAGE          (warm orange, gently pulsing)
//     Wrong shield!                        (yellow, while a wrong shape is shown)
//     Raise a WATER SHIELD: show the CIRCLE   (shield and shape in its colour)
//
// The words come from Translations.csv (Loc), in the player's language.
//
// Once the right shield is up the text goes away: the bubble around the
// player says it all. The breath is still under way, though -- `visible`
// stays true until it is over, which is what SigilShield goes by.
// A dark outline and a soft shadow keep it readable over any passthrough.
//
// Built from code on the head-locked Canvas this component sits under.
public class AttackWarningUI : MonoBehaviour
{
	const float WrongShowTime = 1.5f;  // seconds "Wrong shield!" stays after the wrong shape is last shown
	const float FadeInTime = 0.25f;
	const float RiseDistance = 12f;    // canvas units it rises while fading in
	const float PulseRate = 1.6f;      // title pulses per second
	const string TitleColor = "#FF7A3D";
	const string WrongColor = "#FFE14D";

	[Tooltip("How far below the middle of the view the warning's bottom line sits, in canvas units (millimetres on the head-locked canvas). 245 = just above the player's health bar.")]
	public float belowCenter = 245f;
	[Tooltip("Size of the hint line, in canvas units. The title is a little bigger.")]
	public float fontSize = 26f;

	[Tooltip("The old red warning panel. Kept hidden; the warning is the text now.")]
	public RawImage warningVisual;

	public static AttackWarningUI Instance { get; private set; }

	public bool visible { get; private set; }

	// The shield this breath needs: one of the dragon's own element.
	public static Element Needed => Instance != null ? Instance.needed : Element.Fire;

	Element needed;
	float wrongUntil;     // "Wrong shield!" shows until then
	float shownAt;
	RectTransform root;
	Vector2 home;
	TextMeshProUGUI text;

	void Awake()
	{
		if (Instance != null && Instance != this)
		{
			Destroy(gameObject);
			return;
		}
		Instance = this;
		DontDestroyOnLoad(gameObject); // optional: survive scene loads

		if (warningVisual != null) warningVisual.gameObject.SetActive(false);
		Build();
		HideWarning();
	}

	// Both are safe to call with no warning in the scene (or after it is gone).
	public static void HideWarning()
	{
		if (Instance == null || Instance.root == null) return;
		Instance.root.gameObject.SetActive(false);
		Instance.visible = false;
	}

	// neededShield: the element of the breath coming, which only a shield of
	// the same element stops.
	public static void ShowWarning(Element neededShield)
	{
		if (Instance == null || Instance.root == null) return;
		Instance.needed = neededShield;
		Instance.wrongUntil = 0f;
		Instance.shownAt = Time.time;
		Instance.root.gameObject.SetActive(true);
		Instance.visible = true;
		Instance.UpdateText();
	}

	// A shape for another element was shown.
	public static void SayWrongShield()
	{
		if (Instance == null) return;
		Instance.wrongUntil = Time.time + WrongShowTime;
		Instance.UpdateText();
	}

	// The right shield is up: the text goes, the warning period carries on.
	public static void SayShielded()
	{
		if (Instance == null || Instance.root == null) return;
		Instance.wrongUntil = 0f;
		Instance.root.gameObject.SetActive(false);
	}

	void Update()
	{
		if (!visible) return;

		// "Wrong shield!" goes again a moment after the wrong shape is put away.
		if (wrongUntil > 0f && Time.time > wrongUntil)
		{
			wrongUntil = 0f;
			UpdateText();
		}

		// Fades in rising a little, then breathes gently while it waits.
		float appear = Mathf.SmoothStep(0f, 1f, (Time.time - shownAt) / FadeInTime);
		float pulse = 0.85f + 0.15f * Mathf.Sin((Time.time - shownAt) * PulseRate * 2f * Mathf.PI);
		text.alpha = appear * pulse;
		root.anchoredPosition = home + Vector2.down * RiseDistance * (1f - appear);
	}

	void UpdateText()
	{
		if (text == null) return;
		string color = "#" + ColorUtility.ToHtmlStringRGB(ElementLook.Color(needed));
		string shield = "<color=" + color + "><b>" + Loc.T("shield." + needed) + "</b></color>";
		string shape = "<color=" + color + "><b>" + SpellSign.Sigils.SigilShield.ShapeName(needed) + "</b></color>";

		string title = "<size=125%><color=" + TitleColor + ">" + Loc.T("warning.title") + "</color></size>";
		string status = Loc.T("warning.raise", shield, shape);
		string wrong = wrongUntil > 0f ? "\n<color=" + WrongColor + "><b>" + Loc.T("warning.wrong") + "</b></color>" : "";
		text.text = title + wrong + "\n" + status;
	}

	void Build()
	{
		Canvas canvas = GetComponentInParent<Canvas>();
		if (canvas == null)
		{
			Debug.LogWarning("[AttackWarningUI] no Canvas above it -- no breath warning.");
			return;
		}

		var go = new GameObject("Breath Warning", typeof(RectTransform));
		root = (RectTransform)go.transform;
		root.SetParent(canvas.transform, false);
		root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
		root.pivot = new Vector2(0.5f, 0f); // grows upward from its bottom edge
		root.sizeDelta = new Vector2(760f, 200f);
		home = new Vector2(0f, -belowCenter);
		root.anchoredPosition = home;

		// Drawn after everything else, so nothing near the head can wash it out.
		var own = go.AddComponent<Canvas>();
		own.overrideSorting = true;
		own.sortingOrder = 100;

		text = go.AddComponent<TextMeshProUGUI>();
		text.fontSize = fontSize;
		text.fontStyle = FontStyles.Bold;
		text.color = Color.white;
		text.alignment = TextAlignmentOptions.Bottom;
		text.textWrappingMode = TextWrappingModes.Normal;
		text.lineSpacing = 8f;
		text.raycastTarget = false;

		// Readable over anything without a background: a dark outline and a soft shadow.
		text.outlineWidth = 0.22f;
		text.outlineColor = new Color32(0, 0, 0, 230);
		Material material = text.fontMaterial; // its own copy
		material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
		material.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.6f));
		material.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.6f);
		material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.6f);
		material.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.2f);
	}
}
