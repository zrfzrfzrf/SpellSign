// SpellSign - cuts the whiteboard's drawing area out of the camera frame.
//
// Input:  the tag corners found by BoardTagGate (pixel positions) and the camera frame.
// Output: a straightened grey picture of the drawing area, for SigilClassifier,
//         whatever way the board is held (tilted, turned, sideways).
//
// How:
//   1. Every seen tag gives 4 pairs: where that tag corner is on the board (known
//      from the measurements below) and where the camera sees it (pixels). The tag
//      ID says which tag it is, so the board's orientation never has to be guessed.
//      The corners are those of the detector's square, the central 5/9 of the
//      printed tag (see DetectedSquareFraction). All four tags have to be stuck on
//      the same way up, since they all share one corner order.
//   2. Homography.Fit finds the one mapping board -> picture that matches all pairs
//      best (least squares). Even one tag is enough, but its 4 corners are close
//      together and the rest of the board is guessed from them, so a lone tag is
//      imprecise; 3+ tags are solid. MinTags decides how many are required.
//   3. The fit is checked: each corner is mapped back and compared with where it was
//      actually detected. A big gap means a false detection or a bent tag, and the
//      frame is dropped instead of cropping the wrong area.
//   4. The drawing area (the rectangle between the tags' inner corners) is mapped
//      into the picture, checked for sanity, and every pixel of the output is sampled
//      from the camera frame through the mapping.
//
// Board coordinates: origin at the top-left of the writing surface, x to the right,
// y down, in centimetres. Tag IDs sit at 0 top-left, 1 top-right, 2 bottom-left,
// 3 bottom-right, all stuck upright.
//
// Plain C#, no per-frame allocations after the first frame (apart from the output
// buffer being resized).

using System;
using UnityEngine;

namespace SpellSign.Sigils
{
    public class BoardCropper
    {
        // The corners the detector reports are not the corners of the whole printed tag.
        // A tagStandard41h12 tag is 9 x 9 cells, and the detector's square is the
        // central 5 x 5 (width_at_border / total_width in the library's family
        // definition); the outer cells only carry data bits. So the 4 corner points of
        // a tag sit on a square of TagSizeCm * 5/9, centred on the printed tag.
        // Measured on the headset: the tag centres came out 1.8 (= 9/5) times further
        // apart than the printed size predicted.
        public const float DetectedSquareFraction = 5f / 9f;

        // ---- board measurements (the recognizer copies its Inspector values here) --
        public float BoardWidthCm = 27.5f;    // writing surface, left to right
        public float BoardHeightCm = 21f;     // writing surface, top to bottom
        public float TagSizeCm = 4f;          // the whole printed tag square (all 9 x 9 cells)
        public float TagEdgeGapCm = 0f;       // gap between a printed tag's edge and the board edge
        public float DrawMarginCm = 0f;       // shrinks the drawing area, on all sides, from the tags' inner edges

        // ---- tuning ----------------------------------------------------------
        public int MinTags = 3;               // tags needed for a crop (fewer = no crop)
        public int OutputWidth = 280;         // width of the straightened picture, pixels
        public float MaxCornerErrorPx = 4f;   // a tag corner may be this far from where the fit puts it
        public float MinEdgePx = 40f;         // the drawing area's shortest side must be this long in the camera picture

        // ---- results of the latest TryCrop -------------------------------------
        public byte[] Gray;                   // the straightened picture, OutWidth * OutHeight bytes
        public int OutWidth, OutHeight;
        public int TagsUsed;
        public float MaxErrorPx;              // worst distance between a tag corner and where the fit puts it
        public string Reason;                 // why the last TryCrop failed, null when it worked

        // scratch
        readonly double[] srcX = new double[16], srcY = new double[16];
        readonly double[] dstX = new double[16], dstY = new double[16];
        readonly double[] h = new double[9];
        readonly double[] quadX = new double[4], quadY = new double[4];

