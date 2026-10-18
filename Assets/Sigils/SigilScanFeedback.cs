using Meta.XR;
using TMPro;
using UnityEngine;

namespace SpellSign.Sigils
{
    // Placeholder "the headset saw your drawing" indication, until real gameplay
    // effects exist. On every SigilScanner.Scanned it pops a card up in front of
    // the player: the recognised shape drawn as a glowing outline plus its name,
    // which grows in, holds, then fades. A new scan replaces the current card.
    //
    // Between cards it shows the scan state in the same spot: a ring that fills
    // while a shape is being confirmed ("hold still"), and a hint while the last
    // shape is still in view, because the same shape only fires again after it
    // has left the view.
    //
    // When an attempt breaks off (a shape started confirming, then was lost
    // before it fired), faint corner marks show for a few seconds where the
    // scanned part of the camera image is, at about arm's length. The camera sits
    // off to the side of the eye, so that area isn't where players expect it.
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
        const float RingRadius = 0.05f;
        const float RingWidth = 0.006f;
        const float RingFillSpeed = 4f;    // fill fraction per second, smooths the 5 Hz steps
        const float StatusFontSize = 0.35f;
        const float GuideLineWidth = 0.002f;
        const float GuideCornerLength = 0.2f; // fraction of the frame's side
        const float GuideFadeSpeed = 4f;      // opacity per second

        [Header("Placement")]
        [Tooltip("How far in front of the player's head the card appears, in meters.")]
        [SerializeField] float distance = 1.2f;
        [Tooltip("How far below eye level the card appears, in meters.")]
        [SerializeField] float dropBelowEyes = 0.15f;

        [Header("Timing")]
        [SerializeField] float holdTime = 1.5f;
        [SerializeField] float fadeTime = 0.5f;

        [Header("Colours")]
        [SerializeField] Color circleColor = new Color(0.3f, 0.8f, 1f);
        [SerializeField] Color triangleColor = new Color(1f, 0.35f, 0.2f);
        [SerializeField] Color lineColor = new Color(1f, 0.85f, 0.2f);

        [Header("Scan status")]
        [Tooltip("Show a filling ring while a shape is being confirmed, and a hint to take it away before it can be scanned again.")]
        [SerializeField] bool showScanStatus = true;

        [Header("Scan area guide")]
        [Tooltip("Faint corner marks showing where the camera scans, after a scan attempt breaks off.")]
        [SerializeField] bool showScanGuide = true;
        [Tooltip("Show the corner marks all the time instead of only after a broken-off attempt.")]
        [SerializeField] bool alwaysShowScanGuide = false;
        [Tooltip("How long the marks stay after an attempt breaks off, in seconds.")]
        [SerializeField] float scanGuideTime = 3f;
        [Tooltip("How far from the camera the marks are drawn -- roughly where the paper is held, in meters.")]
        [SerializeField] float scanGuideDistance = 0.45f;
        [Range(0f, 1f)][SerializeField] float scanGuideOpacity = 0.35f;

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

        GameObject status;
        LineRenderer statusTrack;
        LineRenderer statusFill;
        TextMeshPro statusText;
        float ringFill;

