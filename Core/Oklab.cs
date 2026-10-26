using UnityEngine;

namespace MordasGearhold.Core
{
    /// <summary>
    /// The OKLab colour space (Björn Ottosson): lightness and two colour axes with distances that follow what the eye
    /// sees. Zones are found and colours moved in it, so a dark teal and a dark brown stay apart and a new colour
    /// keeps the texture's own light and shadow. sRGB values in and out, as stored in textures.
    /// </summary>
    internal static class Oklab
    {
        private static readonly float[] s_linear = BuildLinear();
        private static readonly byte[] s_srgb = BuildSrgb();

        private static float[] BuildLinear()
        {
            float[] table = new float[256];
            for (int i = 0; i < 256; i++)
            {
                float c = i / 255f;
                table[i] = c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f);
            }
            return table;
        }

        private static byte[] BuildSrgb()
        {
            byte[] table = new byte[4097];
            for (int i = 0; i <= 4096; i++)
            {
                float c = i / 4096f;
                float v = c <= 0.0031308f ? c * 12.92f : 1.055f * Mathf.Pow(c, 1f / 2.4f) - 0.055f;
                table[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
            }
            return table;
        }

        internal static void FromRgb(byte r, byte g, byte b, out float L, out float a, out float bb)
        {
            float lr = s_linear[r], lg = s_linear[g], lb = s_linear[b];
            float l = Cbrt(0.4122214708f * lr + 0.5363325363f * lg + 0.0514459929f * lb);
            float m = Cbrt(0.2119034982f * lr + 0.6806995451f * lg + 0.1073969566f * lb);
            float s = Cbrt(0.0883024619f * lr + 0.2817188376f * lg + 0.6299787005f * lb);
            L = 0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s;
            a = 1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s;
            bb = 0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s;
        }

        internal static void FromColor(Color c, out float L, out float a, out float bb) =>
            FromRgb((byte)Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f), (byte)Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f),
                (byte)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f), out L, out a, out bb);

        internal static void ToRgb(float L, float a, float bb, out byte r, out byte g, out byte b)
        {
            float l = L + 0.3963377774f * a + 0.2158037573f * bb;
            float m = L - 0.1055613458f * a - 0.0638541728f * bb;
            float s = L - 0.0894841775f * a - 1.2914855480f * bb;
            l = l * l * l;
            m = m * m * m;
            s = s * s * s;
            r = Encode(4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s);
            g = Encode(-1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s);
            b = Encode(-0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s);
        }

        private static byte Encode(float linear)
        {
            int i = (int)(Mathf.Clamp01(linear) * 4096f + 0.5f);
            return s_srgb[i];
        }

        private static float Cbrt(float x) => x <= 0f ? 0f : Mathf.Pow(x, 1f / 3f);
    }
}
