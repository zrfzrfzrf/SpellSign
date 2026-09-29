// SpellSign - on-device sigil classifier.
// Pure C#: no UnityEngine types, so it can be unit-tested or run in a console harness.
//
// Input:  an 8-bit grayscale buffer (dark ink on a light board).
// Output: SigilResult with a type, a confidence and the raw features.
//
// Pipeline:
//   1. Otsu threshold                -> ink mask
//   2. connected components          -> discard border-touching and tiny blobs
//   3. largest remaining blob        -> centroid, PCA, radial statistics
//   4. feature bands                 -> Line / Circle / Triangle, or None
//
// Every class has to be matched positively. Anything that falls between or
// outside the bands comes out as None -- triangle is NOT the fallback, otherwise
// every shadow, hand or chair in view reads as a triangle.
//
// Why these two features instead of vertex counting:
//   A drawn shape is an OUTLINE, a thin loop of ink, not a filled region.
//   Circularity (4*pi*A/P^2) is meaningless on a stroke, and contour tracing plus
//   polygon simplification is a lot of code. Instead:
//
//   elongation = sqrt(lambda1/lambda2) from PCA of the ink pixels.
//       line ~ 10-40, circle ~ 1.0, triangle ~ 1.0-1.5
//
//   radialCv = stddev(r)/mean(r), r = distance from the centroid to each ink pixel.
//       circle   ~ 0.03-0.10  (every stroke pixel sits at the same radius)
//       triangle ~ 0.20-0.26  (corners are ~2x further out than edge midpoints)
//       square   ~ 0.12-0.16  (which is why the shape set is circle/triangle/line)
//
//   Both are rotation and scale invariant, which is what you want from a stranger
//   drawing at an angle on a piece of paper.
//
// Three gates reject things that aren't a drawing at all:
//   contrast     = light-class mean minus dark-class mean (Otsu). A view with no
//                  real ink still gets split in two; the split is just weak.
//   darkFraction = share of the frame on the dark side. Ink on a board is a few
//                  percent; a room is closer to half.
//   centreInk    = share of ink in a small disc around the centroid. A drawn
//                  circle/triangle is hollow; a shadow or object is solid.

using System;

namespace SpellSign.Sigils
{
    public enum SigilType
    {
        None = 0,
        Circle = 1,
        Triangle = 2,
        Line = 3
    }

    public struct SigilResult
    {
        public SigilType Type;
        public float Confidence;   // 0..1, how far past the threshold it landed
        public float Elongation;
        public float RadialCv;
        public int InkPixels;
        public float AreaFraction;
        public float CentroidX;    // normalised 0..1 within the analysed buffer
        public float CentroidY;
        public int Contrast;       // 0..255, light-class mean minus dark-class mean
        public float DarkFraction; // share of the buffer on the dark side of the threshold
        public float CentreInk;    // 0..1, how filled-in the middle of the shape is
        public string Reason;     // why it came out None, when it does

        public override string ToString()
        {
            return Type == SigilType.None
                ? $"None ({Reason})"
                : $"{Type} {Confidence:0.00}  elong {Elongation:0.00}  cv {RadialCv:0.000}  px {InkPixels}  centre {CentreInk:0.00}";
        }
    }

    public class SigilClassifier
    {
        // ---- tuning ----------------------------------------------------------
        // Start here. Widen the gaps rather than chasing the last few percent.
        public float LineElongation = 4.0f;    // above this -> Line
        public float MaxShapeElongation = 2.2f; // circle/triangle must be below this
        public float CircleRadialCv = 0.17f;   // below this -> Circle
        public float TriangleMinRadialCv = 0.20f; // triangle band; the gap down to
        public float TriangleMaxRadialCv = 0.36f; //   CircleRadialCv means "unsure"
        public float MaxCentreInk = 0.2f;      // above this -> solid blob, not an outline
        public int MinContrast = 50;           // below this -> no clear ink
        public float MaxDarkFraction = 0.3f;   // above this -> not ink on a light board
        public float MinAreaFraction = 0.004f; // smaller than this -> nothing drawn
        public float MaxAreaFraction = 0.55f;  // bigger than this  -> lighting, not ink
        public bool RejectBorderBlobs = true;  // drop blobs touching the frame edge

