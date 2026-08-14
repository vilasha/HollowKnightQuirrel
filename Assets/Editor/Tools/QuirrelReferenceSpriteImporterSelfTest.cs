using UnityEditor;
using UnityEngine;

namespace Quirrel.EditorTools
{
    /// <summary>
    /// Synthetic-texture verification for QuirrelReferenceSpriteImporter
    /// (Docs/Plans/007_look-up-down-idle-animation-and-pan-tuning.md, Task 1.1's acceptance
    /// criteria: bbox detection excludes the white border and includes the full island; resize
    /// happens on the pre-knockout buffer, confirmed by every partial-alpha pixel in the result
    /// retaining the pristine, undiluted feather midpoint value after a large ~9x downscale -
    /// as opposed to a bilinear blend of it, which is what the forbidden ordering produces).
    ///
    /// Written with plain boolean assertions and a loud Console pass/fail report, mirroring
    /// QuirrelSpriteKnockoutSelfTest's exact pattern (that file/test is NOT modified by this
    /// one - this is a new, independent self-test file for the new tool).
    /// </summary>
    public static class QuirrelReferenceSpriteImporterSelfTest
    {
        // A 108x108 canvas with a diamond-shaped (Manhattan-distance) dark island centered at
        // (53,53) with radius 36. A diamond (rather than a filled solid square, as
        // QuirrelSpriteKnockoutSelfTest uses) is deliberate here: its axis-aligned bounding box
        // touches the shape at exactly one point per edge (top/bottom/left/right), while the
        // box's own four corners remain near-white - this is what makes the synthetic texture a
        // realistic stand-in for an irregular character silhouette (tight bbox, but not a filled
        // rectangle), and it's what lets a border-seeded flood fill still find plenty of
        // near-white pixels to knock out even after cropping tightly to the bbox.
        private const int CanvasSize = 108;
        private const int CenterX = 53;
        private const int CenterY = 53;
        private const int Radius = 36;

        private static readonly Color32 White = new Color32(255, 255, 255, 255);
        private static readonly Color32 Dark = new Color32(20, 20, 20, 255);

        // Mirrors QuirrelSpriteKnockout.ApplyBoundaryFeather's private `featherAlpha` constant
        // (that field is private to that file, so it cannot be referenced directly) - same
        // "single-line mirror of a private value, not a second independently-chosen magic
        // number" discipline QuirrelReferenceSpriteImporter.IsNearWhite already uses for the
        // private near-white predicate. Used below to detect whether a partial-alpha pixel is
        // the pristine, undiluted feather value versus a diluted bilinear blend of it.
        private const byte ExpectedFeatherMidpointAlpha = 127;

        /// <summary>Builds the synthetic test texture described above.</summary>
        public static Color32[] BuildSyntheticTexture(out int width, out int height)
        {
            width = CanvasSize;
            height = CanvasSize;
            var pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int manhattanDistance = System.Math.Abs(x - CenterX) + System.Math.Abs(y - CenterY);
                    pixels[y * width + x] = manhattanDistance <= Radius ? Dark : White;
                }
            }

