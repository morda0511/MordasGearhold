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
        internal readonly ZoneParams Params; // the numbers the zones were found with (they also set how softly pixels join them)

        internal ZoneSet(Vector3[] centers, Color[] chips, float[] share, ZoneParams parameters)
        {
            Centers = centers;
            Chips = chips;
            Share = share;
            Params = parameters;
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
        // the numbers of the texture being examined right now (global, or the texture's own from the zones file)
        private static ZoneParams P = DyeTuning.Default;
        internal static int MaxZones => P.MaxZones;
        private static int SampleSize => P.Sample;
        private static float MinShare => P.MinShare;   // smaller zones are merged into their neighbour
        private static int StartClusters => P.StartClusters;
        private static float MergeDistance => P.MergeDistance; // zones this close in colour are one zone


        /// <summary>Drops every remembered zone set (the tuning changed): they are found again with the new numbers.</summary>
        internal static void Forget() => s_sets.Clear();

        private static readonly Dictionary<string, ZoneSet> s_sets = new Dictionary<string, ZoneSet>();
        /// <summary>The position of a colour in the zone space: OKLab with the lightness and colour axes weighted (DyeTuning).</summary>
        internal static Vector3 Feature(float lightness, float a, float b, ZoneParams p) =>
            new Vector3(lightness * p.BrightnessWeight, a * p.ChromaWeight, b * p.ChromaWeight);

        internal static ZoneSet Of(Texture texture) => texture == null ? null : OfSet(new List<Texture> { texture });

        /// <summary>
        /// The zones of everything one piece shows: the colours of all its textures (model textures, the painted body
        /// texture) are looked at together, so every texture is split by the same zones and a zone chip changes what
        /// it says it changes on all of them. The first texture names the set for the per-texture numbers.
        /// </summary>
        internal static ZoneSet OfSet(List<Texture> textures)
        {
            if (textures == null || textures.Count == 0)
            {
                return null;
            }
            string key = string.Join(",", textures.ConvertAll(t => t.GetInstanceID().ToString()));
            if (s_sets.TryGetValue(key, out ZoneSet known) && known != null)
            {
                return known;
            }
            P = DyeTuning.ParamsFor(textures[0].name);
            ZoneSet found = Detect(textures);
            s_sets[key] = found;
            Plugin.Log.LogInfo($"Colour zones of {string.Join(" + ", textures.ConvertAll(t => t.name))}: {found.Count} ({string.Join(", ", System.Array.ConvertAll(found.Share, s => (s * 100f).ToString("0") + "%"))})");
            return found;
        }

        private static ZoneSet Detect(List<Texture> textures)
        {
            List<Vector3> features = new List<Vector3>();
            List<Color> colors = new List<Color>();
            foreach (Texture texture in textures)
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
                // pure black outlines say nothing about the material; keep them only when nothing else is there
                int start = features.Count;
                for (int pass = 0; pass < 2 && features.Count - start < 50; pass++)
                {
                    features.RemoveRange(start, features.Count - start);
                    colors.RemoveRange(start, colors.Count - start);
                    foreach (Color32 p in pixels)
                    {
                        if (useAlpha && p.a < 128) continue;
                        if (pass == 0 && System.Math.Max(p.r, System.Math.Max(p.g, p.b)) < 10) continue;
                        Oklab.FromRgb(p.r, p.g, p.b, out float lab0, out float lab1, out float lab2);
                        features.Add(Feature(lab0, lab1, lab2, P));
                        colors.Add(new Color(p.r / 255f, p.g / 255f, p.b / 255f));
                    }
                }
            }
            if (features.Count == 0)
            {
                return new ZoneSet(new[] { Feature(0.6f, 0f, 0f, P) }, new[] { new Color(0.5f, 0.5f, 0.5f) }, new[] { 1f }, P);
            }

            // start with more clusters than zones, so a small accent colour (teal stripes on brown leather) gets
            // its own cluster, then merge down: tiny ones into their neighbour, close ones together, and the closest
            // pairs until at most MaxZones are left
            Vector3[] centers = KMeans(features, StartClusters);
            int[] assign = new int[features.Count];
            float[] counts = new float[centers.Length];
            Assign(features, centers, assign, counts);
            while (centers.Length > 1)
            {
                int smallest = 0;
                for (int i = 1; i < centers.Length; i++)
                {
                    if (counts[i] < counts[smallest]) smallest = i;
                }
                int from = -1, into = -1;
                if (counts[smallest] / features.Count < MinShare)
                {
                    from = smallest;
                    into = NearestOther(centers, smallest);
                }
                else
                {
                    float closest = float.MaxValue;
                    for (int i = 0; i < centers.Length; i++)
                    {
                        for (int j = i + 1; j < centers.Length; j++)
                        {
                            float d = Vector3.Distance(centers[i], centers[j]);
                            if (d < closest)
                            {
                                closest = d;
                                from = counts[i] < counts[j] ? i : j;
                                into = from == i ? j : i;
                            }
                        }
                    }
                    if (closest >= MergeDistance && centers.Length <= MaxZones)
                    {
                        break; // every zone is its own colour and there are few enough
                    }
                }
                float total = counts[from] + counts[into];
                centers[into] = total > 0f ? (centers[into] * counts[into] + centers[from] * counts[from]) / total : centers[into];
                List<Vector3> remaining = new List<Vector3>(centers);
                remaining.RemoveAt(from);
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
            return new ZoneSet(sortedCenters, chips, share, P);
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