        // ---- scratch, reused between calls to avoid per-frame allocation -----
        readonly int[] histogram = new int[256];
        int[] labels;
        int[] stack;
        int[] compSize;
        bool[] compBorder;

        public SigilResult Classify(byte[] gray, int width, int height)
        {
            int len = width * height;
            var result = new SigilResult();

            if (gray == null || len <= 0 || gray.Length < len)
            {
                result.Reason = "bad buffer";
                return result;
            }

            EnsureBuffers(len);

            int threshold = OtsuThreshold(gray, len, out int contrast, out float darkFraction);
            result.Contrast = contrast;
            result.DarkFraction = darkFraction;

            if (contrast < MinContrast)
            {
                result.Reason = "low contrast";
                return result;
            }
            if (darkFraction > MaxDarkFraction)
            {
                result.Reason = "too much dark, no clean board";
                return result;
            }

            // --- label connected components of ink (8-connected) --------------
            Array.Clear(labels, 0, len);
            int componentCount = 0;
            int best = -1, bestSize = 0;

            int minPixels = (int)(MinAreaFraction * len);
            int maxPixels = (int)(MaxAreaFraction * len);

            for (int i = 0; i < len; i++)
            {
                if (labels[i] != 0 || gray[i] > threshold) continue;

                componentCount++;
                int id = componentCount;
                int size = 0;
                bool touchesBorder = false;

                int sp = 0;
                stack[sp++] = i;
                labels[i] = id;

                while (sp > 0)
                {
                    int p = stack[--sp];
                    int px = p % width;
                    int py = p / width;
                    size++;

                    if (px == 0 || py == 0 || px == width - 1 || py == height - 1)
                        touchesBorder = true;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = py + dy;
                        if (ny < 0 || ny >= height) continue;

                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = px + dx;
                            if (nx < 0 || nx >= width) continue;

                            int q = ny * width + nx;
                            if (labels[q] != 0 || gray[q] > threshold) continue;

                            labels[q] = id;
                            stack[sp++] = q;
                        }
                    }
                }

                EnsureComponentArrays(componentCount);
                compSize[id] = size;
                compBorder[id] = touchesBorder;

                bool usable = size >= minPixels
                              && size <= maxPixels
                              && !(RejectBorderBlobs && touchesBorder);

                if (usable && size > bestSize)
                {
                    bestSize = size;
                    best = id;
                }
            }

            if (best < 0)
            {
                result.Reason = componentCount == 0 ? "no ink" : "no usable blob";
                return result;
            }

            // --- first moments -------------------------------------------------
            double sumX = 0, sumY = 0;
            for (int i = 0; i < len; i++)
            {
                if (labels[i] != best) continue;
                sumX += i % width;
                sumY += i / width;
            }

            double cx = sumX / bestSize;
            double cy = sumY / bestSize;

            // --- second moments (PCA) + radial statistics -----------------------
            double sxx = 0, syy = 0, sxy = 0;
            double sumR = 0, sumR2 = 0;

            for (int i = 0; i < len; i++)
            {
                if (labels[i] != best) continue;

                double dx = (i % width) - cx;
                double dy = (i / width) - cy;

                sxx += dx * dx;
                syy += dy * dy;
                sxy += dx * dy;

                double r = Math.Sqrt(dx * dx + dy * dy);
                sumR += r;
                sumR2 += r * r;
            }

            sxx /= bestSize; syy /= bestSize; sxy /= bestSize;

            double trace = sxx + syy;
            double diff = Math.Sqrt((sxx - syy) * (sxx - syy) + 4.0 * sxy * sxy);
            double lambda1 = 0.5 * (trace + diff);
            double lambda2 = 0.5 * (trace - diff);

            float elongation = lambda2 > 1e-6
                ? (float)Math.Sqrt(lambda1 / lambda2)
                : 999f;

