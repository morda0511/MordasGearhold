using System.Collections.Generic;
using UnityEngine;

namespace MordasGearhold.Core
{
    /// <summary>
    /// The colour zones of one piece of gear: up to four groups of similar colours found in its texture (e.g. the
    /// grey of the metal, the brown of the leather, the red of the cloth). Every zone can be dyed on its own.
    /// </summary>
    internal sealed class ZoneSet
    {
        private static int s_next = 1;

        internal readonly int Id = s_next++;
        internal readonly Vector3[] Centers; // in Zones.Feature space
        internal readonly Color[] Chips;     // the average colour of each zone, in the original
        internal readonly float[] Share;     // how much of the piece each zone covers (sums to 1)

        internal ZoneSet(Vector3[] centers, Color[] chips, float[] share)
        {
            Centers = centers;
            Chips = chips;
            Share = share;
        }

        internal int Count => Centers.Length;
    }

    /// <summary>
    /// Finds the colour zones of a texture: k-means over a small copy of it, in a space made of the colour wheel
    /// (hue and saturation) plus a little brightness, so light and shadow on the same material stay in one zone
    /// while grey metal and brown leather separate. Zones that are tiny or too close are merged. Done once per
    /// texture and remembered. Other textures of the same piece (the model next to the painted body texture) are
    /// assigned to the same zones by colour (TextureDye.Weights).
    /// </summary>
    internal static class Zones
    {
        internal const int MaxZones = 4;
        private const int SampleSize = 96;
        private const float MinShare = 0.06f;    // smaller zones are merged into their neighbour
        private const float MergeDistance = 0.20f; // zones this close in colour are one zone
        private const float BrightnessWeight = 0.6f;

        private static readonly Dictionary<Texture, ZoneSet> s_sets = new Dictionary<Texture, ZoneSet>();

        /// <summary>The position of a colour in the zone space.</summary>
        internal static Vector3 Feature(float hue, float saturation, float value)
        {
            float angle = hue * Mathf.PI * 2f;
            return new Vector3(Mathf.Cos(angle) * saturation, Mathf.Sin(angle) * saturation, value * BrightnessWeight);
        }

        internal static ZoneSet Of(Texture texture)
        {
            if (texture == null)
            {
                return null;
            }
            if (s_sets.TryGetValue(texture, out ZoneSet known) && known != null)
            {
                return known;
            }
            ZoneSet found = Detect(texture);
            s_sets[texture] = found;
            Plugin.Log.LogInfo($"Colour zones of {texture.name}: {found.Count} ({string.Join(", ", System.Array.ConvertAll(found.Share, s => (s * 100f).ToString("0") + "%"))})");
            return found;
        }

