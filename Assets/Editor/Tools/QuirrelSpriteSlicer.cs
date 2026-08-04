using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Quirrel.EditorTools
{
    /// <summary>
    /// Slicer/knockout Editor tool for the Quirrel sprite sheet
    /// (Docs/Plans/002_quirrel-sprite-animation-player-control.md, Task 1.2).
    ///
    /// Crops each of the 24 verified per-frame bounding boxes out of
    /// Assets/Sprites/Quirrel_Sprites.png and runs the border-connected flood-fill
    /// knockout (QuirrelSpriteKnockout) on each crop independently, writing one RGBA
    /// PNG per frame to a caller-supplied output folder.
    ///
    /// This tool is pure tooling (Task 1.2). It does not decide whether the frames it
    /// produces are visually acceptable, and it does not promote anything into
    /// Assets/Sprites/Quirrel/** - that is Task 1.3's [ART] job. The source sheet is
    /// read via raw file bytes (Texture2D.LoadImage into an in-memory texture), never
    /// via the imported Unity asset, specifically so this tool never needs - and never
    /// risks touching - Read/Write Enabled or any other import setting on the archival
    /// source PNG.
    /// </summary>
    public static class QuirrelSpriteSlicer
    {
        private const string DefaultSourcePath = "Assets/Sprites/Quirrel_Sprites.png";

        /// <summary>
        /// One verified per-frame bounding box, in image-space pixel coordinates with the
        /// origin at the top-left and y increasing downward - the exact convention used by
        /// Docs/Plans/001_pin-head-recolor.md section 1.5, which is the source of truth for
        /// these numbers. xMax/yMax are exclusive (width = xMax - xMin, height = yMax - yMin),
        /// matching that document's own per-frame height notes (e.g. F1: 172-41 = 131px).
        /// </summary>
        [Serializable]
        public struct FrameRect
        {
            public string frameName;
            public int xMin;
            public int yMin;
            public int xMax;
            public int yMax;

            public FrameRect(string frameName, int xMin, int yMin, int xMax, int yMax)
            {
                this.frameName = frameName;
                this.xMin = xMin;
                this.yMin = yMin;
                this.xMax = xMax;
                this.yMax = yMax;
            }

            public int Width => xMax - xMin;
            public int Height => yMax - yMin;
        }

        /// <summary>
        /// The 24 verified frame rects from Docs/Plans/001_pin-head-recolor.md section 1.5,
        /// image-space (top-left origin, y down). Frame IDs F1-F24 match that document's
        /// canonical frame map.
        ///
        /// Caveat carried forward for whoever consumes this table next (Task 1.3): section
        /// 1.5's table gives F22-F24 (Die 1-3) only as three x-ranges in a single collapsed
        /// row, with no per-frame y-range broken out - the only y information available for
        /// the Die band is the aggregate "x 780-1380, y 560-760" note in that document's
        /// section 1.4. The y-range used below for all three Die frames (560-760) is that
        /// aggregate band, not a per-frame-verified box like the other 21 entries. Task 1.3
        /// should re-verify/tighten the Die rects against the actual sheet before accepting
        /// those three output frames.
        /// </summary>
        public static readonly FrameRect[] VerifiedFrameRects = new FrameRect[]
        {
            new FrameRect("Idle_01",            61,   41, 161,  172),
            new FrameRect("Idle_02",            196,  41, 296,  172),
            new FrameRect("Idle_03",            329,  41, 432,  172),
            new FrameRect("Idle_04",            463,  41, 567,  172),
            new FrameRect("Walk_01",            58,   218, 164, 349),
            new FrameRect("Walk_02",            196,  218, 307, 348),
            new FrameRect("Walk_03",            334,  216, 448, 349),
            new FrameRect("Walk_04",            483,  216, 599, 348),
            new FrameRect("Walk_05",            626,  217, 738, 349),
            new FrameRect("Walk_06",            771,  216, 881, 347),
            new FrameRect("Jump_01_Crouch",     937,  208, 1052, 330),
            new FrameRect("Jump_02_Apex",        1084, 151, 1215, 293),
            new FrameRect("Jump_03_Descend",     1241, 197, 1364, 337),
            new FrameRect("Hurt_01",             54,   407, 178,  550),
            new FrameRect("Attack_01",           466,  412, 614,  552),
            new FrameRect("Attack_02",           680,  411, 924,  551),
            new FrameRect("Attack_03",           935,  415, 1100, 552),
            new FrameRect("Attack_04",           1100, 415, 1308, 549),
            new FrameRect("Defend_01_PreRaise",  50,   607, 163,  750),
            new FrameRect("Defend_02_Raise",     224,  610, 424,  751),
            new FrameRect("Defend_03_Held",      457,  611, 629,  750),
            // Die 1-3: x-ranges verified per §1.5 (confirmed pixel-exact against the sheet
            // during Task 1.3 - unchanged). Y-ranges below are Task 1.3's per-frame
            // corrections, replacing the aggregate §1.4 Die band (560-760) called out in the
            // caveat above. The aggregate band was NOT safe to use as-is: at y560-587, x784-823
            // (inside Die_01's x-range) sits the baked-in "DIE" row-label text glyphs, which
            // the aggregate band would have pulled into Die_01's crop as opaque dark pixels
            // sitting on the character's hood. Measured directly off the sheet (near-white
            // threshold >=245, same rule the knockout uses): the label's last non-white row is
            // y587; Die_01's actual character content runs y598-746; Die_02's runs y640-740;
            // Die_03's runs y676-744 (each frame is progressively more collapsed/lower than the
            // last, so a shared y-band was also unnecessarily loose on Die_02/Die_03). Each
            // Die_0N y-range below is that frame's own measured content span plus a ~4px margin.
            new FrameRect("Die_01",              806,  591, 927,  750),
            new FrameRect("Die_02",              983,  636, 1142, 744),
            new FrameRect("Die_03",              1187, 672, 1368, 748),
        };

        [MenuItem("Tools/Quirrel/Slice And Knockout 24 Frames...")]
        private static void SliceAndKnockoutMenuCommand()
        {
            string outputFolder = EditorUtility.OpenFolderPanel(
                "Choose output folder for sliced/knocked-out frames (do NOT choose a folder under Assets/)",
                Application.dataPath + "/..",
                "");

            if (string.IsNullOrEmpty(outputFolder))
                return; // user cancelled

            int written = SliceAndKnockout(DefaultSourcePath, VerifiedFrameRects, outputFolder);
            Debug.Log($"[QuirrelSpriteSlicer] Wrote {written} frame PNG(s) to '{outputFolder}'.");
        }

        [MenuItem("Tools/Quirrel/Run Synthetic Knockout Self-Test")]
        private static void RunSelfTestMenuCommand()
        {
            QuirrelSpriteKnockoutSelfTest.Run();
        }

        /// <summary>
        /// Loads <paramref name="sourceAssetPath"/> as raw bytes (bypassing the Unity import
        /// pipeline entirely - the source texture's import settings, including Read/Write
        /// Enabled, are never touched), crops each of <paramref name="frames"/> out of it, runs
        /// the border-connected flood-fill knockout + 1px feather on each crop independently, and
        /// writes each result as its own RGBA PNG under <paramref name="outputFolder"/>.
        /// </summary>
        /// <param name="sourceAssetPath">Project-relative or absolute path to the source sheet.</param>
        /// <param name="frames">The bounding boxes to crop, image-space top-left/y-down.</param>
        /// <param name="outputFolder">Arbitrary absolute output folder - NOT required to be
        /// under Assets/. Created if it does not already exist.</param>
        /// <returns>Number of frame PNGs written.</returns>
        public static int SliceAndKnockout(string sourceAssetPath, FrameRect[] frames, string outputFolder)
        {
            if (frames == null) throw new ArgumentNullException(nameof(frames));
            if (string.IsNullOrEmpty(outputFolder)) throw new ArgumentException("outputFolder must not be empty", nameof(outputFolder));

            string absoluteSourcePath = ResolveToAbsolutePath(sourceAssetPath);
            if (!File.Exists(absoluteSourcePath))
                throw new FileNotFoundException($"Source sprite sheet not found at '{absoluteSourcePath}'.");

            byte[] sourceBytes = File.ReadAllBytes(absoluteSourcePath);

            // LoadImage always produces a readable, in-memory texture regardless of the source
            // asset's own import settings - this is what lets us treat Quirrel_Sprites.png as
            // strictly read-only.
            var sheet = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!sheet.LoadImage(sourceBytes, markNonReadable: false))
                throw new InvalidDataException($"Failed to decode PNG at '{absoluteSourcePath}'.");

            Directory.CreateDirectory(outputFolder);

            int written = 0;
            foreach (var frame in frames)
            {
                Texture2D cropped = CropFrame(sheet, frame);
                Color32[] pixels = cropped.GetPixels32();
                QuirrelSpriteKnockout.KnockoutAndFeather(pixels, cropped.width, cropped.height);
                cropped.SetPixels32(pixels);
                cropped.Apply(false, false);

                byte[] png = cropped.EncodeToPNG();
                string outPath = Path.Combine(outputFolder, frame.frameName + ".png");
                File.WriteAllBytes(outPath, png);

                UnityEngine.Object.DestroyImmediate(cropped);
                written++;
            }

            UnityEngine.Object.DestroyImmediate(sheet);
            return written;
        }

        /// <summary>
        /// Crops one frame out of <paramref name="sheet"/>, converting the frame's image-space
        /// (top-left origin, y down) bounding box into Unity's Texture2D pixel convention
        /// (bottom-left origin, y up) using the same formula recorded in
        /// Docs/Plans/001_pin-head-recolor.md section 1.5:
        ///   rect.y      = textureHeight - y_image_bottom   (y_image_bottom == frame.yMax)
        ///   rect.height = y_image_bottom - y_image_top      (== frame.Height)
        /// </summary>
        public static Texture2D CropFrame(Texture2D sheet, FrameRect frame)
        {
            int width = frame.Width;
            int height = frame.Height;
            int unityX = frame.xMin;
            int unityY = sheet.height - frame.yMax;

            if (unityX < 0 || unityY < 0 || unityX + width > sheet.width || unityY + height > sheet.height)
            {
                throw new ArgumentOutOfRangeException(nameof(frame),
                    $"Frame '{frame.frameName}' rect falls outside the {sheet.width}x{sheet.height} source sheet.");
            }

            Color[] block = sheet.GetPixels(unityX, unityY, width, height);
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
            result.SetPixels(block);
            result.Apply(false, false);
            return result;
        }

        private static string ResolveToAbsolutePath(string path)
        {
            if (Path.IsPathRooted(path))
                return path;

            // Project-relative (e.g. "Assets/Sprites/Quirrel_Sprites.png").
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, path);
        }
    }
}
