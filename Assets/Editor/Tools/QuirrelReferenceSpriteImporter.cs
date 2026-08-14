using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Quirrel.EditorTools
{
    /// <summary>
    /// Crop/resize/knockout Editor tool for arbitrary standalone reference images
    /// (Docs/Plans/007_look-up-down-idle-animation-and-pan-tuning.md, Task 1.1).
    ///
    /// Unlike <see cref="QuirrelSpriteSlicer"/> (built around one known sheet and a table of
    /// pre-verified pixel-exact bounding boxes), this tool detects the tight content bounding
    /// box itself on an arbitrary single reference PNG of unknown resolution, at any resolution,
    /// opaque and on a near-white background - e.g. raw AI-generated reference art that hasn't
    /// been cropped or scaled to match the existing per-frame sprite roster yet.
    ///
    /// Pipeline, in order (Decision 2 of the plan above - this order is load-bearing, do not
    /// reverse it):
    ///   1. Load the source PNG as raw bytes into an in-memory Texture2D (never touches the
    ///      source asset's own import settings - same discipline QuirrelSpriteSlicer uses).
    ///   2. Detect the tightest bounding box containing any pixel that is not near-white, using
    ///      the exact same near-white classification (and the same default threshold constant,
    ///      <see cref="QuirrelSpriteKnockout.DefaultWhiteThreshold"/>) QuirrelSpriteKnockout
    ///      already uses internally.
    ///   3. Crop to that bounding box.
    ///   4. Uniformly bilinear-resize the STILL FULLY OPAQUE crop so its height lands on a
    ///      caller-supplied target - this must happen BEFORE any alpha/knockout work. Doing it
    ///      in the other order (knockout+feather at native resolution, then downscale) would
    ///      resample the already-computed 1px feather ring across many source pixels, diluting
    ///      or erasing it entirely on a large downscale (e.g. the ~9x downscale
    ///      Quirrel_Looking_Down.png needs) - defeating the feather's whole purpose (avoiding a
    ///      bilinear white-fringe artifact).
    ///   5. Run the existing, unmodified <see cref="QuirrelSpriteKnockout.KnockoutAndFeather"/>
    ///      on the resized RGBA buffer.
    ///   6. Encode to PNG and write to a caller-specified output path.
    ///
    /// Steps 2-5 are exposed as pure functions operating on Color32[] buffers (no Texture2D/File
    /// dependency), mirroring QuirrelSpriteKnockout's own separation of pure algorithm from
    /// Editor/File glue, specifically so they can be exercised directly from an EditMode-style
    /// self-test (see QuirrelReferenceSpriteImporterSelfTest) without needing a real file on disk.
    /// </summary>
    public static class QuirrelReferenceSpriteImporter
    {
        [MenuItem("Tools/Quirrel/Crop Region, Resize And Knockout Reference Sprite...")]
        private static void CropRegionResizeAndKnockoutMenuCommand()
        {
            string sourcePath = EditorUtility.OpenFilePanel(
                "Choose source reference PNG (raw, opaque, on a near-white background)",
                Application.dataPath,
                "png");

            if (string.IsNullOrEmpty(sourcePath))
                return; // user cancelled

            string outputFolder = EditorUtility.OpenFolderPanel(
                "Choose output folder for the cropped/resized/knocked-out sprite (do NOT choose a folder under Assets/ on the first pass)",
                Application.dataPath + "/..",
                "");

            if (string.IsNullOrEmpty(outputFolder))
                return; // user cancelled

            if (!RegionAndTargetHeightPromptWindow.TryPromptForRegionAndTargetHeight(
                    out int regionXMin, out int regionYMin, out int regionXMax, out int regionYMax,
                    out int targetContentHeight))
                return; // user cancelled

            string outputPath = Path.Combine(outputFolder, Path.GetFileName(sourcePath));
            CropRegionResizeAndKnockout(
                sourcePath, outputPath, regionXMin, regionYMin, regionXMax, regionYMax, targetContentHeight);
            Debug.Log($"[QuirrelReferenceSpriteImporter] Wrote '{outputPath}' " +
                      $"(region [{regionXMin},{regionYMin}]-[{regionXMax},{regionYMax}], target content height {targetContentHeight}px).");
        }

        [MenuItem("Tools/Quirrel/Crop, Resize And Knockout Reference Sprite...")]
        private static void CropResizeAndKnockoutMenuCommand()
        {
            string sourcePath = EditorUtility.OpenFilePanel(
                "Choose source reference PNG (raw, opaque, on a near-white background)",
                Application.dataPath,
                "png");

            if (string.IsNullOrEmpty(sourcePath))
                return; // user cancelled

            string outputFolder = EditorUtility.OpenFolderPanel(
                "Choose output folder for the cropped/resized/knocked-out sprite (do NOT choose a folder under Assets/ on the first pass)",
                Application.dataPath + "/..",
                "");

            if (string.IsNullOrEmpty(outputFolder))
                return; // user cancelled

            if (!TargetHeightPromptWindow.TryPromptForTargetHeight(out int targetContentHeight))
                return; // user cancelled

            string outputPath = Path.Combine(outputFolder, Path.GetFileName(sourcePath));
            CropResizeAndKnockout(sourcePath, outputPath, targetContentHeight);
            Debug.Log($"[QuirrelReferenceSpriteImporter] Wrote '{outputPath}' (target content height {targetContentHeight}px).");
        }

        /// <summary>
        /// Small blocking prompt window used only to collect the target content height from the
        /// menu command above (Unity has no built-in modal integer-input dialog). Deliberately
        /// minimal - a single IntField plus OK/Cancel - and Editor-only.
        /// </summary>
        private sealed class TargetHeightPromptWindow : EditorWindow
        {
            private const int DefaultTargetHeight = 131; // matches the existing roster's established baseline

            private int _targetHeight = DefaultTargetHeight;
            private bool _confirmed;

            public static bool TryPromptForTargetHeight(out int targetHeight)
            {
                var window = CreateInstance<TargetHeightPromptWindow>();
                window.titleContent = new GUIContent("Target Content Height");
                window.minSize = new Vector2(360, 100);
                window.maxSize = window.minSize;
                window.ShowModal(); // blocks until Close() is called below

                targetHeight = window._targetHeight;
                bool confirmed = window._confirmed;
                DestroyImmediate(window);
                return confirmed;
            }

            private void OnGUI()
            {
                EditorGUILayout.LabelField(
                    "Target content height (px), e.g. 131 to match the existing roster baseline:",
                    EditorStyles.wordWrappedLabel);
                _targetHeight = EditorGUILayout.IntField("Target Height", _targetHeight);

                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("OK"))
                    {
                        if (_targetHeight > 0)
                        {
                            _confirmed = true;
                            Close();
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("Invalid target height", "Target height must be a positive integer.", "OK");
                        }
                    }

                    if (GUILayout.Button("Cancel"))
                    {
                        _confirmed = false;
                        Close();
                    }
                }
            }
        }

        /// <summary>
        /// Small blocking prompt window used only to collect a manual pre-crop region (4 ints)
        /// plus the target content height from the region-based menu command above (Docs/Plans/
        /// 008_bench-sit-mechanic.md, Task 1.1). Reuses <see cref="TargetHeightPromptWindow"/>'s
        /// exact UX pattern (a handful of IntFields plus OK/Cancel, Editor-only, blocking modal)
        /// with four additional IntFields for the region rectangle - deliberately a separate
        /// class rather than a modified <see cref="TargetHeightPromptWindow"/>, so that window's
        /// existing behavior (used by the existing, unmodified single-image menu command) stays
        /// byte-for-byte untouched.
        /// </summary>
        private sealed class RegionAndTargetHeightPromptWindow : EditorWindow
        {
            private const int DefaultTargetHeight = 131; // matches the existing roster's established baseline

            private int _regionXMin;
            private int _regionYMin;
            private int _regionXMax = 100;
            private int _regionYMax = 100;
            private int _targetHeight = DefaultTargetHeight;
            private bool _confirmed;

            public static bool TryPromptForRegionAndTargetHeight(
                out int regionXMin, out int regionYMin, out int regionXMax, out int regionYMax,
                out int targetHeight)
            {
                var window = CreateInstance<RegionAndTargetHeightPromptWindow>();
                window.titleContent = new GUIContent("Region + Target Content Height");
                window.minSize = new Vector2(380, 220);
                window.maxSize = window.minSize;
                window.ShowModal(); // blocks until Close() is called below

                regionXMin = window._regionXMin;
                regionYMin = window._regionYMin;
                regionXMax = window._regionXMax;
                regionYMax = window._regionYMax;
                targetHeight = window._targetHeight;
                bool confirmed = window._confirmed;
                DestroyImmediate(window);
                return confirmed;
            }

            private void OnGUI()
            {
                EditorGUILayout.LabelField(
                    "Manual pre-crop region (inclusive pixel coordinates in the SOURCE image, " +
                    "before any crop/resize - isolate the subject from unrelated content):",
                    EditorStyles.wordWrappedLabel);
                _regionXMin = EditorGUILayout.IntField("Region X Min", _regionXMin);
                _regionYMin = EditorGUILayout.IntField("Region Y Min", _regionYMin);
                _regionXMax = EditorGUILayout.IntField("Region X Max", _regionXMax);
                _regionYMax = EditorGUILayout.IntField("Region Y Max", _regionYMax);

                EditorGUILayout.Space();
                EditorGUILayout.LabelField(
                    "Target content height (px), e.g. 131 to match the existing roster baseline:",
                    EditorStyles.wordWrappedLabel);
                _targetHeight = EditorGUILayout.IntField("Target Height", _targetHeight);

                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("OK"))
                    {
                        bool regionValid = _regionXMin >= 0 && _regionYMin >= 0 &&
                                            _regionXMax >= _regionXMin && _regionYMax >= _regionYMin;

                        if (_targetHeight > 0 && regionValid)
                        {
                            _confirmed = true;
                            Close();
                        }
                        else
                        {
                            EditorUtility.DisplayDialog(
                                "Invalid input",
                                "Region must have non-negative Min values and Max >= Min on both axes, " +
                                "and target height must be a positive integer.",
                                "OK");
                        }
                    }

                    if (GUILayout.Button("Cancel"))
                    {
                        _confirmed = false;
                        Close();
                    }
                }
            }
        }

        /// <summary>
        /// File-based entry point: loads <paramref name="sourceAbsolutePath"/> as raw bytes
        /// (bypassing the Unity import pipeline entirely, identical discipline to
        /// <see cref="QuirrelSpriteSlicer.SliceAndKnockout"/>), runs the full crop/resize/knockout
        /// pipeline, and writes the result as an RGBA PNG to <paramref name="outputAbsolutePath"/>.
        /// </summary>
        /// <param name="sourceAbsolutePath">Absolute (or project-relative) path to the source PNG.</param>
        /// <param name="outputAbsolutePath">Absolute output file path. Parent directory is created
        /// if it does not already exist. Not required to be under Assets/.</param>
        /// <param name="targetContentHeight">Target height, in pixels, for the cropped content
        /// after resize.</param>
        /// <param name="whiteThreshold">Minimum R, G and B value (inclusive) for a pixel to be
        /// considered near-white background, both for bbox detection and for the knockout pass.</param>
        public static void CropResizeAndKnockout(
            string sourceAbsolutePath,
            string outputAbsolutePath,
            int targetContentHeight,
            byte whiteThreshold = QuirrelSpriteKnockout.DefaultWhiteThreshold)
        {
            if (string.IsNullOrEmpty(sourceAbsolutePath)) throw new ArgumentException("sourceAbsolutePath must not be empty", nameof(sourceAbsolutePath));
            if (string.IsNullOrEmpty(outputAbsolutePath)) throw new ArgumentException("outputAbsolutePath must not be empty", nameof(outputAbsolutePath));

            string resolvedSourcePath = ResolveToAbsolutePath(sourceAbsolutePath);
            if (!File.Exists(resolvedSourcePath))
                throw new FileNotFoundException($"Source reference image not found at '{resolvedSourcePath}'.");

            byte[] sourceBytes = File.ReadAllBytes(resolvedSourcePath);

            // LoadImage always produces a readable, in-memory texture regardless of the source
            // asset's own import settings - this is what lets us treat the reference PNG as
            // strictly read-only (never touching Read/Write Enabled or any other import setting).
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!source.LoadImage(sourceBytes, markNonReadable: false))
                    throw new InvalidDataException($"Failed to decode PNG at '{resolvedSourcePath}'.");

                Color32[] sourcePixels = source.GetPixels32();
                Color32[] outputPixels = BuildOutputPixels(
                    sourcePixels, source.width, source.height, targetContentHeight, whiteThreshold,
                    out int outputWidth, out int outputHeight);

                var output = new Texture2D(outputWidth, outputHeight, TextureFormat.RGBA32, false);
                try
                {
                    output.SetPixels32(outputPixels);
                    output.Apply(false, false);

                    byte[] png = output.EncodeToPNG();
                    string outputDir = Path.GetDirectoryName(outputAbsolutePath);
                    if (!string.IsNullOrEmpty(outputDir))
                        Directory.CreateDirectory(outputDir);
                    File.WriteAllBytes(outputAbsolutePath, png);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(output);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        /// <summary>
        /// File-based entry point mirroring <see cref="CropResizeAndKnockout"/> exactly, plus a
        /// caller-supplied manual pre-crop region (Docs/Plans/008_bench-sit-mechanic.md, Task
        /// 1.1): loads <paramref name="sourceAbsolutePath"/> as raw bytes, runs the region-based
        /// pipeline (see <see cref="BuildOutputPixelsFromRegion"/>), and writes the result as an
        /// RGBA PNG to <paramref name="outputAbsolutePath"/>. Deliberately duplicates
        /// <see cref="CropResizeAndKnockout"/>'s own file load/write plumbing verbatim rather than
        /// refactoring it into a shared private helper, so that method's existing body is left
        /// completely untouched (regression discipline, Task 1.1's acceptance criteria).
        /// </summary>
        /// <param name="sourceAbsolutePath">Absolute (or project-relative) path to the source PNG.</param>
        /// <param name="outputAbsolutePath">Absolute output file path. Parent directory is created
        /// if it does not already exist. Not required to be under Assets/.</param>
        /// <param name="regionXMin">Inclusive left edge, in source-image pixel coordinates, of the
        /// manual pre-crop region.</param>
        /// <param name="regionYMin">Inclusive top edge, in source-image pixel coordinates, of the
        /// manual pre-crop region.</param>
        /// <param name="regionXMax">Inclusive right edge, in source-image pixel coordinates, of the
        /// manual pre-crop region.</param>
        /// <param name="regionYMax">Inclusive bottom edge, in source-image pixel coordinates, of the
        /// manual pre-crop region.</param>
        /// <param name="targetContentHeight">Target height, in pixels, for the cropped content
        /// after resize.</param>
        /// <param name="whiteThreshold">Minimum R, G and B value (inclusive) for a pixel to be
        /// considered near-white background, both for bbox detection and for the knockout pass.</param>
        public static void CropRegionResizeAndKnockout(
            string sourceAbsolutePath,
            string outputAbsolutePath,
            int regionXMin,
            int regionYMin,
            int regionXMax,
            int regionYMax,
            int targetContentHeight,
            byte whiteThreshold = QuirrelSpriteKnockout.DefaultWhiteThreshold)
        {
            if (string.IsNullOrEmpty(sourceAbsolutePath)) throw new ArgumentException("sourceAbsolutePath must not be empty", nameof(sourceAbsolutePath));
            if (string.IsNullOrEmpty(outputAbsolutePath)) throw new ArgumentException("outputAbsolutePath must not be empty", nameof(outputAbsolutePath));

            string resolvedSourcePath = ResolveToAbsolutePath(sourceAbsolutePath);
            if (!File.Exists(resolvedSourcePath))
                throw new FileNotFoundException($"Source reference image not found at '{resolvedSourcePath}'.");

            byte[] sourceBytes = File.ReadAllBytes(resolvedSourcePath);

            // LoadImage always produces a readable, in-memory texture regardless of the source
            // asset's own import settings - this is what lets us treat the reference PNG as
            // strictly read-only (never touching Read/Write Enabled or any other import setting).
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!source.LoadImage(sourceBytes, markNonReadable: false))
                    throw new InvalidDataException($"Failed to decode PNG at '{resolvedSourcePath}'.");

                Color32[] sourcePixels = source.GetPixels32();
                Color32[] outputPixels = BuildOutputPixelsFromRegion(
                    sourcePixels, source.width, source.height,
                    regionXMin, regionYMin, regionXMax, regionYMax,
                    targetContentHeight, whiteThreshold,
                    out int outputWidth, out int outputHeight);

                var output = new Texture2D(outputWidth, outputHeight, TextureFormat.RGBA32, false);
                try
                {
                    output.SetPixels32(outputPixels);
                    output.Apply(false, false);

                    byte[] png = output.EncodeToPNG();
                    string outputDir = Path.GetDirectoryName(outputAbsolutePath);
                    if (!string.IsNullOrEmpty(outputDir))
                        Directory.CreateDirectory(outputDir);
                    File.WriteAllBytes(outputAbsolutePath, png);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(output);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        /// <summary>
        /// Pure-logic pipeline entry point (no Texture2D/File dependency): detects the content
        /// bbox, crops, resizes the still-opaque crop to <paramref name="targetContentHeight"/>,
        /// then runs knockout+feather on the resized buffer - in that order (Decision 2). Exposed
        /// separately from <see cref="CropResizeAndKnockout"/> so it can be exercised directly by
        /// <c>QuirrelReferenceSpriteImporterSelfTest</c> against a synthetic in-memory buffer.
        /// </summary>
        /// <exception cref="InvalidOperationException">The source buffer has no pixel that is not
        /// near-white (nothing to crop to).</exception>
        public static Color32[] BuildOutputPixels(
            Color32[] sourcePixels,
            int sourceWidth,
            int sourceHeight,
            int targetContentHeight,
            byte whiteThreshold,
            out int outputWidth,
            out int outputHeight)
        {
            if (targetContentHeight <= 0) throw new ArgumentException("targetContentHeight must be positive", nameof(targetContentHeight));

            if (!TryComputeContentBoundingBox(sourcePixels, sourceWidth, sourceHeight, whiteThreshold,
                    out int xMin, out int yMin, out int xMax, out int yMax))
            {
                throw new InvalidOperationException(
                    "No non-near-white content found in the source buffer - image appears to be entirely near-white.");
            }

            Color32[] cropped = CropPixels(sourcePixels, sourceWidth, sourceHeight, xMin, yMin, xMax, yMax,
                out int croppedWidth, out int croppedHeight);

            float scale = targetContentHeight / (float)croppedHeight;
            outputWidth = Mathf.Max(1, Mathf.RoundToInt(croppedWidth * scale));
            outputHeight = Mathf.Max(1, Mathf.RoundToInt(croppedHeight * scale));

            // Resize the still-fully-opaque crop BEFORE any alpha/knockout work (Decision 2).
            Color32[] resized = ResizeBilinear(cropped, croppedWidth, croppedHeight, outputWidth, outputHeight);

            QuirrelSpriteKnockout.KnockoutAndFeather(resized, outputWidth, outputHeight, whiteThreshold);

            return resized;
        }

        /// <summary>
        /// Pure-logic pipeline entry point with an explicit, caller-supplied manual pre-crop
        /// region (Docs/Plans/008_bench-sit-mechanic.md, Task 1.1) - for source images where the
        /// subject shares a canvas with other content the bbox-autodetect step alone cannot
        /// separate (e.g. Quirrel composited on a bench in the same photo). Calls the existing,
        /// unmodified <see cref="CropPixels"/> exactly once with <paramref name="regionXMin"/>/
        /// <paramref name="regionYMin"/>/<paramref name="regionXMax"/>/<paramref name="regionYMax"/>,
        /// then delegates entirely to the existing, unmodified <see cref="BuildOutputPixels"/> on
        /// that sub-buffer - which itself still auto-tightens the bbox, resizes, and knocks out.
        /// No new cropping/bbox/resize/knockout algorithm is introduced here; this is glue only.
        /// </summary>
        /// <param name="regionXMin">Inclusive left edge, in <paramref name="sourcePixels"/>
        /// coordinates, of the manual pre-crop region.</param>
        /// <param name="regionYMin">Inclusive top edge, in <paramref name="sourcePixels"/>
        /// coordinates, of the manual pre-crop region.</param>
        /// <param name="regionXMax">Inclusive right edge, in <paramref name="sourcePixels"/>
        /// coordinates, of the manual pre-crop region.</param>
        /// <param name="regionYMax">Inclusive bottom edge, in <paramref name="sourcePixels"/>
        /// coordinates, of the manual pre-crop region.</param>
        /// <exception cref="InvalidOperationException">The region sub-buffer has no pixel that is
        /// not near-white (nothing to crop to) once the existing bbox-autodetect step runs on
        /// it.</exception>
        public static Color32[] BuildOutputPixelsFromRegion(
            Color32[] sourcePixels,
            int sourceWidth,
            int sourceHeight,
            int regionXMin,
            int regionYMin,
            int regionXMax,
            int regionYMax,
            int targetContentHeight,
            byte whiteThreshold,
            out int outputWidth,
            out int outputHeight)
        {
            Color32[] regionPixels = CropPixels(
                sourcePixels, sourceWidth, sourceHeight,
                regionXMin, regionYMin, regionXMax, regionYMax,
                out int regionWidth, out int regionHeight);

            return BuildOutputPixels(
                regionPixels, regionWidth, regionHeight, targetContentHeight, whiteThreshold,
                out outputWidth, out outputHeight);
        }

        /// <summary>
        /// Scans <paramref name="pixels"/> (row-major, matching Texture2D.GetPixels32 layout) for
        /// the tightest axis-aligned bounding box containing any pixel that is NOT near-white,
        /// using the same near-white classification (R, G and B all &gt;= <paramref
        /// name="whiteThreshold"/>) <see cref="QuirrelSpriteKnockout"/> already uses internally.
        /// </summary>
        /// <returns>False if every pixel in the buffer is near-white (no content to bound).</returns>
        public static bool TryComputeContentBoundingBox(
            Color32[] pixels, int width, int height, byte whiteThreshold,
            out int xMin, out int yMin, out int xMax, out int yMax)
        {
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            if (pixels.Length != width * height)
                throw new ArgumentException($"pixels.Length ({pixels.Length}) does not match width*height ({width * height})");

            xMin = width;
            yMin = height;
            xMax = -1;
            yMax = -1;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!IsNearWhite(pixels[y * width + x], whiteThreshold))
                    {
                        if (x < xMin) xMin = x;
                        if (x > xMax) xMax = x;
                        if (y < yMin) yMin = y;
                        if (y > yMax) yMax = y;
                    }
                }
            }

            return xMax >= xMin && yMax >= yMin;
        }

        // Same predicate QuirrelSpriteKnockout.IsNearWhite implements (that method is private to
        // that file, so it cannot be called directly) - kept as a single-line mirror rather than a
        // copy-pasted magic number: the threshold value itself always flows from the shared
        // QuirrelSpriteKnockout.DefaultWhiteThreshold constant (see method default parameters
        // above), never a second hardcoded literal.
        private static bool IsNearWhite(Color32 c, byte whiteThreshold)
        {
            return c.r >= whiteThreshold && c.g >= whiteThreshold && c.b >= whiteThreshold;
        }

        /// <summary>
        /// Crops <paramref name="pixels"/> to the inclusive [xMin,xMax]x[yMin,yMax] box, returning
        /// a new row-major Color32[] buffer of the cropped size.
        /// </summary>
        public static Color32[] CropPixels(
            Color32[] pixels, int width, int height,
            int xMin, int yMin, int xMax, int yMax,
            out int croppedWidth, out int croppedHeight)
        {
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            if (pixels.Length != width * height)
                throw new ArgumentException($"pixels.Length ({pixels.Length}) does not match width*height ({width * height})");
            if (xMin < 0 || yMin < 0 || xMax >= width || yMax >= height || xMax < xMin || yMax < yMin)
                throw new ArgumentOutOfRangeException(nameof(xMax), "Crop box falls outside the source buffer or is degenerate.");

            croppedWidth = xMax - xMin + 1;
            croppedHeight = yMax - yMin + 1;
            var result = new Color32[croppedWidth * croppedHeight];

            for (int y = 0; y < croppedHeight; y++)
            {
                for (int x = 0; x < croppedWidth; x++)
                {
                    result[y * croppedWidth + x] = pixels[(y + yMin) * width + (x + xMin)];
                }
            }

            return result;
        }

        /// <summary>
        /// Uniform bilinear resize of a row-major Color32[] buffer to an arbitrary target size.
        /// Destination pixel centers are mapped back into source space
        /// (<c>(dst + 0.5) * srcSize/dstSize - 0.5</c>) and sampled from their surrounding 2x2
        /// source neighborhood, clamped at the source edges (standard bilinear resample - no
        /// mip/box-filtering pre-pass, matching a plain texture-filtering resize).
        /// </summary>
        public static Color32[] ResizeBilinear(Color32[] source, int srcWidth, int srcHeight, int dstWidth, int dstHeight)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.Length != srcWidth * srcHeight)
                throw new ArgumentException($"source.Length ({source.Length}) does not match srcWidth*srcHeight ({srcWidth * srcHeight})");
            if (dstWidth <= 0 || dstHeight <= 0)
                throw new ArgumentException("dstWidth and dstHeight must both be positive");

            var dst = new Color32[dstWidth * dstHeight];

            float xRatio = srcWidth / (float)dstWidth;
            float yRatio = srcHeight / (float)dstHeight;

            for (int dy = 0; dy < dstHeight; dy++)
            {
                float srcYf = (dy + 0.5f) * yRatio - 0.5f;
                int y0 = Mathf.FloorToInt(srcYf);
                float fy = srcYf - y0;
                int y0c = Mathf.Clamp(y0, 0, srcHeight - 1);
                int y1c = Mathf.Clamp(y0 + 1, 0, srcHeight - 1);

                for (int dx = 0; dx < dstWidth; dx++)
                {
                    float srcXf = (dx + 0.5f) * xRatio - 0.5f;
                    int x0 = Mathf.FloorToInt(srcXf);
                    float fx = srcXf - x0;
                    int x0c = Mathf.Clamp(x0, 0, srcWidth - 1);
                    int x1c = Mathf.Clamp(x0 + 1, 0, srcWidth - 1);

                    Color32 c00 = source[y0c * srcWidth + x0c];
                    Color32 c10 = source[y0c * srcWidth + x1c];
                    Color32 c01 = source[y1c * srcWidth + x0c];
                    Color32 c11 = source[y1c * srcWidth + x1c];

                    dst[dy * dstWidth + dx] = BilerpColor32(c00, c10, c01, c11, fx, fy);
                }
            }

            return dst;
        }

        private static Color32 BilerpColor32(Color32 c00, Color32 c10, Color32 c01, Color32 c11, float fx, float fy)
        {
            float r = Mathf.Lerp(Mathf.Lerp(c00.r, c10.r, fx), Mathf.Lerp(c01.r, c11.r, fx), fy);
            float g = Mathf.Lerp(Mathf.Lerp(c00.g, c10.g, fx), Mathf.Lerp(c01.g, c11.g, fx), fy);
            float b = Mathf.Lerp(Mathf.Lerp(c00.b, c10.b, fx), Mathf.Lerp(c01.b, c11.b, fx), fy);
            float a = Mathf.Lerp(Mathf.Lerp(c00.a, c10.a, fx), Mathf.Lerp(c01.a, c11.a, fx), fy);

            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(r), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(g), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(b), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(a), 0, 255));
        }

        private static string ResolveToAbsolutePath(string path)
        {
            if (Path.IsPathRooted(path))
                return path;

            // Project-relative (e.g. "Assets/Sprites/Reference/Quirrel_Looking_Up.png").
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, path);
        }
    }
}
