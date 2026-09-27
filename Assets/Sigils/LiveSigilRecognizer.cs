// SpellSign - live on-device sigil recognition.
//
//
// Every EvaluationsPerSecond frames it:
//   1. grabs the camera frame
//   2. crops to the centre region and downsamples to a small grayscale buffer
//   3. runs SigilClassifier
//   4. requires N consecutive agreeing results before declaring a shape
//   5. fires OnSigilRecognized once, on the transition
//
// The debounce in step 4 is the thing that makes it usable: a raw per-frame
// classification flickers between classes while the camera is moving. Holding the
// shape steady for half a second is also the natural physical gesture.

using System;
using System.Collections;
using Meta.XR;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SpellSign.Sigils
{
    public class LiveSigilRecognizer : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] PassthroughCameraAccess cameraAccess;
        [SerializeField] Text resultText;        // big readout
        [SerializeField] Text featureText;       // optional: raw numbers
        [SerializeField] RawImage maskPreview;   // optional: shows the ink mask

        [Header("Sampling")]
        [Tooltip("How many frames per second to classify. 4-6 is plenty and keeps the app smooth.")]
        [SerializeField] float evaluationsPerSecond = 5f;
        [Tooltip("Only look at this fraction of the frame, centred. Cuts out room clutter.")]
        [Range(0.2f, 1f)][SerializeField] float roiFraction = 0.6f;
        [Tooltip("Target width of the analysed buffer. 240-320 is a good balance.")]
        [SerializeField] int analysisWidth = 280;

        [Header("Stability")]
        [Tooltip("Consecutive agreeing frames before a shape is declared.")]
        [SerializeField] int framesToConfirm = 3;
        [SerializeField] float minConfidence = 0.15f;

        [Header("Thresholds (mirrored onto the classifier)")]
        [SerializeField] float lineElongation = 4.0f;
        [SerializeField] float circleRadialCv = 0.14f;
        [SerializeField] float minAreaFraction = 0.004f;
        [SerializeField] bool rejectBorderBlobs = true;

        public event Action<SigilResult> OnSigilRecognized;

        readonly SigilClassifier classifier = new SigilClassifier();

        byte[] gray;
        int grayWidth, grayHeight;
        Texture2D maskTexture;
        Color32[] maskPixels;

        float nextEvalTime;
        SigilType candidate = SigilType.None;
        int candidateStreak;
        SigilType declared = SigilType.None;
        SigilResult lastResult;

        void ApplyThresholds()
        {
            classifier.LineElongation = lineElongation;
            classifier.CircleRadialCv = circleRadialCv;
            classifier.MinAreaFraction = minAreaFraction;
            classifier.RejectBorderBlobs = rejectBorderBlobs;
        }

        void OnValidate() => ApplyThresholds();

        // Fallback so the component works without Inspector wiring when it sits
        // next to PassthroughCameraAccess (as it does on SigilScanner).
        void Awake()
        {
            if (cameraAccess == null) cameraAccess = GetComponent<PassthroughCameraAccess>();
        }

        IEnumerator Start()
        {
            ApplyThresholds();

            if (cameraAccess == null)
            {
                Debug.LogError("[LiveSigil] PassthroughCameraAccess not assigned");
                yield break;
            }

            // SigilCaptureRig handles permission and enables the camera. Just wait.
            while (!cameraAccess.IsPlaying) yield return null;

            if (resultText != null) resultText.text = "ready";
            Debug.Log("[LiveSigil] camera playing, starting recognition");
        }

        void Update()
        {
            if (cameraAccess == null || !cameraAccess.IsPlaying) return;
            if (Time.time < nextEvalTime) return;
            nextEvalTime = Time.time + 1f / Mathf.Max(0.5f, evaluationsPerSecond);

            if (!BuildGrayBuffer()) return;

            var result = classifier.Classify(gray, grayWidth, grayHeight);
            lastResult = result;

            UpdateStability(result);
            UpdateUi(result);
            UpdateMaskPreview();
        }

        bool BuildGrayBuffer()
        {
            var res = cameraAccess.CurrentResolution;
            if (res.x <= 0 || res.y <= 0) return false;

            NativeArray<Color32> colors;
            try { colors = cameraAccess.GetColors(); }
            catch (Exception e)
            {
                Debug.LogError("[LiveSigil] GetColors: " + e.Message);
                return false;
            }

            if (!colors.IsCreated || colors.Length < res.x * res.y) return false;

            // centred region of interest
            int roiW = Mathf.Max(16, (int)(res.x * roiFraction));
            int roiH = Mathf.Max(16, (int)(res.y * roiFraction));
            int roiX = (res.x - roiW) / 2;
            int roiY = (res.y - roiH) / 2;

            int stride = Mathf.Max(1, roiW / Mathf.Max(32, analysisWidth));
            int w = roiW / stride;
            int h = roiH / stride;

            if (gray == null || grayWidth != w || grayHeight != h)
            {
                gray = new byte[w * h];
                grayWidth = w;
                grayHeight = h;
                maskTexture = null; // force rebuild
            }

            for (int y = 0; y < h; y++)
            {
                int srcRow = (roiY + y * stride) * res.x + roiX;
                int dstRow = y * w;
                for (int x = 0; x < w; x++)
                {
                    Color32 c = colors[srcRow + x * stride];
                    // integer luma, fast enough at this size
                    gray[dstRow + x] = (byte)((c.r * 77 + c.g * 150 + c.b * 29) >> 8);
                }
            }

            return true;
        }

        void UpdateStability(SigilResult result)
        {
            bool acceptable = result.Type != SigilType.None && result.Confidence >= minConfidence;
            SigilType seen = acceptable ? result.Type : SigilType.None;

            if (seen == candidate) candidateStreak++;
            else { candidate = seen; candidateStreak = 1; }

            if (candidateStreak < framesToConfirm) return;
            if (candidate == declared) return;

            declared = candidate;

            if (declared != SigilType.None)
            {
                Debug.Log("[LiveSigil] recognised " + result);
                OnSigilRecognized?.Invoke(result);
            }
        }

        void UpdateUi(SigilResult result)
        {
            if (resultText != null)
            {
                resultText.text = declared == SigilType.None
                    ? "- - -"
                    : declared.ToString().ToUpperInvariant();
            }

            if (featureText != null)
            {
                featureText.text =
                    $"raw:    {result.Type}  {result.Confidence:0.00}\n" +
                    $"elong:  {result.Elongation:0.00}   (line > {lineElongation})\n" +
                    $"cv:     {result.RadialCv:0.000}  (circle < {circleRadialCv})\n" +
                    $"ink:    {result.InkPixels} px  ({result.AreaFraction:0.000})\n" +
                    $"streak: {candidateStreak}/{framesToConfirm}\n" +
                    (string.IsNullOrEmpty(result.Reason) ? "" : "why:    " + result.Reason);
            }
        }

        void UpdateMaskPreview()
        {
            if (maskPreview == null || gray == null) return;

            if (maskTexture == null)
            {
                maskTexture = new Texture2D(grayWidth, grayHeight, TextureFormat.RGBA32, false);
                maskTexture.filterMode = FilterMode.Point;
                maskPixels = new Color32[grayWidth * grayHeight];
                maskPreview.texture = maskTexture;
            }

            // cheap: re-threshold at the midpoint of the buffer just for display
            int sum = 0;
            for (int i = 0; i < gray.Length; i++) sum += gray[i];
            byte mid = (byte)(sum / gray.Length);

            for (int y = 0; y < grayHeight; y++)
            {
                int src = y * grayWidth;
                int dst = (grayHeight - 1 - y) * grayWidth;
                for (int x = 0; x < grayWidth; x++)
                {
                    bool ink = gray[src + x] <= mid;
                    maskPixels[dst + x] = ink
                        ? new Color32(255, 40, 160, 255)
                        : new Color32(10, 10, 10, 160);
                }
            }

            maskTexture.SetPixels32(maskPixels);
            maskTexture.Apply(false);
        }

        public SigilResult LastResult => lastResult;
        public SigilType Declared => declared;
        public int CandidateStreak => candidateStreak;
        public int FramesToConfirm => framesToConfirm;
    }
}