        // Tries to cut the drawing area out of `frame` (width * height, the camera buffer).
        // On success Gray / OutWidth / OutHeight hold the picture and Reason is null.
        public bool TryCrop(BoardTagGate gate, ReadOnlySpan<Color32> frame, int width, int height)
        {
            Reason = null;

            int tags = 0;
            for (int id = 0; id < BoardTagGate.BoardTags; id++)
                if (gate.TagSeen[id]) tags++;
            TagsUsed = tags;

            if (tags < MinTags) return Fail($"{tags} tags, need {MinTags}");

            // --- 1 + 2: fit board -> picture -----------------------------------
            int n = BuildPairs(gate);
            if (!Homography.Fit(srcX, srcY, dstX, dstY, n, h)) return Fail("fit failed");

            MaxErrorPx = (float)MeasureError(h, n);

            // --- 3: is the fit believable? --------------------------------------
            if (MaxErrorPx > MaxCornerErrorPx)
                return Fail($"corner error {MaxErrorPx:0.0}px (limit {MaxCornerErrorPx:0.0})");

            // --- 4: where is the drawing area in the picture? ---------------------
            float t = TagSizeCm;
            float left = TagEdgeGapCm + t + DrawMarginCm;
            float right = BoardWidthCm - TagEdgeGapCm - t - DrawMarginCm;
            float top = TagEdgeGapCm + t + DrawMarginCm;
            float bottom = BoardHeightCm - TagEdgeGapCm - t - DrawMarginCm;
            float drawW = right - left, drawH = bottom - top;

            if (drawW <= 0.1f || drawH <= 0.1f) return Fail("drawing area has no size, check the board settings");

            // top-left, top-right, bottom-right, bottom-left
            Project(h, left, top, 0);
            Project(h, right, top, 1);
            Project(h, right, bottom, 2);
            Project(h, left, bottom, 3);

            double shortest = double.MaxValue;
            double firstSign = 0;
            for (int i = 0; i < 4; i++)
            {
                if (double.IsNaN(quadX[i]) || quadX[i] < 0 || quadX[i] > width - 1
                    || quadY[i] < 0 || quadY[i] > height - 1)
                    return Fail("drawing area not fully in view");

                int j = (i + 1) % 4, k = (i + 2) % 4;
                double ex = quadX[j] - quadX[i], ey = quadY[j] - quadY[i];
                double fx = quadX[k] - quadX[j], fy = quadY[k] - quadY[j];

                shortest = Math.Min(shortest, Math.Sqrt(ex * ex + ey * ey));

                // All turns the same way = a convex four-sided shape. Anything else
                // means the fit folded the board over itself.
                double cross = ex * fy - ey * fx;
                if (i == 0) firstSign = cross;
                else if (cross * firstSign <= 0) return Fail("board outline is not a clean four-sided shape");
            }

            if (shortest < MinEdgePx) return Fail($"board too small in view ({shortest:0}px side, need {MinEdgePx:0})");

            // --- straighten: sample the camera frame through the mapping -----------
            int outW = Math.Max(16, OutputWidth);
            int outH = Math.Max(16, (int)Math.Round(outW * (double)drawH / drawW));

            if (Gray == null || Gray.Length < outW * outH) Gray = new byte[outW * outH];
            OutWidth = outW;
            OutHeight = outH;

            double h0 = h[0], h1 = h[1], h2 = h[2];
            double h3 = h[3], h4 = h[4], h5 = h[5];
            double h6 = h[6], h7 = h[7], h8 = h[8];

            for (int v = 0; v < outH; v++)
            {
                double by = top + (v + 0.5) * drawH / outH;

                // Row 0 of the camera buffer is the bottom of the picture, and the
                // classifier's debug preview flips rows to match, so the straightened
                // picture is stored bottom row first as well.
                int rowStart = (outH - 1 - v) * outW;

                for (int u = 0; u < outW; u++)
                {
                    double bx = left + (u + 0.5) * drawW / outW;

                    double w = h6 * bx + h7 * by + h8;
                    double px = (h0 * bx + h1 * by + h2) / w;
                    double py = (h3 * bx + h4 * by + h5) / w;

                    Gray[rowStart + u] = Sample(frame, width, height, px, py);
                }
            }

            return true;
        }

