using System.Collections.Generic;
using UnityEngine;

namespace Quirrel.EditorTools
{
    /// <summary>
    /// Pure background-knockout algorithm for the Quirrel sprite pipeline
    /// (Docs/Plans/002_quirrel-sprite-animation-player-control.md, Task 1.2;
    /// technique locked in by Docs/Plans/002 section 1.2).
    ///
    /// Deliberately engine-adjacent but not Unity-Editor-specific beyond Color32/Texture2D
    /// types, so it can be exercised directly from EditMode tests or a menu command alike.
    ///
    /// Algorithm (do not replace with a global luminance/color threshold - see plan
    /// rationale): a border-connected 4-connected flood fill starting from the four
    /// canvas edges, walking through pixels whose R, G and B channels are all >= threshold
    /// (default 245). Only pixels reachable from the border through such near-white pixels
    /// are knocked out to alpha 0. Any near-white pixel that is topologically enclosed by
    /// darker outline/body pixels (e.g. an eye highlight, a hood highlight) is never visited
    /// by a flood fill that starts at the border, so it is left completely untouched -
    /// this is what makes the algorithm safe against punching holes in enclosed art.
    /// </summary>
    public static class QuirrelSpriteKnockout
    {
        public const byte DefaultWhiteThreshold = 245;

        /// <summary>
        /// Runs the border-connected flood fill over <paramref name="pixels"/> (a row-major
        /// Color32 buffer matching Texture2D.GetPixels32/SetPixels32 layout - index 0 is the
        /// bottom-left texel, x increases first, then y increases upward; this orientation is
        /// irrelevant to correctness since all four edges are treated symmetrically as the
        /// flood-fill seed).
        /// </summary>
        /// <param name="pixels">Pixel buffer, mutated in place: background pixels have their
        /// alpha set to 0.</param>
        /// <param name="width">Buffer width in pixels.</param>
        /// <param name="height">Buffer height in pixels.</param>
        /// <param name="whiteThreshold">Minimum R, G and B value (inclusive) for a pixel to be
        /// considered "near-white" and eligible for the flood fill.</param>
        /// <returns>A width*height bool mask, true where the pixel was classified as
        /// border-connected background (and therefore knocked out).</returns>
        public static bool[] FloodFillBackground(Color32[] pixels, int width, int height, byte whiteThreshold = DefaultWhiteThreshold)
        {
            if (pixels == null) throw new System.ArgumentNullException(nameof(pixels));
            if (pixels.Length != width * height)
                throw new System.ArgumentException($"pixels.Length ({pixels.Length}) does not match width*height ({width * height})");

            var background = new bool[width * height];
            var visited = new bool[width * height];
            var queue = new Queue<int>();

            // Seed the queue with every near-white pixel on the four edges.
            for (int x = 0; x < width; x++)
            {
                TrySeed(x, 0, pixels, width, whiteThreshold, visited, queue);
                TrySeed(x, height - 1, pixels, width, whiteThreshold, visited, queue);
            }
            for (int y = 0; y < height; y++)
            {
                TrySeed(0, y, pixels, width, whiteThreshold, visited, queue);
                TrySeed(width - 1, y, pixels, width, whiteThreshold, visited, queue);
            }

            // Standard 4-connected BFS flood fill.
            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                background[idx] = true;

                int x = idx % width;
                int y = idx / width;

                TryVisit(x - 1, y, pixels, width, height, whiteThreshold, visited, queue);
                TryVisit(x + 1, y, pixels, width, height, whiteThreshold, visited, queue);
                TryVisit(x, y - 1, pixels, width, height, whiteThreshold, visited, queue);
                TryVisit(x, y + 1, pixels, width, height, whiteThreshold, visited, queue);
            }

            for (int i = 0; i < pixels.Length; i++)
            {
                if (background[i])
                {
                    var c = pixels[i];
                    c.a = 0;
                    pixels[i] = c;
                }
            }

            return background;
        }

        private static void TrySeed(int x, int y, Color32[] pixels, int width, byte whiteThreshold, bool[] visited, Queue<int> queue)
        {
            int idx = y * width + x;
            if (visited[idx]) return;
            visited[idx] = true;
            if (IsNearWhite(pixels[idx], whiteThreshold))
                queue.Enqueue(idx);
        }

        private static void TryVisit(int x, int y, Color32[] pixels, int width, int height, byte whiteThreshold, bool[] visited, Queue<int> queue)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return;
            int idx = y * width + x;
            if (visited[idx]) return;
            visited[idx] = true;
            if (IsNearWhite(pixels[idx], whiteThreshold))
                queue.Enqueue(idx);
        }

        private static bool IsNearWhite(Color32 c, byte whiteThreshold)
        {
            return c.r >= whiteThreshold && c.g >= whiteThreshold && c.b >= whiteThreshold;
        }

        /// <summary>
        /// Applies a 1px alpha feather at the boundary produced by <see cref="FloodFillBackground"/>,
        /// so a subsequent bilinear filter doesn't blend a hard-alpha edge against the fully-opaque
        /// original white and produce a visible fringe. Background pixels (mask == true) are already
        /// alpha 0. Any foreground pixel (mask == false) that is 4-adjacent to a background pixel is
        /// the single feather ring: it is set to the ramp's midpoint alpha (127) rather than left at
        /// its original alpha, giving a 0 (background) -> 127 (feather ring) -> 255 (interior) linear
        /// ramp across the 1px boundary. All other foreground pixels are untouched.
        /// </summary>
        public static void ApplyBoundaryFeather(Color32[] pixels, bool[] background, int width, int height)
        {
            if (pixels == null) throw new System.ArgumentNullException(nameof(pixels));
            if (background == null) throw new System.ArgumentNullException(nameof(background));
            if (pixels.Length != width * height || background.Length != width * height)
                throw new System.ArgumentException("pixels/background length does not match width*height");

            const byte featherAlpha = 127;

            // Compute which foreground pixels touch background first, then apply -
            // never read a value that a previous mutation in this same pass already changed.
            var featherRing = new bool[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    if (background[idx]) continue; // only foreground pixels can be a feather ring

                    if (IsBackground(x - 1, y, width, height, background) ||
                        IsBackground(x + 1, y, width, height, background) ||
                        IsBackground(x, y - 1, width, height, background) ||
                        IsBackground(x, y + 1, width, height, background))
                    {
                        featherRing[idx] = true;
                    }
                }
            }

            for (int i = 0; i < pixels.Length; i++)
            {
                if (!featherRing[i]) continue;
                var c = pixels[i];
                c.a = featherAlpha;
                pixels[i] = c;
            }
        }

        private static bool IsBackground(int x, int y, int width, int height, bool[] background)
        {
            if (x < 0 || x >= width || y < 0 || y >= height) return false; // out-of-bounds is not background
            return background[y * width + x];
        }

        /// <summary>
        /// Convenience entry point: runs the flood fill and then the feather pass in the
        /// correct order over the same buffer.
        /// </summary>
        public static void KnockoutAndFeather(Color32[] pixels, int width, int height, byte whiteThreshold = DefaultWhiteThreshold)
        {
            var background = FloodFillBackground(pixels, width, height, whiteThreshold);
            ApplyBoundaryFeather(pixels, background, width, height);
        }
    }
}
