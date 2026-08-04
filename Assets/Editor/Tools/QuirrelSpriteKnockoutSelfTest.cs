using UnityEngine;

namespace Quirrel.EditorTools
{
    /// <summary>
    /// Synthetic-texture verification for QuirrelSpriteKnockout
    /// (Docs/Plans/002_quirrel-sprite-animation-player-control.md, Task 1.2's acceptance
    /// criterion: "Verified against a synthetic test texture ... only border-connected
    /// white is knocked out, enclosed white survives").
    ///
    /// This project has no EditMode test assembly yet (the first one is scaffolded later,
    /// by Task 3.2's asmdefs) - rather than block Task 1.2 on that unrelated scaffolding,
    /// this self-test runs as an ordinary static method (invokable via
    /// Tools > Quirrel > Run Synthetic Knockout Self-Test, or directly from a Unity MCP
    /// EvaluateCode-style call) and reports pass/fail loudly via the Console. It is written
    /// with plain boolean assertions specifically so it can be lifted verbatim into a real
    /// [Test] once Task 3.2's EditMode assembly exists, with no logic changes required.
    /// </summary>
    public static class QuirrelSpriteKnockoutSelfTest
    {
        private const int CanvasSize = 12;

        // Solid dark island occupying x,y in [3,8] (inclusive) - a 6x6 square.
        private const int IslandMin = 3;
        private const int IslandMax = 8;

        // Enclosed white "eye" strictly inside the island, x,y in [5,6] (inclusive) -
        // a 2x2 region with at least 1px of dark island pixels between it and the
        // island's own edge on every side, so it is topologically enclosed and can never
        // be reached by a flood fill seeded at the canvas border.
        private const int EyeMin = 5;
        private const int EyeMax = 6;

        private static readonly Color32 White = new Color32(255, 255, 255, 255);
        private static readonly Color32 Dark = new Color32(20, 20, 20, 255);

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
                    Color32 c = White;
                    if (x >= IslandMin && x <= IslandMax && y >= IslandMin && y <= IslandMax)
                        c = Dark;
                    if (x >= EyeMin && x <= EyeMax && y >= EyeMin && y <= EyeMax)
                        c = White; // enclosed eye, drawn last so it wins over the island fill
                    pixels[y * width + x] = c;
                }
            }

            return pixels;
        }

        /// <summary>
        /// Runs the algorithm against the synthetic texture and checks every acceptance
        /// criterion. Returns true and logs a success summary if everything passes; logs a
        /// Debug.LogError per failed check and returns false otherwise. Never throws for a
        /// failed check (only for a genuine setup bug), so a caller gets a complete report
        /// of every failing assertion in one run rather than stopping at the first one.
        /// </summary>
        public static bool Run()
        {
            Color32[] pixels = BuildSyntheticTexture(out int width, out int height);

            bool[] background = QuirrelSpriteKnockout.FloodFillBackground(pixels, width, height);
            QuirrelSpriteKnockout.ApplyBoundaryFeather(pixels, background, width, height);

            bool allPassed = true;

            // (a) Border-connected white background is knocked out to alpha 0.
            allPassed &= Check(background[Index(0, 0, width)] == true, "Canvas corner (0,0) should be classified as background");
            allPassed &= Check(pixels[Index(0, 0, width)].a == 0, "Canvas corner (0,0) alpha should be 0 after knockout");
            allPassed &= Check(background[Index(width / 2, 0, width)] == true, "Bottom-edge midpoint should be classified as background");
            allPassed &= Check(pixels[Index(width / 2, 0, width)].a == 0, "Bottom-edge midpoint alpha should be 0 after knockout");

            // (b) Enclosed white "eye" region survives with its original alpha (255) -
            // not reachable from the border via 4-connected flood fill, and not adjacent
            // to any background pixel, so it is also untouched by feathering.
            for (int y = EyeMin; y <= EyeMax; y++)
            {
                for (int x = EyeMin; x <= EyeMax; x++)
                {
                    int idx = Index(x, y, width);
                    allPassed &= Check(background[idx] == false, $"Enclosed eye pixel ({x},{y}) must NOT be classified as background");
                    allPassed &= Check(pixels[idx].a == 255, $"Enclosed eye pixel ({x},{y}) alpha must remain 255 (untouched)");
                    allPassed &= Check(pixels[idx].r == 255 && pixels[idx].g == 255 && pixels[idx].b == 255,
                        $"Enclosed eye pixel ({x},{y}) RGB must remain unchanged white");
                }
            }

            // Sanity: the island's own solid interior (not adjacent to the eye or to the
            // knocked-out background) remains fully opaque and untouched.
            int islandDeepInteriorIdx = Index(IslandMin + 1, IslandMin + 1, width); // e.g. (4,4): dark, not adjacent to background or eye
            allPassed &= Check(background[islandDeepInteriorIdx] == false, "Island deep-interior pixel must not be classified as background");
            allPassed &= Check(pixels[islandDeepInteriorIdx].a == 255, "Island deep-interior pixel alpha must remain fully opaque (not feathered)");

            // (c) 1px feather: the island's outermost ring, directly adjacent to the
            // knocked-out background, must be feathered to the ramp's midpoint (127),
            // not left at the original 255 and not zeroed like true background.
            int islandBorderIdx = Index(IslandMin, IslandMin, width); // (3,3): island edge, adjacent to background at (2,3)/(3,2)
            allPassed &= Check(background[islandBorderIdx] == false, "Island border pixel (3,3) must remain foreground (it is dark, not near-white)");
            allPassed &= Check(pixels[islandBorderIdx].a == 127, "Island border pixel (3,3) must be feathered to alpha 127 (adjacent to background)");

            if (allPassed)
            {
                Debug.Log("[QuirrelSpriteKnockoutSelfTest] PASS - all synthetic-texture checks succeeded: " +
                          "border-connected white knocked out to alpha 0, enclosed white eye untouched at alpha 255, " +
                          "1px feather ring present at alpha 127 on the island boundary.");
            }
            else
            {
                Debug.LogError("[QuirrelSpriteKnockoutSelfTest] FAIL - one or more synthetic-texture checks failed, see errors above.");
            }

            return allPassed;
        }

        private static int Index(int x, int y, int width) => y * width + x;

        private static bool Check(bool condition, string message)
        {
            if (!condition)
                Debug.LogError("[QuirrelSpriteKnockoutSelfTest] CHECK FAILED: " + message);
            return condition;
        }

        /// <summary>
        /// Batch-mode entry point (Unity -executeMethod target) so this self-test can be run
        /// headlessly from the command line as part of building/verifying this tool, without
        /// requiring an interactive Editor session. Exits with code 0 on pass, 1 on failure,
        /// so it is scriptable in CI as well.
        /// </summary>
        public static void RunFromCommandLine()
        {
            bool passed = Run();
            UnityEditor.EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
