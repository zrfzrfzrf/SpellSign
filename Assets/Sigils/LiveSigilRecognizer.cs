// SpellSign - live on-device sigil recognition.
//
//
// Every EvaluationsPerSecond frames it:
//   1. grabs the camera frame
//   2. asks BoardTagGate whether the whiteboard (its AprilTags) is in view, and
//      stops here if not -- shapes elsewhere in the room are ignored
//   3. cuts the whiteboard's drawing area out of the frame with BoardCropper and
//      straightens it into a small grayscale buffer (if the tags don't allow that,
//      it stops here too). With cropToBoard off it takes the centre region of the
//      frame instead
//   4. runs SigilClassifier
//   5. requires N consecutive agreeing results before declaring a shape
//   6. fires OnSigilRecognized once, on the transition
//
// The debounce in step 5 is the thing that makes it usable: a raw per-frame
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
        [Tooltip("Circle and triangle must be rounder than this. Between this and Line Elongation -> nothing.")]
        [SerializeField] float maxShapeElongation = 2.2f;
        [SerializeField] float circleRadialCv = 0.17f;
        [Tooltip("Triangle band. Between Circle Radial Cv and the min -> nothing; above the max -> nothing.")]
        [SerializeField] float triangleMinRadialCv = 0.20f;
        [SerializeField] float triangleMaxRadialCv = 0.36f;
        [Tooltip("How filled-in the middle may be. Drawn outlines are hollow; shadows and objects are solid.")]
        [Range(0f, 1f)][SerializeField] float maxCentreInk = 0.2f;
        [Tooltip("Minimum brightness gap (0-255) between ink and board. Below it the view has no clear drawing.")]
        [SerializeField] int minContrast = 50;
        [Tooltip("Maximum share of the view that may be dark. Ink on a whiteboard is a few percent; a room is ~half.")]
        [Range(0f, 1f)][SerializeField] float maxDarkFraction = 0.3f;
        [SerializeField] float minAreaFraction = 0.004f;
        [SerializeField] bool rejectBorderBlobs = true;

        [Header("Whiteboard gate")]
        [Tooltip("Only recognise shapes while the whiteboard's corner AprilTags are in view. Off = recognise everywhere, as before.")]
        [SerializeField] bool requireBoardTags = true;
        [Tooltip("Corner tags (of 4) that must be seen at once. 1 = any tag is enough.")]
        [Range(1, 4)][SerializeField] int minTags = 1;
        [Tooltip("Keep recognising this long after the tags are lost, in seconds. Covers a hand or a shake blocking the tags for a moment.")]
        [SerializeField] float gateGraceSeconds = 1f;
        [Tooltip("Tag detection runs at 1/this resolution. Higher = faster but misses small or far tags.")]
        [Range(1, 4)][SerializeField] int tagDecimation = 2;

        [Header("Whiteboard crop")]
        [Tooltip("Classify only the whiteboard's drawing area, cut out and straightened using the tags. Off = classify the centre of the camera picture. Needs Require Board Tags.")]
        [SerializeField] bool cropToBoard = true;
        [Tooltip("Writing surface width, left to right, in cm.")]
        [SerializeField] float boardWidthCm = 27.5f;
        [Tooltip("Writing surface height, top to bottom, in cm.")]
        [SerializeField] float boardHeightCm = 21f;
        [Tooltip("Side of the whole printed tag in cm, all of its 9 x 9 cells (the detector itself only reports the central 5 x 5). Measure the print, not the file.")]
        [SerializeField] float tagSizeCm = 4f;
        [Tooltip("Gap between a printed tag's edge and the board edge, in cm. 0 = stuck right up to the edge.")]
        [SerializeField] float tagEdgeGapCm = 0f;
        [Tooltip("Shrinks the drawing area on all sides, in cm. 0 = the rectangle between the tags' inner edges.")]
        [SerializeField] float drawMarginCm = 0f;
        [Tooltip("Tags (of 4) that must be seen at once to cut out the drawing area. 1-2 tags only fix the board position roughly.")]
        [Range(1, 4)][SerializeField] int cropMinTags = 3;
        [Tooltip("Largest distance, in camera pixels, between a tag corner and where the fitted board puts it. Above it the frame is dropped as a bad detection.")]
        [SerializeField] float maxCornerErrorPx = 4f;
        [Tooltip("The drawing area's shortest side must be at least this many camera pixels long, otherwise the board is too far away.")]
        [SerializeField] float minBoardEdgePx = 40f;

        [Header("Debug")]
        [Tooltip("Log the raw classifier result about once a second (adb logcat -s Unity), for tuning the thresholds above.")]
        [SerializeField] bool logRawResults = true;

        public event Action<SigilResult> OnSigilRecognized;

        readonly SigilClassifier classifier = new SigilClassifier();
        BoardTagGate gate;
        BoardCropper cropper;

        byte[] gray;
        int grayWidth, grayHeight;
        Texture2D maskTexture;
        Color32[] maskPixels;

        float nextEvalTime;
        float nextRawLogTime;
        SigilType candidate = SigilType.None;
        int candidateStreak;
        SigilType declared = SigilType.None;
        SigilResult lastResult;

        void ApplyThresholds()
        {
            classifier.LineElongation = lineElongation;
            classifier.MaxShapeElongation = maxShapeElongation;
            classifier.CircleRadialCv = circleRadialCv;
            classifier.TriangleMinRadialCv = triangleMinRadialCv;
            classifier.TriangleMaxRadialCv = triangleMaxRadialCv;
            classifier.MaxCentreInk = maxCentreInk;
            classifier.MinContrast = minContrast;
            classifier.MaxDarkFraction = maxDarkFraction;
            classifier.MinAreaFraction = minAreaFraction;
            classifier.RejectBorderBlobs = rejectBorderBlobs;
        }

        void OnValidate() => ApplyThresholds();

        void OnDestroy() => gate?.Dispose();

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

            if (!TryGetFrame(out NativeArray<Color32> colors, out Vector2Int res)) return;

            // No whiteboard in view: nothing to classify. Feeding "no shape" into the
            // debounce also resets a declared shape, so it can fire again once the
            // board is back.
            if (requireBoardTags && !BoardInView(colors, res))
            {
                lastResult = new SigilResult { Reason = "no whiteboard tags" };
                UpdateStability(lastResult);
                UpdateUi(lastResult);
                return;
            }

            if (CropsToBoard)
            {
                // The tags are there but don't pin the board down well enough (too few,
                // too far, too slanted, or a bad detection): same as no board.
                if (!CropToBoard(colors, res))
                {
                    lastResult = new SigilResult { Reason = "no usable board crop: " + cropper.Reason };

                    if (logRawResults && Time.time >= nextRawLogTime)
                    {
                        nextRawLogTime = Time.time + 1f;
                        Debug.Log("[LiveSigil] " + lastResult.Reason);
                    }

                    UpdateStability(lastResult);
                    UpdateUi(lastResult);
                    return;
                }
            }
            else
            {
                BuildGrayBuffer(colors, res);
            }

            var result = classifier.Classify(gray, grayWidth, grayHeight);
            lastResult = result;

            if (logRawResults && Time.time >= nextRawLogTime)
            {
                nextRawLogTime = Time.time + 1f;
                Debug.Log($"[LiveSigil] raw {result}  contrast {result.Contrast}  dark {result.DarkFraction:0.00}");
            }

            UpdateStability(result);
            UpdateUi(result);
            UpdateMaskPreview();
        }

        bool TryGetFrame(out NativeArray<Color32> colors, out Vector2Int res)
        {
            colors = default;
            res = cameraAccess.CurrentResolution;
            if (res.x <= 0 || res.y <= 0) return false;

            try { colors = cameraAccess.GetColors(); }
            catch (Exception e)
            {
                Debug.LogError("[LiveSigil] GetColors: " + e.Message);
                return false;
            }

            return colors.IsCreated && colors.Length >= res.x * res.y;
        }

        // Runs the AprilTag detector on the full frame (not the cropped ROI: the tags
        // sit in the board's corners, which can be outside the centre region).
        bool BoardInView(NativeArray<Color32> colors, Vector2Int res)
        {
            gate ??= new BoardTagGate();
            gate.MinTags = minTags;
            gate.GraceSeconds = gateGraceSeconds;
            gate.Decimation = tagDecimation;

            gate.Update(colors.AsReadOnlySpan(), res.x, res.y, Time.time);
            return gate.IsOpen;
        }

        // Cuts the drawing area out of the frame (see BoardCropper) and makes that the
        // picture the classifier looks at. Needs the gate to have run on this frame.
        bool CropToBoard(NativeArray<Color32> colors, Vector2Int res)
        {
            cropper ??= new BoardCropper();
            cropper.BoardWidthCm = boardWidthCm;
            cropper.BoardHeightCm = boardHeightCm;
            cropper.TagSizeCm = tagSizeCm;
            cropper.TagEdgeGapCm = tagEdgeGapCm;
            cropper.DrawMarginCm = drawMarginCm;
            cropper.MinTags = cropMinTags;
            cropper.OutputWidth = analysisWidth;
            cropper.MaxCornerErrorPx = maxCornerErrorPx;
            cropper.MinEdgePx = minBoardEdgePx;

            if (!cropper.TryCrop(gate, colors.AsReadOnlySpan(), res.x, res.y)) return false;

            if (grayWidth != cropper.OutWidth || grayHeight != cropper.OutHeight)
                maskTexture = null; // force rebuild

            gray = cropper.Gray;
            grayWidth = cropper.OutWidth;
            grayHeight = cropper.OutHeight;
            return true;
        }

        void BuildGrayBuffer(NativeArray<Color32> colors, Vector2Int res)
        {
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
                    $"cv:     {result.RadialCv:0.000}  (circle < {circleRadialCv}, triangle {triangleMinRadialCv}-{triangleMaxRadialCv})\n" +
                    $"centre: {result.CentreInk:0.00}  contrast {result.Contrast}  dark {result.DarkFraction:0.00}\n" +
                    $"ink:    {result.InkPixels} px  ({result.AreaFraction:0.000})\n" +
                    $"streak: {candidateStreak}/{framesToConfirm}\n" +
                    (gate != null && requireBoardTags ? "board:  " + gate.Describe() + "\n" : "") +
                    (cropper != null && CropsToBoard ? "crop:   " + cropper.Describe() + "\n" : "") +
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
        public SigilType Candidate => candidate;
        public bool RequireBoardTags => requireBoardTags;
        public BoardTagGate Gate => gate;  // null until the first frame arrives
        public bool CropsToBoard => requireBoardTags && cropToBoard;
        public string CropStatus => cropper != null && CropsToBoard ? cropper.Describe() : null;
        public float RoiFraction => roiFraction;
        public PassthroughCameraAccess CameraAccess => cameraAccess;
        public int CandidateStreak => candidateStreak;
        public int FramesToConfirm => framesToConfirm;
    }
}