            double meanR = sumR / bestSize;
            double varR = Math.Max(0.0, sumR2 / bestSize - meanR * meanR);
            float radialCv = meanR > 1e-6 ? (float)(Math.Sqrt(varR) / meanR) : 0f;

            result.Elongation = elongation;
            result.RadialCv = radialCv;
            result.InkPixels = bestSize;
            result.AreaFraction = (float)bestSize / len;
            result.CentroidX = (float)(cx / width);
            result.CentroidY = (float)(cy / height);
            result.CentreInk = CentreInkFraction(best, width, height, cx, cy, 0.25 * meanR);

            // --- decide ---------------------------------------------------------
            if (elongation > LineElongation)
            {
                result.Type = SigilType.Line;
                result.Confidence = Clamp01((elongation - LineElongation) / LineElongation);
            }
            else if (elongation > MaxShapeElongation)
                result.Reason = "between line and shape";
            else if (result.CentreInk > MaxCentreInk)
                result.Reason = "solid blob, not an outline";
            else if (radialCv < CircleRadialCv)
            {
                result.Type = SigilType.Circle;
                result.Confidence = Clamp01((CircleRadialCv - radialCv) / CircleRadialCv);
            }
            else if (radialCv < TriangleMinRadialCv)
                result.Reason = "between circle and triangle";
            else if (radialCv <= TriangleMaxRadialCv)
            {
                // Scored by distance from the circle cutoff, mirroring the circle
                // score. Sloppy, lopsided triangles land near the top of the band
                // and are still clearly triangles, so the top edge doesn't lower it.
                result.Type = SigilType.Triangle;
                result.Confidence = Clamp01((radialCv - CircleRadialCv) / CircleRadialCv);
            }
            else
                result.Reason = "irregular shape";

            return result;
        }

        // Share of the chosen blob's pixels inside a disc of the given radius
        // around the centroid. ~0 for a hollow outline, ~1 for a solid patch.
        float CentreInkFraction(int blob, int width, int height, double cx, double cy, double radius)
        {
            int r = Math.Max(1, (int)radius);
            int x0 = Math.Max(0, (int)cx - r), x1 = Math.Min(width - 1, (int)cx + r);
            int y0 = Math.Max(0, (int)cy - r), y1 = Math.Min(height - 1, (int)cy + r);
            double r2 = radius * radius;

            int inside = 0, ink = 0;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    double dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy > r2) continue;
                    inside++;
                    if (labels[y * width + x] == blob) ink++;
                }
            }

            return inside > 0 ? (float)ink / inside : 0f;
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        void EnsureBuffers(int len)
        {
            if (labels == null || labels.Length < len)
            {
                labels = new int[len];
                stack = new int[len];
            }
            if (compSize == null)
            {
                compSize = new int[64];
                compBorder = new bool[64];
            }
        }

        void EnsureComponentArrays(int id)
        {
            if (id < compSize.Length) return;
            int n = compSize.Length * 2;
            while (n <= id) n *= 2;
            Array.Resize(ref compSize, n);
            Array.Resize(ref compBorder, n);
        }

        int OtsuThreshold(byte[] gray, int len, out int contrast, out float darkFraction)
        {
            Array.Clear(histogram, 0, 256);
            for (int i = 0; i < len; i++) histogram[gray[i]]++;

            long total = len;
            double sum = 0;
            for (int i = 0; i < 256; i++) sum += (double)i * histogram[i];

            double sumB = 0;
            long wB = 0;
            double maxBetween = -1;
            int threshold = 128;
            contrast = 0;
            darkFraction = 0f;

            for (int t = 0; t < 256; t++)
            {
                wB += histogram[t];
                if (wB == 0) continue;

                long wF = total - wB;
                if (wF == 0) break;

                sumB += (double)t * histogram[t];

                double mB = sumB / wB;
                double mF = (sum - sumB) / wF;
                double between = (double)wB * wF * (mB - mF) * (mB - mF);

                if (between > maxBetween)
                {
                    maxBetween = between;
                    threshold = t;
                    contrast = (int)(mF - mB);
                    darkFraction = (float)wB / total;
                }
            }

            return threshold;
        }
    }
}
