// SpellSign - plane-to-picture mapping (homography) fitted from point pairs.
// Pure C#, no UnityEngine types.
//
// A flat board seen by a camera is related to its picture by a 3x3 matrix H:
//
//        [px]     [x]                    px = (h0*x + h1*y + h2) / w
//     w  [py] = H [y]     with           py = (h3*x + h4*y + h5) / w
//        [ 1]     [1]                     w =  h6*x + h7*y + h8
//
// (x, y) is a point on the board, in whatever unit the board is measured in, and
// (px, py) is where the camera sees it. Rotation, tilt, distance and perspective of
// the board are all inside H. H is stored row-major in a double[9], h8 = 1.
//
// Fit() finds the H that maps the most points as closely as possible. 4 pairs are
// enough in theory; with more, the detection noise of each point averages out.

using System;

namespace SpellSign.Sigils
{
    public static class Homography
    {
        // Least-squares fit of H so that H * (srcX[i], srcY[i]) ~ (dstX[i], dstY[i]) for
        // the first `count` pairs. Returns false when the points can't determine an H
        // (fewer than 4 pairs, or all on one line). `h` must have room for 9 values.
        public static bool Fit(double[] srcX, double[] srcY, double[] dstX, double[] dstY, int count, double[] h)
        {
            if (count < 4) return false;

            // Both point sets are first shifted to their centre and scaled to a mean
            // distance of sqrt(2) from it. Without this, board units (cm) and picture
            // units (pixels) differ so much that the equations below become unreliable.
            Normalise(srcX, srcY, count, out double scx, out double scy, out double ss);
            Normalise(dstX, dstY, count, out double dcx, out double dcy, out double ds);

            // Each pair gives two equations in the 8 unknowns h0..h7 (h8 = 1):
            //   h0*x + h1*y + h2 - u*(h6*x + h7*y) = u
            //   h3*x + h4*y + h5 - v*(h6*x + h7*y) = v
            // With more than 4 pairs there are more equations than unknowns, so solve
            // the normal equations (A^T A) h = A^T b, which is the least-squares answer.
            var m = new double[8, 9];
            var row = new double[8];

            for (int i = 0; i < count; i++)
            {
                double x = (srcX[i] - scx) * ss, y = (srcY[i] - scy) * ss;
                double u = (dstX[i] - dcx) * ds, v = (dstY[i] - dcy) * ds;

                row[0] = x; row[1] = y; row[2] = 1; row[3] = 0; row[4] = 0; row[5] = 0; row[6] = -u * x; row[7] = -u * y;
                Accumulate(m, row, u);

                row[0] = 0; row[1] = 0; row[2] = 0; row[3] = x; row[4] = y; row[5] = 1; row[6] = -v * x; row[7] = -v * y;
                Accumulate(m, row, v);
            }

            if (!Solve(m, row)) return false;   // `row` now holds h0..h7

            // Undo the normalisation: H = Td^-1 * Hn * Ts.
            double[] hn = { row[0], row[1], row[2], row[3], row[4], row[5], row[6], row[7], 1.0 };
            double[] ts = { ss, 0, -ss * scx, 0, ss, -ss * scy, 0, 0, 1 };
            double[] tdInv = { 1.0 / ds, 0, dcx, 0, 1.0 / ds, dcy, 0, 0, 1 };

            double[] tmp = Multiply(hn, ts);
            double[] full = Multiply(tdInv, tmp);

            if (Math.Abs(full[8]) < 1e-12) return false;
            for (int i = 0; i < 9; i++) h[i] = full[i] / full[8];
            return true;
        }

        // Where H puts the board point (x, y) in the picture. False when the point ends
        // up at infinity.
        public static bool Apply(double[] h, double x, double y, out double px, out double py)
        {
            double w = h[6] * x + h[7] * y + h[8];
            px = py = 0;
            if (Math.Abs(w) < 1e-12) return false;

            px = (h[0] * x + h[1] * y + h[2]) / w;
            py = (h[3] * x + h[4] * y + h[5]) / w;
            return true;
        }

        static void Normalise(double[] xs, double[] ys, int count, out double cx, out double cy, out double scale)
        {
            cx = cy = 0;
            for (int i = 0; i < count; i++) { cx += xs[i]; cy += ys[i]; }
            cx /= count;
            cy /= count;

            double meanDist = 0;
            for (int i = 0; i < count; i++)
            {
                double dx = xs[i] - cx, dy = ys[i] - cy;
                meanDist += Math.Sqrt(dx * dx + dy * dy);
            }
            meanDist /= count;

            scale = meanDist > 1e-12 ? Math.Sqrt(2.0) / meanDist : 1.0;
        }

        // m += a * a^T and the right-hand column += a * rhs.
        static void Accumulate(double[,] m, double[] a, double rhs)
        {
            for (int r = 0; r < 8; r++)
            {
                for (int c = 0; c < 8; c++) m[r, c] += a[r] * a[c];
                m[r, 8] += a[r] * rhs;
            }
        }

        // Gaussian elimination with partial pivoting on the 8x9 augmented matrix.
        // The solution is written to `solution`. False if the matrix is singular.
        static bool Solve(double[,] m, double[] solution)
        {
            for (int col = 0; col < 8; col++)
            {
                int pivot = col;
                double best = Math.Abs(m[col, col]);
                for (int r = col + 1; r < 8; r++)
                {
                    double a = Math.Abs(m[r, col]);
                    if (a > best) { best = a; pivot = r; }
                }

                if (best < 1e-12) return false;

                if (pivot != col)
                {
                    for (int c = 0; c < 9; c++)
                    {
                        double t = m[col, c];
                        m[col, c] = m[pivot, c];
                        m[pivot, c] = t;
                    }
                }

                for (int r = col + 1; r < 8; r++)
                {
                    double f = m[r, col] / m[col, col];
                    for (int c = col; c < 9; c++) m[r, c] -= f * m[col, c];
                }
            }

            for (int r = 7; r >= 0; r--)
            {
                double sum = m[r, 8];
                for (int c = r + 1; c < 8; c++) sum -= m[r, c] * solution[c];
                solution[r] = sum / m[r, r];
            }

            return true;
        }

        // 3x3 matrix product, row-major.
        static double[] Multiply(double[] a, double[] b)
        {
            var r = new double[9];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    r[i * 3 + j] = a[i * 3] * b[j] + a[i * 3 + 1] * b[3 + j] + a[i * 3 + 2] * b[6 + j];
            return r;
        }
    }
}