        private static ZoneSet Detect(Texture texture)
        {
            Texture2D small = TextureDye.ReadBack(texture, SampleSize, false);
            Color32[] pixels = small.GetPixels32();
            Object.Destroy(small);

            // armour textures keep their coverage in alpha (0 = skin shows through); model textures often have no
            // useful alpha at all: use it only when a real part of the texture is opaque
            int opaque = 0;
            foreach (Color32 p in pixels)
            {
                if (p.a >= 128) opaque++;
            }
            bool useAlpha = opaque >= pixels.Length / 20;

            List<Vector3> features = new List<Vector3>(pixels.Length);
            List<Color> colors = new List<Color>(pixels.Length);
            for (int pass = 0; pass < 2 && features.Count < 50; pass++)
            {
                features.Clear();
                colors.Clear();
                foreach (Color32 p in pixels)
                {
                    if (useAlpha && p.a < 128) continue;
                    Color.RGBToHSV(p, out float h, out float s, out float v);
                    if (pass == 0 && v < 0.04f) continue; // pure black outlines say nothing about the material
                    features.Add(Feature(h, s, v));
                    colors.Add(new Color(p.r / 255f, p.g / 255f, p.b / 255f));
                }
            }
            if (features.Count == 0)
            {
                return new ZoneSet(new[] { Feature(0f, 0f, 0.5f) }, new[] { new Color(0.5f, 0.5f, 0.5f) }, new[] { 1f });
            }

            Vector3[] centers = KMeans(features, Mathf.Min(MaxZones, 4));
            int[] assign = new int[features.Count];
            float[] counts = new float[centers.Length];
            Assign(features, centers, assign, counts);

            // merge zones that are too small or too close to another one
            bool changed = true;
            while (changed && centers.Length > 1)
            {
                changed = false;
                int smallest = 0;
                for (int i = 1; i < centers.Length; i++)
                {
                    if (counts[i] < counts[smallest]) smallest = i;
                }
                int nearest = NearestOther(centers, smallest);
                bool tiny = counts[smallest] / features.Count < MinShare;
                bool close = Vector3.Distance(centers[smallest], centers[nearest]) < MergeDistance;
                if (!tiny && !close)
                {
                    // the smallest one is fine; look for any other close pair
                    for (int i = 0; i < centers.Length && !changed; i++)
                    {
                        for (int j = i + 1; j < centers.Length; j++)
                        {
                            if (Vector3.Distance(centers[i], centers[j]) < MergeDistance)
                            {
                                smallest = counts[i] < counts[j] ? i : j;
                                nearest = smallest == i ? j : i;
                                changed = true;
                                break;
                            }
                        }
                    }
                    if (!changed) break;
                }
                else
                {
                    changed = true;
                }
                float total = counts[smallest] + counts[nearest];
                centers[nearest] = total > 0f ? (centers[nearest] * counts[nearest] + centers[smallest] * counts[smallest]) / total : centers[nearest];
                List<Vector3> remaining = new List<Vector3>(centers);
                remaining.RemoveAt(smallest);
                centers = remaining.ToArray();
                counts = new float[centers.Length];
                Assign(features, centers, assign, counts);
            }

            // average original colour and share of every zone, biggest zone first
            Vector3[] sums = new Vector3[centers.Length];
            for (int i = 0; i < features.Count; i++)
            {
                Color c = colors[i];
                sums[assign[i]] += new Vector3(c.r, c.g, c.b);
            }
            int n = centers.Length;
            int[] order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            System.Array.Sort(order, (a, b) => counts[b].CompareTo(counts[a]));
            Vector3[] sortedCenters = new Vector3[n];
            Color[] chips = new Color[n];
            float[] share = new float[n];
            for (int i = 0; i < n; i++)
            {
                int k = order[i];
                sortedCenters[i] = centers[k];
                Vector3 mean = counts[k] > 0f ? sums[k] / counts[k] : new Vector3(0.5f, 0.5f, 0.5f);
                chips[i] = new Color(mean.x, mean.y, mean.z, 1f);
                share[i] = counts[k] / features.Count;
            }
            return new ZoneSet(sortedCenters, chips, share);
        }

        private static int NearestOther(Vector3[] centers, int index)
        {
            int best = index == 0 ? 1 : 0;
            for (int i = 0; i < centers.Length; i++)
            {
                if (i != index && (centers[i] - centers[index]).sqrMagnitude < (centers[best] - centers[index]).sqrMagnitude)
                {
                    best = i;
                }
            }
            return best;
        }

        private static void Assign(List<Vector3> features, Vector3[] centers, int[] assign, float[] counts)
        {
            for (int i = 0; i < counts.Length; i++) counts[i] = 0f;
            for (int i = 0; i < features.Count; i++)
            {
                int best = 0;
                float bestDistance = float.MaxValue;
                for (int k = 0; k < centers.Length; k++)
                {
                    float d = (features[i] - centers[k]).sqrMagnitude;
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = k;
                    }
                }
                assign[i] = best;
                counts[best] += 1f;
            }
        }

        /// <summary>Plain k-means with a fixed seed (k-means++ start), so the same texture always gives the same zones.</summary>
        private static Vector3[] KMeans(List<Vector3> features, int k)
        {
            System.Random random = new System.Random(20260101);
            List<Vector3> centers = new List<Vector3> { features[random.Next(features.Count)] };
            float[] distance = new float[features.Count];
            while (centers.Count < k)
            {
                float total = 0f;
                for (int i = 0; i < features.Count; i++)
                {
                    float best = float.MaxValue;
                    foreach (Vector3 c in centers)
                    {
                        best = Mathf.Min(best, (features[i] - c).sqrMagnitude);
                    }
                    distance[i] = best;
                    total += best;
                }
                if (total <= 0f)
                {
                    break; // every sample is already a centre: a single colour
                }
                float pick = (float)random.NextDouble() * total;
                int chosen = features.Count - 1;
                for (int i = 0; i < features.Count; i++)
                {
                    pick -= distance[i];
                    if (pick <= 0f)
                    {
                        chosen = i;
                        break;
                    }
                }
                centers.Add(features[chosen]);
            }

            Vector3[] result = centers.ToArray();
            int[] assign = new int[features.Count];
            float[] counts = new float[result.Length];
            for (int iteration = 0; iteration < 15; iteration++)
            {
                Assign(features, result, assign, counts);
                Vector3[] sums = new Vector3[result.Length];
                for (int i = 0; i < features.Count; i++) sums[assign[i]] += features[i];
                for (int c = 0; c < result.Length; c++)
                {
                    if (counts[c] > 0f) result[c] = sums[c] / counts[c];
                }
            }
            return result;
        }
    }
}
