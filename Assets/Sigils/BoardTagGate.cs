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
// Only the tag IDs are used here. The library also estimates each tag's pose, which
// needs a field of view and a tag size; the values below are placeholders and the
// pose is thrown away. They matter once the board crop is built on top of this.

using System;
using System.Diagnostics;
using AprilTag;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SpellSign.Sigils
{
    public class BoardTagGate : IDisposable
    {
        public const int BoardTags = 4;

        const float AssumedFov = 80f;       // degrees, horizontal. Placeholder, pose is unused
        const float AssumedTagSize = 0.04f; // meters. Placeholder, pose is unused

        // ---- tuning (the recognizer copies its Inspector values here) --------
        public int MinTags = 1;          // tags needed in one frame to count as "board in view"
        public float GraceSeconds = 1f;  // keep the gate open this long after the tags are lost
        public int Decimation = 2;       // detector runs at 1/Decimation resolution: faster, less sensitive
        public bool VerboseLog = true;   // one status line per second; open/close changes are always logged

        // ---- results of the latest frame -------------------------------------
        public bool IsOpen { get; private set; }
        public int SeenMask { get; private set; }       // bit i set = tag ID i seen
        public int SeenCount { get; private set; }      // how many of IDs 0..3 were seen
        public int OtherTagCount { get; private set; }  // tags seen with some other ID, ignored
        public float DetectMs { get; private set; }     // how long the last detection took

        TagDetector detector;
        int detectorWidth, detectorHeight, detectorDecimation;

        readonly Stopwatch stopwatch = new Stopwatch();
        float nextLogTime;

        // When the number of seen tags last reached MinTags. Used by Decide().
        float lastQualifiedTime = float.NegativeInfinity;

        // Run the detector on one camera frame (width * height pixels, as delivered by
        // PassthroughCameraAccess.GetColors) and update IsOpen.
        public void Update(ReadOnlySpan<Color32> frame, int width, int height, float now)
        {
            EnsureDetector(width, height);

            stopwatch.Restart();
            detector.ProcessImage(frame, AssumedFov, AssumedTagSize);
            stopwatch.Stop();
            DetectMs = (float)stopwatch.Elapsed.TotalMilliseconds;

            int mask = 0, other = 0;
            foreach (TagPose tag in detector.DetectedTags)
            {
                if (tag.ID >= 0 && tag.ID < BoardTags) mask |= 1 << tag.ID;
                else other++;
            }

            SeenMask = mask;
            SeenCount = CountBits(mask);
            OtherTagCount = other;

            bool wasOpen = IsOpen;
            IsOpen = Decide(SeenCount, SeenMask, now);

            if (IsOpen != wasOpen)
                Debug.Log("[BoardGate] " + (IsOpen ? "OPEN" : "CLOSED") + "  " + Describe());

            if (VerboseLog && now >= nextLogTime)
            {
                nextLogTime = now + 1f;
                Debug.Log("[BoardGate] " + Describe());
            }
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

        // The detector allocates native buffers for one image size, so it is rebuilt
        // when the camera resolution or the decimation setting changes.
        void EnsureDetector(int width, int height)
        {
            if (detector != null
                && width == detectorWidth
                && height == detectorHeight
                && Decimation == detectorDecimation)
                return;

            detector?.Dispose();
            detector = new TagDetector(width, height, Decimation);
            detectorWidth = width;
            detectorHeight = height;
            detectorDecimation = Decimation;
            Debug.Log($"[BoardGate] detector created {width}x{height}, decimation {Decimation}");
        }

        public void Dispose()
        {
            detector?.Dispose();
            detector = null;
        }
    }
}