        GameObject guide;
        LineRenderer[] guideCorners;
        Vector3[] guideRays;          // ROI corner directions in camera space, z = 1
        Vector2Int guideRaysResolution;
        SigilType lastCandidate;
        float guideTimer;
        float guideAlpha;

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
            if (status != null) Destroy(status);
            if (guide != null) Destroy(guide);
            if (debugText != null) Destroy(debugText.gameObject);
        }

        void Show(SigilResult result)
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            if (card == null) BuildCard();

            card.transform.position = SpotInFront(cam);
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
            UpdateStatus();
            UpdateGuide();
            UpdateDebugReadout();
        }

        // In front of the player at eye level minus a drop, ignoring head pitch.
        Vector3 SpotInFront(Camera cam)
        {
            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            return cam.transform.position + forward * distance + Vector3.down * dropBelowEyes;
        }

        void UpdateStatus()
        {
            Camera cam = Camera.main;
            if (!showScanStatus || recognizer == null || cam == null)
            {
                if (status != null) status.SetActive(false);
                return;
            }

            SigilType candidate = recognizer.Candidate;
            SigilType declared = recognizer.Declared;

            // Confirming: a shape is building its streak. Waiting: the last shape
            // fired and hasn't left the view yet, so it can't fire again.
            bool confirming = candidate != SigilType.None && candidate != declared;
            bool waiting = !confirming && declared != SigilType.None;

            float target = confirming ? (float)recognizer.CandidateStreak / recognizer.FramesToConfirm : 0f;
            ringFill = Mathf.MoveTowards(ringFill, target, RingFillSpeed * Time.deltaTime);

            // The card owns this spot while it's up.
            bool cardShowing = card != null && card.activeSelf;
            bool show = !cardShowing && (confirming || waiting);
            if (!show)
            {
                if (status != null) status.SetActive(false);
                return;
            }

            if (status == null) BuildStatus();
            status.SetActive(true);
            status.transform.position = SpotInFront(cam);
            status.transform.rotation = Quaternion.LookRotation(status.transform.position - cam.transform.position);

            statusTrack.enabled = confirming;
            statusFill.enabled = confirming;

            if (confirming)
            {
                Color c = ColorFor(candidate);
                statusFill.startColor = c;
                statusFill.endColor = c;
                SetArc(statusFill, ringFill);
                statusText.text = "HOLD STILL";
            }
            else
            {
                statusText.text = "take it away to scan again";
            }
        }

        void BuildStatus()
        {
            status = new GameObject("SigilScanStatus");

            var trackGo = new GameObject("Track");
            trackGo.transform.SetParent(status.transform, false);
            statusTrack = NewLine(trackGo, RingWidth);
            Color dim = new Color(1f, 1f, 1f, 0.25f);
            statusTrack.startColor = dim;
            statusTrack.endColor = dim;
            SetArc(statusTrack, 1f);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(status.transform, false);
            fillGo.transform.localPosition = new Vector3(0f, 0f, -0.001f); // just in front of the track
            statusFill = NewLine(fillGo, RingWidth);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(status.transform, false);
            textGo.transform.localPosition = new Vector3(0f, -RingRadius - 0.035f, 0f);
            statusText = textGo.AddComponent<TextMeshPro>();
            statusText.fontSize = StatusFontSize;
            statusText.alignment = TextAlignmentOptions.Center;
            statusText.color = Color.white;
            statusText.rectTransform.sizeDelta = new Vector2(0.6f, 0.08f);
        }

        // Clockwise from the top, covering `fill` (0..1) of the ring.
        static void SetArc(LineRenderer line, float fill)
        {
            fill = Mathf.Clamp01(fill);
            bool full = fill >= 0.999f;
            int count = full ? CircleSegments : Mathf.Max(2, Mathf.CeilToInt(CircleSegments * fill) + 1);

            line.loop = full;
            line.positionCount = count;
            float sweep = full ? Mathf.PI * 2f * (count - 1) / count : Mathf.PI * 2f * fill;
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.PI / 2f - sweep * i / (count - 1);
                line.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * RingRadius);
            }
        }

        void UpdateGuide()
        {
            if (!showScanGuide || recognizer == null)
            {
                if (guide != null) guide.SetActive(false);
                return;
            }

            // A broken-off attempt: the candidate changed away from a shape that
            // hadn't fired. (After a shape fires, Declared still holds it while it
            // leaves the view, so taking a scanned shape away doesn't count.)
            SigilType candidate = recognizer.Candidate;
            if (candidate != lastCandidate)
            {
                if (lastCandidate != SigilType.None && lastCandidate != recognizer.Declared)
                    guideTimer = scanGuideTime;
                lastCandidate = candidate;
            }
            guideTimer -= Time.deltaTime;

            bool wanted = alwaysShowScanGuide || guideTimer > 0f;
            guideAlpha = Mathf.MoveTowards(guideAlpha, wanted ? 1f : 0f, GuideFadeSpeed * Time.deltaTime);

            Camera cam = Camera.main;
            PassthroughCameraAccess cameraAccess = recognizer.CameraAccess;
            if (guideAlpha <= 0f || cam == null || cameraAccess == null
                || !cameraAccess.IsPlaying || !EnsureGuideRays(cameraAccess))
            {
                if (guide != null) guide.SetActive(false);
                return;
            }

            if (guide == null) BuildGuide();
            guide.SetActive(true);

            // Follow the head every frame instead of GetCameraPose(), which is the
            // pose at the last camera frame and would make the marks trail behind
            // head movement. LensOffset is the camera's pose relative to the head.
            Pose lens = cameraAccess.Intrinsics.LensOffset;
            Transform head = cam.transform;
            guide.transform.SetPositionAndRotation(
                head.position + head.rotation * lens.position,
                head.rotation * lens.rotation);

            Color c = new Color(1f, 1f, 1f, scanGuideOpacity * guideAlpha);
            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = guideRays[i] * scanGuideDistance;
                Vector3 prev = guideRays[(i + 3) % 4] * scanGuideDistance;
                Vector3 next = guideRays[(i + 1) % 4] * scanGuideDistance;

                LineRenderer line = guideCorners[i];
                line.SetPosition(0, Vector3.Lerp(corner, prev, GuideCornerLength));
                line.SetPosition(1, corner);
                line.SetPosition(2, Vector3.Lerp(corner, next, GuideCornerLength));
                line.startColor = c;
                line.endColor = c;
            }
        }

        // The corners of the recognizer's scan area as camera-space directions,
        // scaled so z = 1 (multiply by a distance to get a point on the plane at
        // that depth). Intrinsics are fixed while the camera runs, so this only
        // reruns when the resolution changes.
        bool EnsureGuideRays(PassthroughCameraAccess cameraAccess)
        {
            Vector2Int res = cameraAccess.CurrentResolution;
            if (res.x <= 0 || res.y <= 0) return false;
            if (guideRays != null && res == guideRaysResolution) return true;

            float lo = 0.5f - 0.5f * recognizer.RoiFraction;
            float hi = 0.5f + 0.5f * recognizer.RoiFraction;
            Vector2[] viewport =
            {
                new Vector2(lo, lo), new Vector2(lo, hi), new Vector2(hi, hi), new Vector2(hi, lo)
            };
            var cameraSpace = new Pose(Vector3.zero, Quaternion.identity);

            guideRays = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                Vector3 d = cameraAccess.ViewportPointToRay(viewport[i], cameraSpace).direction;
                guideRays[i] = d / d.z;
            }
            guideRaysResolution = res;
            return true;
        }

        void BuildGuide()
        {
            guide = new GameObject("SigilScanGuide");
            guideCorners = new LineRenderer[4];
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("Corner" + i);
                go.transform.SetParent(guide.transform, false);
                guideCorners[i] = NewLine(go, GuideLineWidth);
                guideCorners[i].positionCount = 3;
            }
        }

        LineRenderer NewLine(GameObject go, float width)
        {
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.widthMultiplier = width;
            line.material = lineMaterial;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
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
            glyph = NewLine(card, LineWidth);

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
                $"centre {r.CentreInk:0.00}   contrast {r.Contrast}   dark {r.DarkFraction:0.00}\n" +
                $"ink {r.AreaFraction:0.000}   streak {recognizer.CandidateStreak}/{recognizer.FramesToConfirm}   " +
                $"declared {recognizer.Declared}" +
                (recognizer.RequireBoardTags && recognizer.Gate != null ? $"\nboard {recognizer.Gate.Describe()}" : "") +
                (string.IsNullOrEmpty(r.Reason) ? "" : $"\n{r.Reason}");
        }
    }
}
