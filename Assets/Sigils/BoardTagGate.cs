// SpellSign - board gate: "is the whiteboard in view?"
//
// Looks for the four AprilTags stuck in the corners of the whiteboard (tag IDs
// 0..3 = top-left, top-right, bottom-left, bottom-right; family tagStandard41h12).
// LiveSigilRecognizer only classifies shapes while this gate is open, so a circle,
// triangle or line on some other object in the room can't trigger a scan.
//
// Plain C# like SigilClassifier: the recognizer owns one and feeds it a camera
// frame at every evaluation. Detection is synchronous (the library spreads it over
// worker threads but the call waits for the result), so DetectMs is worth watching
// on the headset.
//
// It talks to the library's low-level detector instead of the TagDetector wrapper:
// the wrapper only returns each tag's 3D pose, while BoardCropper needs the pixel
// position of every tag corner. The IDs decide whether the gate opens; the corners
// (CornerX / CornerY) are for the crop.

using System;
using System.Diagnostics;
using AprilTag.Interop;
using UnityEngine;
using Debug = UnityEngine.Debug;
using JobsUtility = Unity.Jobs.LowLevel.Unsafe.JobsUtility;

namespace SpellSign.Sigils
{
    public class BoardTagGate : IDisposable
    {
        public const int BoardTags = 4;

        // ---- tuning (the recognizer copies its Inspector values here) --------
        public int MinTags = 1;          // tags needed in one frame to count as "board in view"
        public float GraceSeconds = 1f;  // keep the gate open this long after the tags are lost
        public int Decimation = 2;       // detector runs at 1/Decimation resolution: faster, less sensitive

        // ---- results of the latest frame -------------------------------------
        public bool IsOpen { get; private set; }
        public int SeenMask { get; private set; }       // bit i set = tag ID i seen
        public int SeenCount { get; private set; }      // how many of IDs 0..3 were seen
        public int OtherTagCount { get; private set; }  // tags seen with some other ID, ignored
        public float DetectMs { get; private set; }     // how long the last detection took

        // Tag corners of the latest frame, for the tags with TagSeen[id] set. Corner k of
        // tag id is at index id * 4 + k, in the library's order: counter-clockwise around
        // the tag, starting at the bottom-left corner of the tag when it is upright.
        // Pixel coordinates in the detector's picture: origin top-left, y down. That
        // picture is the camera buffer flipped upside down, so picture row y is buffer
        // row (height - 1 - y).
        public readonly bool[] TagSeen = new bool[BoardTags];
        public readonly double[] CornerX = new double[BoardTags * 4];
        public readonly double[] CornerY = new double[BoardTags * 4];

        Detector detector;
        Family family;
        ImageU8 image;
        int detectorWidth, detectorHeight, detectorDecimation;

        // Detection quality of the tag kept for each ID this frame, to pick the better
        // one if the same ID shows up twice.
        readonly float[] margins = new float[BoardTags];

        readonly Stopwatch stopwatch = new Stopwatch();

        // When the number of seen tags last reached MinTags. Used by Decide().
        float lastQualifiedTime = float.NegativeInfinity;

