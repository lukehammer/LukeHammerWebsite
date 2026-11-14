using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BlazorApp.Shared.PhotoShoot
{
    /// <summary>
    /// Sorting and recommending for a completed shoot's uploads. Pure functions: the browser runs the image
    /// analysis (sharpness, exposure) and the QR scan, then hands the numbers here. Thresholds live in one place.
    /// See docs/photoshoot/POC-DECISIONS.md.
    /// </summary>
    public static class PhotoShootUploadSort
    {
        public const string QrPrefix = "lhp:";
        private static readonly Regex QrPattern = new(@"^lhp:shoot/([A-Za-z0-9-]{1,64})/shot/([A-Za-z0-9-]{1,64})$", RegexOptions.CultureInvariant);

        /// <summary>Payload drawn on the marker screen for the current shot.</summary>
        public static string QrPayload(string shootId, string shotId) => $"lhp:shoot/{shootId}/shot/{shotId}";

        /// <summary>Reads a marker payload. Anything else (including other QR codes) returns false.</summary>
        public static bool TryParseQr(string? text, out string shootId, out string shotId)
        {
            shootId = "";
            shotId = "";
            var match = QrPattern.Match(text ?? "");
            if (!match.Success) return false;
            shootId = match.Groups[1].Value;
            shotId = match.Groups[2].Value;
            return true;
        }

        public static class Thresholds
        {
            /// <summary>Laplacian variance of the 1280 px preview, normalised to 0..1 by the browser. Below this is blurry.</summary>
            public const double MinSharpness = 0.12;
            /// <summary>Fraction of pixels at pure black above this fails the frame (crushed shadows).</summary>
            public const double MaxClippedShadows = 0.10;
        }

        /// <summary>Numbers the browser computes for one upload, from a downscaled preview.</summary>
        public class FrameMetrics
        {
            /// <summary>Sharpness 0..1 (normalised Laplacian variance).</summary>
            public double Sharpness { get; set; }
            /// <summary>Mean luminance 0..1.</summary>
            public double MeanLuma { get; set; }
            public double ClippedHighlights { get; set; }
            public double ClippedShadows { get; set; }
        }

        private static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;

        public static (bool Passed, string Reason) Check(FrameMetrics m)
        {
            if (m.Sharpness < Thresholds.MinSharpness) return (false, "Too blurry");
            // Highlights are not a fail: the white seamless backdrop is lit to pure white on purpose. They still lower the score.
            if (m.ClippedShadows > Thresholds.MaxClippedShadows) return (false, "Crushed shadows");
            return (true, "");
        }

        /// <summary>Quality 0..1 for frames that passed. Sharpness counts most; exposure balance and clipping add the rest.</summary>
        public static double Score(FrameMetrics m)
        {
            var sharp = Clamp01(m.Sharpness);
            var balance = 1 - Clamp01(Math.Abs(m.MeanLuma - 0.5) * 2);
            var clipping = 1 - Clamp01((m.ClippedHighlights + m.ClippedShadows) * 5);
            return Math.Round(0.6 * sharp + 0.25 * balance + 0.15 * clipping, 4);
        }

        /// <summary>One upload as the browser reports it.</summary>
        public class Frame
        {
            public string Id { get; set; } = "";
            public DateTime CapturedUtc { get; set; }
            /// <summary>QR text if one was read from the frame, else null.</summary>
            public string? QrText { get; set; }
            public FrameMetrics Metrics { get; set; } = new();
        }

        public class SortedFrame
        {
            public string Id { get; set; } = "";
            public DateTime CapturedUtc { get; set; }
            /// <summary>Shot id, or empty when the frame is unsorted.</summary>
            public string ShotId { get; set; } = "";
            /// <summary>QrRun, MarkerFrame, or Unsorted.</summary>
            public string Evidence { get; set; } = "";
            public bool Passed { get; set; }
            public string FailReason { get; set; } = "";
            public double Score { get; set; }
            public bool Recommended { get; set; }
        }

        /// <summary>
        /// Orders frames by capture time. A marker QR for this shoot starts a run: that frame and each following
        /// frame belong to its shot until the next marker. Frames before the first marker, or with a marker from
        /// another shoot, stay unsorted for Luke. Missed markers just extend the previous run.
        /// </summary>
        public static List<SortedFrame> Sort(string shootId, IEnumerable<Frame> frames)
        {
            var ordered = frames.OrderBy(f => f.CapturedUtc).ThenBy(f => f.Id, StringComparer.Ordinal).ToList();
            var result = new List<SortedFrame>(ordered.Count);
            string? currentShot = null;
            foreach (var frame in ordered)
            {
                var sorted = new SortedFrame { Id = frame.Id, CapturedUtc = frame.CapturedUtc };
                var (passed, reason) = Check(frame.Metrics);
                sorted.Passed = passed;
                sorted.FailReason = reason;
                sorted.Score = passed ? Score(frame.Metrics) : 0;

                if (TryParseQr(frame.QrText, out var qrShoot, out var qrShot) && qrShoot == shootId)
                {
                    currentShot = qrShot;
                    sorted.ShotId = qrShot;
                    sorted.Evidence = "MarkerFrame";
                }
                else if (currentShot != null)
                {
                    sorted.ShotId = currentShot;
                    sorted.Evidence = "QrRun";
                }
                else
                {
                    sorted.Evidence = "Unsorted";
                }

                result.Add(sorted);
            }

            return result;
        }

        /// <summary>Marks one recommended frame per shot: the highest score among frames that passed, ties by earliest capture then id.</summary>
        public static void Recommend(IEnumerable<SortedFrame> frames)
        {
            var list = frames.ToList();
            foreach (var group in list.Where(f => f.Passed && f.ShotId != "").GroupBy(f => f.ShotId))
            {
                var best = group.OrderByDescending(f => f.Score).ThenBy(f => f.CapturedUtc).ThenBy(f => f.Id, StringComparer.Ordinal).First();
                best.Recommended = true;
            }
        }
    }
}
