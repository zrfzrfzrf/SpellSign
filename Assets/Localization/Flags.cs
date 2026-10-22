using System.Collections.Generic;
using UnityEngine;

// The flag for each language, for the language buttons on the start menu.
// A PNG at Resources/Flags/<language> (en.png, sv.png ...) is used when there
// is one. Without one, the flag is drawn from code: the Union Jack for "en",
// the Swedish flag for "sv". Any other language gets a plain grey card.
public static class Flags
{
    const int Height = 96;
    const int Supersample = 3; // samples across each pixel, for smooth edges

    static readonly Color32 Grey = new Color32(110, 110, 120, 255);
    static readonly Dictionary<string, Texture2D> made = new Dictionary<string, Texture2D>();

    // Width over height of the flag.
    public static float Aspect(string language)
    {
        Texture2D picture = Picture(language);
        return (float)picture.width / picture.height;
    }

    public static Texture2D Picture(string language)
    {
        if (made.TryGetValue(language, out Texture2D texture) && texture != null) return texture;

        texture = Resources.Load<Texture2D>("Flags/" + language);
        if (texture == null)
        {
            switch (language)
            {
                case "en": texture = Draw(2f, UnionJack); break;
                case "sv": texture = Draw(1.6f, Sweden); break;
                default:   texture = Draw(1.5f, (x, y) => Grey); break;
            }
        }
        made[language] = texture;
        return texture;
    }

    // colorAt: the flag's color at (x, y), both 0..1 from the bottom left.
    static Texture2D Draw(float aspect, System.Func<float, float, Color> colorAt)
    {
        int width = Mathf.RoundToInt(Height * aspect);
        var texture = new Texture2D(width, Height, TextureFormat.RGBA32, true);
        texture.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[width * Height];
        float step = 1f / Supersample;

        for (int py = 0; py < Height; py++)
        for (int px = 0; px < width; px++)
        {
            Color sum = Color.clear;
            for (int sy = 0; sy < Supersample; sy++)
            for (int sx = 0; sx < Supersample; sx++)
                sum += colorAt((px + (sx + 0.5f) * step) / width, (py + (sy + 0.5f) * step) / Height);
            pixels[py * width + px] = sum / (Supersample * Supersample);
        }

        texture.SetPixels(pixels);
        texture.Apply(true);
        return texture;
    }

    // Blue, with a yellow cross set toward the hoist (5 : 2 : 9 across, 4 : 2 : 4 down).
    static Color Sweden(float x, float y)
    {
        float u = x * 16f, v = y * 10f;
        bool cross = (u >= 5f && u <= 7f) || (v >= 4f && v <= 6f);
        return cross ? new Color32(254, 204, 0, 255) : new Color32(0, 106, 167, 255);
    }

    // Measured on the official 60 x 30 grid.
    static Color UnionJack(float x, float y)
    {
        Color blue = new Color32(1, 33, 105, 255);
        Color red = new Color32(200, 16, 46, 255);
        Color white = Color.white;

        float u = x * 60f, v = y * 30f;
        float dx = Mathf.Abs(u - 30f), dy = Mathf.Abs(v - 15f);

        // St George's cross, red on a white border.
        if (dx <= 3f || dy <= 3f) return red;
        if (dx <= 5f || dy <= 5f) return white;

        // The diagonals: white, with a thin red stripe set off-centre, turning
        // like a pinwheel -- below the white in the upper hoist, above it in the upper fly.
        const float length = 67.082f; // sqrt(60^2 + 30^2)
        bool hoist = u < 30f, top = v > 15f;
        float distance = hoist == top
            ? (30f * u + 60f * v - 1800f) / length   // the diagonal from top left to bottom right
            : (60f * v - 30f * u) / length;          // the diagonal from bottom left to top right
        if (Mathf.Abs(distance) > 3f) return blue;

        bool redBelow = hoist; // upper and lower hoist: below the line; fly: above
        bool onRed = redBelow ? distance >= -2f && distance <= 0f : distance >= 0f && distance <= 2f;
        return onRed ? red : white;
    }
}