            return pixels;
        }

        /// <summary>
        /// Runs the tool's pipeline against the synthetic texture and checks every acceptance
        /// criterion. Returns true and logs a success summary if everything passes; logs a
        /// Debug.LogError per failed check and returns false otherwise (same convention as
        /// QuirrelSpriteKnockoutSelfTest.Run()).
        /// </summary>
        public static bool Run()
        {
            bool allPassed = true;

            Color32[] sourcePixels = BuildSyntheticTexture(out int width, out int height);

            // (a) Bounding box detection: excludes the white border, includes the full island.
            bool bboxFound = QuirrelReferenceSpriteImporter.TryComputeContentBoundingBox(
                sourcePixels, width, height, QuirrelSpriteKnockout.DefaultWhiteThreshold,
                out int xMin, out int yMin, out int xMax, out int yMax);

            allPassed &= Check(bboxFound, "Bounding box detection should find the diamond island's content");
            allPassed &= Check(xMin == CenterX - Radius, $"xMin should be {CenterX - Radius}, was {xMin}");
            allPassed &= Check(xMax == CenterX + Radius, $"xMax should be {CenterX + Radius}, was {xMax}");
            allPassed &= Check(yMin == CenterY - Radius, $"yMin should be {CenterY - Radius}, was {yMin}");
            allPassed &= Check(yMax == CenterY + Radius, $"yMax should be {CenterY + Radius}, was {yMax}");

            // Sanity: the tight bbox's own four corners must remain near-white - a diamond does
            // not fill its own axis-aligned bounding box, so this confirms the box is genuinely
            // tight around a non-rectangular silhouette, not just "some box containing everything".
            allPassed &= Check(IsNearWhite(sourcePixels[Index(xMin, yMin, width)]), "Bbox corner (xMin,yMin) should be near-white");
            allPassed &= Check(IsNearWhite(sourcePixels[Index(xMax, yMin, width)]), "Bbox corner (xMax,yMin) should be near-white");
            allPassed &= Check(IsNearWhite(sourcePixels[Index(xMin, yMax, width)]), "Bbox corner (xMin,yMax) should be near-white");
            allPassed &= Check(IsNearWhite(sourcePixels[Index(xMax, yMax, width)]), "Bbox corner (xMax,yMax) should be near-white");

            if (!bboxFound)
            {
                Debug.LogError("[QuirrelReferenceSpriteImporterSelfTest] FAIL - bounding box not found, aborting remaining checks.");
                return false;
            }

            // (b) Resize-before-knockout ordering (Decision 2): a large ~9x downscale of the
            // 73px-tall crop (2*Radius+1) down to a small target height.
            int croppedSize = xMax - xMin + 1; // == yMax - yMin + 1 for this symmetric diamond
            int targetContentHeight = Mathf.Max(1, Mathf.RoundToInt(croppedSize / 9f));

            // CORRECT order (production pipeline, exactly as QuirrelReferenceSpriteImporter.
            // CropResizeAndKnockout runs it): crop -> resize the still-opaque buffer -> knockout+
            // feather on the resized buffer. The feather ring is computed directly at the small
            // target resolution, so it is guaranteed to be a real, target-resolution-wide ring.
            Color32[] correctOrderResult = QuirrelReferenceSpriteImporter.BuildOutputPixels(
                sourcePixels, width, height, targetContentHeight, QuirrelSpriteKnockout.DefaultWhiteThreshold,
                out int outputWidth, out int outputHeight);

            int correctOrderPartialAlphaCount = CountPartialAlphaPixels(correctOrderResult);
            int correctOrderFullyOpaqueCount = CountAlphaEqualTo(correctOrderResult, 255);
            int correctOrderFullyTransparentCount = CountAlphaEqualTo(correctOrderResult, 0);

            allPassed &= Check(correctOrderFullyOpaqueCount > 0, "Correct order result should retain some fully-opaque interior pixels");
            allPassed &= Check(correctOrderFullyTransparentCount > 0, "Correct order result should retain some fully-transparent background pixels");
            allPassed &= Check(correctOrderPartialAlphaCount >= 4,
                $"Correct order (resize-before-knockout) should leave a real feather ring at the target resolution " +
                $"(>=4 partial-alpha pixels expected), found {correctOrderPartialAlphaCount}");

            // WRONG order (the ordering Decision 2 explicitly forbids): knockout+feather at the
            // native crop resolution FIRST, THEN downscale. The 1px feather ring baked in at
            // native (73px) resolution is only ever ~1 source pixel wide, so a plain bilinear
            // resample against a ~9px-spaced grid of destination points blends that ring's alpha
            // (127) together with its 0-alpha/255-alpha neighbors - this is the exact dilution
            // bug Decision 2 is guarding against. Note this does NOT reduce how many partial-
            // alpha pixels land in the output (see the NOTE below the two orders' results are
            // computed) - it dilutes their alpha value away from the pristine 127 instead.
            QuirrelReferenceSpriteImporter.TryComputeContentBoundingBox(
                sourcePixels, width, height, QuirrelSpriteKnockout.DefaultWhiteThreshold,
                out int xMin2, out int yMin2, out int xMax2, out int yMax2);
            Color32[] wrongOrderCropped = QuirrelReferenceSpriteImporter.CropPixels(
                sourcePixels, width, height, xMin2, yMin2, xMax2, yMax2,
                out int croppedWidth, out int croppedHeight);

            QuirrelSpriteKnockout.KnockoutAndFeather(wrongOrderCropped, croppedWidth, croppedHeight);
            Color32[] wrongOrderResult = QuirrelReferenceSpriteImporter.ResizeBilinear(
                wrongOrderCropped, croppedWidth, croppedHeight, outputWidth, outputHeight);

            int wrongOrderPartialAlphaCount = CountPartialAlphaPixels(wrongOrderResult);

            // NOTE: raw partial-alpha PIXEL COUNT alone does not discriminate the two orderings
            // for this synthetic diamond texture - for a symmetric shape like this, both orders
            // land partial-alpha pixels at the same positions (same count), at every scale. What
            // actually differs between the two orders is the ALPHA VALUE at those positions:
            //
            // - Correct order: ApplyBoundaryFeather runs directly on the already-resized target-
            //   resolution buffer, so every feather-ring pixel is assigned the literal constant
            //   127 (ExpectedFeatherMidpointAlpha above) - never touched by interpolation
            //   afterwards. Every partial-alpha pixel here must be exactly the pristine 127.
            // - Wrong order: the 1-native-pixel-wide ring (already baked in at alpha 127) gets
            //   bilinearly blended with its 0-alpha and 255-alpha neighbors during the ~9x
            //   downscale - this is the exact dilution bug Decision 2 guards against. Essentially
            //   none of the resulting partial-alpha pixels survive at exactly 127; they land on a
            //   diluted blended value instead (e.g. ~64 for this synthetic texture - nowhere near
            //   a rounding-boundary coincidence, so this holds regardless of float32 vs float64
            //   rounding-mode differences between an Editor run and any offline check).
            int correctOrderPristineFeatherCount = CountAlphaEqualTo(correctOrderResult, ExpectedFeatherMidpointAlpha);
            int wrongOrderPristineFeatherCount = CountAlphaEqualTo(wrongOrderResult, ExpectedFeatherMidpointAlpha);

            allPassed &= Check(correctOrderPristineFeatherCount == correctOrderPartialAlphaCount,
                $"Correct order (resize-before-knockout) should leave every partial-alpha pixel at the pristine, " +
                $"undiluted feather midpoint ({ExpectedFeatherMidpointAlpha}) - expected {correctOrderPartialAlphaCount}, " +
                $"found {correctOrderPristineFeatherCount}");

            allPassed &= Check(wrongOrderPristineFeatherCount < correctOrderPristineFeatherCount,
                $"Wrong order (knockout-before-resize) should dilute the feather ring away from the pristine midpoint " +
                $"({ExpectedFeatherMidpointAlpha}) via the ~9x bilinear downscale - expected fewer pristine-" +
                $"{ExpectedFeatherMidpointAlpha} pixels ({wrongOrderPristineFeatherCount}) than the correct order's " +
                $"({correctOrderPristineFeatherCount})");

            // (c) Region-based pre-crop overload (Docs/Plans/008_bench-sit-mechanic.md, Task 1.1):
            // BuildOutputPixelsFromRegion must (i) be byte-identical to manually calling CropPixels
            // then BuildOutputPixels in sequence, and (ii) genuinely exclude out-of-region content
            // (a disconnected "contaminant" shape) BEFORE the existing bbox-autodetect step ever
            // runs - not merely happen to produce a similar-looking result.
            allPassed &= RunRegionOverloadChecks();

            if (allPassed)
            {
                Debug.Log("[QuirrelReferenceSpriteImporterSelfTest] PASS - all synthetic-texture checks succeeded: " +
                          "tight bbox correctly excludes the white border and includes the full diamond island, " +
                          $"and resize-before-knockout ordering leaves every partial-alpha pixel at the pristine feather " +
                          $"midpoint ({correctOrderPristineFeatherCount}/{correctOrderPartialAlphaCount} px at " +
                          $"{ExpectedFeatherMidpointAlpha}) versus knockout-before-resize's diluted result " +
                          $"({wrongOrderPristineFeatherCount}/{wrongOrderPartialAlphaCount} px still at " +
                          $"{ExpectedFeatherMidpointAlpha}), and the region-based pre-crop overload is byte-identical " +
                          "to the manual crop+build sequence while excluding an out-of-region contaminant shape.");
            }
            else
            {
                Debug.LogError("[QuirrelReferenceSpriteImporterSelfTest] FAIL - one or more synthetic-texture checks failed, see errors above.");
            }

            return allPassed;
        }

        // Widened canvas for the region-overload check below: same diamond island as
        // BuildSyntheticTexture (centered at (RegionCenterX,RegionCenterY), same radius), plus a
        // second, disconnected dark "contaminant" square placed well to the right, entirely
        // outside both the island's own bounding box AND the region rectangle the test below
        // supplies to BuildOutputPixelsFromRegion. A wider canvas (rather than reusing CanvasSize)
        // is required so the contaminant has room to sit clear of the region without overlapping
        // the island.
        private const int RegionTestCanvasWidth = 200;
        private const int RegionTestCanvasHeight = CanvasSize; // 108, same as BuildSyntheticTexture
        private const int RegionContaminantXMin = 150;
        private const int RegionContaminantXMax = 170;
        private const int RegionContaminantYMin = 40;
        private const int RegionContaminantYMax = 60;

        // The region supplied to BuildOutputPixelsFromRegion: comfortably contains the diamond
        // island (bbox [17,17]-[89,89], per CenterX/CenterY/Radius above) but stops well short of
        // the contaminant (which starts at x=150) - so a correct region-based pre-crop can never
        // see the contaminant's pixels at all.
        private const int RegionXMin = 0;
        private const int RegionYMin = 0;
        private const int RegionXMax = 99;
        private const int RegionYMax = RegionTestCanvasHeight - 1;

        /// <summary>Builds the widened, contaminated synthetic test texture described above.</summary>
        private static Color32[] BuildContaminatedSyntheticTexture(out int width, out int height)
        {
            width = RegionTestCanvasWidth;
            height = RegionTestCanvasHeight;
            var pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int manhattanDistance = System.Math.Abs(x - CenterX) + System.Math.Abs(y - CenterY);
                    bool isIsland = manhattanDistance <= Radius;
                    bool isContaminant = x >= RegionContaminantXMin && x <= RegionContaminantXMax &&
                                          y >= RegionContaminantYMin && y <= RegionContaminantYMax;

                    pixels[y * width + x] = (isIsland || isContaminant) ? Dark : White;
                }
            }

            return pixels;
        }

        /// <summary>
        /// Verifies both Task 1.1 acceptance criteria for
        /// <see cref="QuirrelReferenceSpriteImporter.BuildOutputPixelsFromRegion"/>: byte-identical
        /// output to a manual CropPixels-then-BuildOutputPixels sequence, and genuine exclusion of
        /// an out-of-region contaminant shape.
        /// </summary>
        private static bool RunRegionOverloadChecks()
        {
            bool allPassed = true;

            Color32[] sourcePixels = BuildContaminatedSyntheticTexture(out int width, out int height);
            const int targetContentHeight = 40;

            // Region overload under test.
            Color32[] regionResult = QuirrelReferenceSpriteImporter.BuildOutputPixelsFromRegion(
                sourcePixels, width, height,
                RegionXMin, RegionYMin, RegionXMax, RegionYMax,
                targetContentHeight, QuirrelSpriteKnockout.DefaultWhiteThreshold,
                out int regionOutputWidth, out int regionOutputHeight);

            // Manual equivalent: CropPixels once with the same region, then BuildOutputPixels on
            // that sub-buffer - exactly what BuildOutputPixelsFromRegion is documented to do
            // internally. Both existing methods are used completely unmodified here.
            Color32[] manualCropped = QuirrelReferenceSpriteImporter.CropPixels(
                sourcePixels, width, height, RegionXMin, RegionYMin, RegionXMax, RegionYMax,
                out int manualCroppedWidth, out int manualCroppedHeight);
            Color32[] manualResult = QuirrelReferenceSpriteImporter.BuildOutputPixels(
                manualCropped, manualCroppedWidth, manualCroppedHeight,
                targetContentHeight, QuirrelSpriteKnockout.DefaultWhiteThreshold,
                out int manualOutputWidth, out int manualOutputHeight);

            allPassed &= Check(regionOutputWidth == manualOutputWidth,
                $"Region overload output width ({regionOutputWidth}) should match the manual crop+build " +
                $"sequence's width ({manualOutputWidth})");
            allPassed &= Check(regionOutputHeight == manualOutputHeight,
                $"Region overload output height ({regionOutputHeight}) should match the manual crop+build " +
                $"sequence's height ({manualOutputHeight})");
            allPassed &= Check(ArraysAreByteIdentical(regionResult, manualResult),
                "BuildOutputPixelsFromRegion's output should be byte-identical to manually calling " +
                "CropPixels then BuildOutputPixels in sequence");

            // The island (within the region) is a symmetric diamond, so a contamination-free
            // result must be square. If the region pre-crop failed to exclude the contaminant (or
            // the overload wrongly ran the FULL, un-cropped source through the bbox-autodetect
            // step), the detected bbox would additionally span out to the contaminant at x=170,
            // producing a much wider-than-tall, non-square output - this is what a genuine
            // regression here would look like, not merely a hypothetical.
            allPassed &= Check(regionOutputWidth == regionOutputHeight,
                $"Region overload output should be square (source content within the region is a symmetric " +
                $"diamond) - width {regionOutputWidth}, height {regionOutputHeight}. A mismatch would indicate " +
                "the out-of-region contaminant leaked into the result.");

            // Direct, unambiguous confirmation the contaminant is structurally impossible to reach:
            // the region's own xMax (99) sits below the contaminant's own xMin (150), so the
            // contaminant can never appear in the cropped sub-buffer CropPixels produces, regardless
            // of any bbox-autodetect behavior downstream.
            allPassed &= Check(RegionXMax < RegionContaminantXMin,
                "Test setup invariant: the supplied region must end before the contaminant begins");

            if (allPassed)
            {
                Debug.Log("[QuirrelReferenceSpriteImporterSelfTest] Region overload checks PASS - " +
                          $"{regionOutputWidth}x{regionOutputHeight} output byte-identical to the manual " +
                          "crop+build sequence, with the out-of-region contaminant structurally excluded.");
            }

            return allPassed;
        }

        private static bool ArraysAreByteIdentical(Color32[] a, Color32[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (!a[i].Equals(b[i])) return false;
            }

            return true;
        }

        private static int Index(int x, int y, int width) => y * width + x;

        private static bool IsNearWhite(Color32 c) =>
            c.r >= QuirrelSpriteKnockout.DefaultWhiteThreshold &&
            c.g >= QuirrelSpriteKnockout.DefaultWhiteThreshold &&
            c.b >= QuirrelSpriteKnockout.DefaultWhiteThreshold;

        private static int CountPartialAlphaPixels(Color32[] pixels)
        {
            int count = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a > 0 && pixels[i].a < 255) count++;
            }
            return count;
        }

        private static int CountAlphaEqualTo(Color32[] pixels, byte alpha)
        {
            int count = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a == alpha) count++;
            }
            return count;
        }

        private static bool Check(bool condition, string message)
        {
            if (!condition)
                Debug.LogError("[QuirrelReferenceSpriteImporterSelfTest] CHECK FAILED: " + message);
            return condition;
        }

        [MenuItem("Tools/Quirrel/Run Synthetic Reference Importer Self-Test")]
        private static void RunSelfTestMenuCommand()
        {
            Run();
        }

        /// <summary>
        /// Batch-mode entry point (Unity -executeMethod target), mirroring
        /// QuirrelSpriteKnockoutSelfTest.RunFromCommandLine exactly, so this self-test can also
        /// be run headlessly from the command line. Exits with code 0 on pass, 1 on failure.
        /// </summary>
        public static void RunFromCommandLine()
        {
            bool passed = Run();
            EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