        // Run the detector on one camera frame (width * height pixels, as delivered by
        // PassthroughCameraAccess.GetColors) and update IsOpen.
        public void Update(ReadOnlySpan<Color32> frame, int width, int height, float now)
        {
            EnsureDetector(width, height);

            stopwatch.Restart();
            FillImage(frame, width, height);

            Array.Clear(TagSeen, 0, BoardTags);
            int mask = 0, other = 0;

            using (DetectionArray detections = detector.Detect(image))
            {
                for (int i = 0; i < detections.Length; i++)
                {
                    ref Detection d = ref detections[i];
                    int id = d.ID;

                    if (id < 0 || id >= BoardTags)
                    {
                        other++;
                        continue;
                    }

                    // Same ID twice in one frame (a false detection): keep the surer one.
                    if (TagSeen[id] && d.DecisionMargin <= margins[id]) continue;

                    TagSeen[id] = true;
                    margins[id] = d.DecisionMargin;
                    mask |= 1 << id;
                    StoreCorner(id, 0, d.Corner1);
                    StoreCorner(id, 1, d.Corner2);
                    StoreCorner(id, 2, d.Corner3);
                    StoreCorner(id, 3, d.Corner4);
                }
            }

            stopwatch.Stop();
            DetectMs = (float)stopwatch.Elapsed.TotalMilliseconds;

            SeenMask = mask;
            SeenCount = CountBits(mask);
            OtherTagCount = other;

            bool wasOpen = IsOpen;
            IsOpen = Decide(SeenCount, SeenMask, now);

            if (IsOpen != wasOpen)
                Debug.Log("[BoardGate] " + (IsOpen ? "OPEN" : "CLOSED") + "  " + Describe());
        }

        // Open while at least MinTags tags are seen, and for GraceSeconds after that,
        // so a hand or a shake covering the tags for a moment doesn't flicker the gate
        // (the recognizer needs several agreeing frames in a row to confirm a shape).
        bool Decide(int seenCount, int seenMask, float now)
        {
            if (seenCount >= MinTags)
            {
                lastQualifiedTime = now;
                return true;
            }

            return now - lastQualifiedTime <= GraceSeconds;
        }

        // e.g. "open=True tags=0,2 (2/4) other=0 detect=12.3ms"
        public string Describe()
        {
            string ids = "-";
            if (SeenMask != 0)
            {
                ids = "";
                for (int i = 0; i < BoardTags; i++)
                    if ((SeenMask & (1 << i)) != 0) ids += (ids.Length > 0 ? "," : "") + i;
            }

            return $"open={IsOpen} tags={ids} ({SeenCount}/{BoardTags}) other={OtherTagCount} detect={DetectMs:0.0}ms";
        }

        static int CountBits(int mask)
        {
            int n = 0;
            for (; mask != 0; mask &= mask - 1) n++;
            return n;
        }

        void StoreCorner(int id, int k, (double x, double y) corner)
        {
            CornerX[id * 4 + k] = corner.x;
            CornerY[id * 4 + k] = corner.y;
        }

        // Copies the camera frame into the detector's grey picture, upside down (the
        // camera buffer starts at the bottom row, the detector expects the top row
        // first). Uses the green channel, which is what the library's own converter does.
        void FillImage(ReadOnlySpan<Color32> frame, int width, int height)
        {
            Span<byte> dst = image.Buffer;
            int stride = image.Stride;

            for (int y = 0; y < height; y++)
            {
                int src = y * width;
                int row = (height - 1 - y) * stride;
                for (int x = 0; x < width; x++) dst[row + x] = frame[src + x].g;
            }
        }

        // The detector allocates native buffers for one image size, so it is rebuilt
        // when the camera resolution or the decimation setting changes.
        void EnsureDetector(int width, int height)
        {
            if (detector != null
                && width == detectorWidth
                && height == detectorHeight
                && Decimation == detectorDecimation)
                return;

            DisposeDetector();

            detector = Detector.Create();
            family = Family.CreateTagStandard41h12();
            image = ImageU8.Create(width, height);

            detector.ThreadCount = Mathf.Max(1, JobsUtility.JobWorkerCount);
            detector.QuadDecimate = Decimation;
            detector.AddFamily(family);

            detectorWidth = width;
            detectorHeight = height;
            detectorDecimation = Decimation;
            Debug.Log($"[BoardGate] detector created {width}x{height}, decimation {Decimation}");
        }

        void DisposeDetector()
        {
            if (detector != null)
            {
                if (family != null) detector.RemoveFamily(family);
                detector.Dispose();
            }

            family?.Dispose();
            image?.Dispose();

            detector = null;
            family = null;
            image = null;
        }

        public void Dispose() => DisposeDetector();
    }
}