        // e.g. "tags=4 err 1.6px out 280x187" or "failed: 2 tags, need 3"
        public string Describe()
        {
            return Reason == null
                ? $"tags={TagsUsed} err {MaxErrorPx:0.0}px out {OutWidth}x{OutHeight}"
                : $"failed: {Reason}";
        }

        bool Fail(string reason)
        {
            Reason = reason;
            return false;
        }

        // Fills srcX/srcY (board, cm) and dstX/dstY (picture, px) with the 4 corners of
        // every seen tag. Returns the number of pairs.
        int BuildPairs(BoardTagGate gate)
        {
            int n = 0;
            float t = TagSizeCm;

            for (int id = 0; id < BoardTagGate.BoardTags; id++)
            {
                if (!gate.TagSeen[id]) continue;

                // centre of this printed tag on the board
                float cx = (id & 1) == 0 ? TagEdgeGapCm + t / 2 : BoardWidthCm - TagEdgeGapCm - t / 2;
                float cy = (id & 2) == 0 ? TagEdgeGapCm + t / 2 : BoardHeightCm - TagEdgeGapCm - t / 2;

                // the detector's corners are on a smaller square around that centre
                float half = t * DetectedSquareFraction / 2;

                for (int k = 0; k < 4; k++)
                {
                    // The library lists the corners counter-clockwise from the bottom-left
                    // of the upright tag: 0 bottom-left, 1 bottom-right, 2 top-right,
                    // 3 top-left. (Found on the headset by trying all 8 possible orders:
                    // this one fitted, the other seven were far off.)
                    srcX[n] = k == 0 || k == 3 ? cx - half : cx + half;
                    srcY[n] = k == 0 || k == 1 ? cy + half : cy - half;
                    dstX[n] = gate.CornerX[id * 4 + k];
                    dstY[n] = gate.CornerY[id * 4 + k];
                    n++;
                }
            }

            return n;
        }

        // The largest distance, in picture pixels, between a tag corner and where the fit
        // puts it. double.MaxValue if a corner can't be mapped at all.
        double MeasureError(double[] fit, int n)
        {
            double max = 0;

            for (int i = 0; i < n; i++)
            {
                if (!Homography.Apply(fit, srcX[i], srcY[i], out double px, out double py))
                    return double.MaxValue;

                double dx = px - dstX[i], dy = py - dstY[i];
                double d2 = dx * dx + dy * dy;
                if (d2 > max) max = d2;
            }

            return Math.Sqrt(max);
        }

        void Project(double[] fit, double x, double y, int slot)
        {
            if (!Homography.Apply(fit, x, y, out quadX[slot], out quadY[slot]))
                quadX[slot] = quadY[slot] = double.NaN;
        }

        // Brightness (0..255) of the camera frame at the picture position (px, py),
        // interpolated between the 4 nearest pixels. (px, py) use the detector's
        // picture: y down from the top, so picture row y is buffer row height - 1 - y.
        static byte Sample(ReadOnlySpan<Color32> frame, int width, int height, double px, double py)
        {
            int x0 = (int)Math.Floor(px);
            int y0 = (int)Math.Floor(py);
            double fx = px - x0, fy = py - y0;

            if (x0 < 0) { x0 = 0; fx = 0; }
            else if (x0 > width - 2) { x0 = width - 2; fx = 1; }
            if (y0 < 0) { y0 = 0; fy = 0; }
            else if (y0 > height - 2) { y0 = height - 2; fy = 1; }

            int rowA = (height - 1 - y0) * width;
            int rowB = (height - 2 - y0) * width;

            double top = Luma(frame[rowA + x0]) * (1 - fx) + Luma(frame[rowA + x0 + 1]) * fx;
            double bottom = Luma(frame[rowB + x0]) * (1 - fx) + Luma(frame[rowB + x0 + 1]) * fx;

            return (byte)(top * (1 - fy) + bottom * fy + 0.5);
        }

        // Same weights as the recognizer's own grey conversion.
        static int Luma(Color32 c) => (c.r * 77 + c.g * 150 + c.b * 29) >> 8;
    }
}
