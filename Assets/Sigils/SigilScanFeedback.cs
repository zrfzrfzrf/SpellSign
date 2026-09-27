using TMPro;
using UnityEngine;

namespace SpellSign.Sigils
{
    // Placeholder "the headset saw your drawing" indication, until real gameplay
    // effects exist. On every SigilScanner.Scanned it pops a card up in front of
    // the player: the recognised shape drawn as a glowing outline plus its name,
    // which grows in, holds, then fades. A new scan replaces the current card.
    //
    // Built entirely from code (TMP's default font, like DamageNumber), so it
    // needs no prefab, canvas or Inspector wiring -- just add it next to
    // SigilScanner. Faces Camera.main (the Camera Rig's CenterEyeAnchor).
    public class SigilScanFeedback : MonoBehaviour
    {
        const float GlyphRadius = 0.12f;   // meters
        const float LineWidth = 0.012f;
        const float LabelFontSize = 0.9f;  // roughly 0.09 m tall
        const float StartScale = 0.3f;
        const float GrowTime = 0.2f;
        const int CircleSegments = 48;

        [Header("Placement")]
        [Tooltip("How far in front of the player's head the card appears, in meters.")]
        [SerializeField] float distance = 1.2f;
        [Tooltip("How far below eye level the card appears, in meters.")]
        [SerializeField] float dropBelowEyes = 0.15f;

        [Header("Timing")]
        [SerializeField] float holdTime = 1.5f;
        [SerializeField] float fadeTime = 0.5f;

        [Header("Colours")]
        [SerializeField] Color circleColor = new Color(0.3f, 0.8f, 1f);   // shield
        [SerializeField] Color triangleColor = new Color(1f, 0.35f, 0.2f); // attack
        [SerializeField] Color lineColor = new Color(1f, 0.85f, 0.2f);     // wall

        [Header("Optional")]
        [Tooltip("Played at the card when a shape is scanned.")]
        [SerializeField] AudioClip scanSound;
        [Tooltip("Leave empty to use the built-in Sprites/Default shader.")]
        [SerializeField] Material lineMaterial;

        [Header("Debug")]
        [Tooltip("Head-locked readout of the raw classifier numbers, for tuning thresholds on site.")]
        [SerializeField] bool showDebugReadout = false;

        GameObject card;
        LineRenderer glyph;
        TextMeshPro label;
        Color cardColor;
        float cardAge;

        LiveSigilRecognizer recognizer;
        TextMeshPro debugText;

        void Awake()
        {
            recognizer = GetComponent<LiveSigilRecognizer>();
            if (lineMaterial == null) lineMaterial = new Material(Shader.Find("Sprites/Default"));
        }

        void OnEnable() => SigilScanner.Scanned += Show;
        void OnDisable() => SigilScanner.Scanned -= Show;

        void OnDestroy()
        {
            if (card != null) Destroy(card);
            if (debugText != null) Destroy(debugText.gameObject);
        }

        void Show(SigilResult result)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            if (card == null) BuildCard();

            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            card.transform.position = cam.transform.position + forward * distance + Vector3.down * dropBelowEyes;
            card.SetActive(true);
            cardAge = 0f;

            cardColor = ColorFor(result.Type);
            SetGlyphShape(result.Type);
            label.text = result.Type.ToString().ToUpperInvariant();

            if (scanSound != null) AudioSource.PlayClipAtPoint(scanSound, card.transform.position);
        }

        void Update()
        {
            UpdateCard();
            UpdateDebugReadout();
        }

        void UpdateCard()
        {
            if (card == null || !card.activeSelf) return;

            cardAge += Time.deltaTime;
            if (cardAge >= holdTime + fadeTime)
            {
                card.SetActive(false);
                return;
            }

            float grow = Mathf.Clamp01(cardAge / GrowTime);
            float easeOut = 1f - (1f - grow) * (1f - grow);
            card.transform.localScale = Vector3.one * Mathf.Lerp(StartScale, 1f, easeOut);

            float alpha = cardAge < holdTime ? 1f : 1f - (cardAge - holdTime) / fadeTime;
            Color c = cardColor;
            c.a = alpha;
            glyph.startColor = c;
            glyph.endColor = c;
            label.alpha = alpha;

            // TMP text reads correctly when its forward points away from the viewer.
            Camera cam = Camera.main;
            if (cam != null)
                card.transform.rotation = Quaternion.LookRotation(card.transform.position - cam.transform.position);
        }

        void BuildCard()
        {
            card = new GameObject("SigilScanCard");

            glyph = card.AddComponent<LineRenderer>();
            glyph.useWorldSpace = false;
            glyph.widthMultiplier = LineWidth;
            glyph.material = lineMaterial;
            glyph.numCapVertices = 4;
            glyph.numCornerVertices = 4;
            glyph.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            glyph.receiveShadows = false;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(card.transform, false);
            labelGo.transform.localPosition = new Vector3(0f, -GlyphRadius - 0.07f, 0f);
            label = labelGo.AddComponent<TextMeshPro>();
            label.fontSize = LabelFontSize;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.rectTransform.sizeDelta = new Vector2(0.6f, 0.15f);
        }

        void SetGlyphShape(SigilType type)
        {
            switch (type)
            {
                case SigilType.Circle:
                    glyph.loop = true;
                    glyph.positionCount = CircleSegments;
                    for (int i = 0; i < CircleSegments; i++)
                    {
                        float a = i * Mathf.PI * 2f / CircleSegments;
                        glyph.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * GlyphRadius);
                    }
                    break;

                case SigilType.Triangle:
                    glyph.loop = true;
                    glyph.positionCount = 3;
                    for (int i = 0; i < 3; i++)
                    {
                        float a = Mathf.PI / 2f + i * Mathf.PI * 2f / 3f;
                        glyph.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * GlyphRadius);
                    }
                    break;

                default: // Line
                    glyph.loop = false;
                    glyph.positionCount = 2;
                    glyph.SetPosition(0, new Vector3(-GlyphRadius, 0f, 0f));
                    glyph.SetPosition(1, new Vector3(GlyphRadius, 0f, 0f));
                    break;
            }
        }

        Color ColorFor(SigilType type) => type switch
        {
            SigilType.Circle => circleColor,
            SigilType.Triangle => triangleColor,
            _ => lineColor
        };

        void UpdateDebugReadout()
        {
            if (!showDebugReadout || recognizer == null)
            {
                if (debugText != null) debugText.gameObject.SetActive(false);
                return;
            }

            Camera cam = Camera.main;
            if (cam == null) return;

            if (debugText == null)
            {
                var go = new GameObject("SigilDebugReadout");
                go.transform.SetParent(cam.transform, false);
                go.transform.localPosition = new Vector3(0f, -0.25f, 0.9f);
                debugText = go.AddComponent<TextMeshPro>();
                debugText.fontSize = 0.35f;
                debugText.alignment = TextAlignmentOptions.Center;
                debugText.color = Color.white;
                debugText.rectTransform.sizeDelta = new Vector2(0.8f, 0.2f);
            }

            debugText.gameObject.SetActive(true);
            SigilResult r = recognizer.LastResult;
            debugText.text =
                $"raw {r.Type} {r.Confidence:0.00}   elong {r.Elongation:0.00}   cv {r.RadialCv:0.000}\n" +
                $"ink {r.AreaFraction:0.000}   streak {recognizer.CandidateStreak}/{recognizer.FramesToConfirm}   " +
                $"declared {recognizer.Declared}" +
                (string.IsNullOrEmpty(r.Reason) ? "" : $"\n{r.Reason}");
        }
    }
}
