using System;
using System.Linq;
using BlazorApp.Shared.PhotoShoot;
using FluentAssertions;
using Xunit;

namespace UnitTest.PhotoShoot
{
    public class PhotoShootUploadSortTests
    {
        private static PhotoShootUploadSort.FrameMetrics Good(double sharp = 0.5) =>
            new() { Sharpness = sharp, MeanLuma = 0.5, ClippedHighlights = 0.0, ClippedShadows = 0.0 };

        private static PhotoShootUploadSort.Frame Frame(string id, int minute, string? qr = null, PhotoShootUploadSort.FrameMetrics? metrics = null) =>
            new() { Id = id, CapturedUtc = new DateTime(2026, 11, 6, 10, minute, 0, DateTimeKind.Utc), QrText = qr, Metrics = metrics ?? Good() };

        [Fact]
        public void QrPayload_round_trips()
        {
            var text = PhotoShootUploadSort.QrPayload("f77cc235728d", "e75ee391f0f7");
            text.Should().Be("lhp:shoot/f77cc235728d/shot/e75ee391f0f7");
            PhotoShootUploadSort.TryParseQr(text, out var shoot, out var shot).Should().BeTrue();
            shoot.Should().Be("f77cc235728d");
            shot.Should().Be("e75ee391f0f7");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("https://example.com/x")]
        [InlineData("lhp:shoot//shot/abc")]
        [InlineData("lhp:shoot/abc/shot/abc/extra")]
        [InlineData(" lhp:shoot/abc/shot/abc")]
        public void Parse_rejects_anything_that_is_not_a_marker(string? text)
        {
            PhotoShootUploadSort.TryParseQr(text, out _, out _).Should().BeFalse();
        }

        [Fact]
        public void Marker_starts_a_run_that_covers_following_frames_until_the_next_marker()
        {
            const string shoot = "shoot1";
            var frames = new[]
            {
                Frame("a", 0),
                Frame("m1", 1, PhotoShootUploadSort.QrPayload(shoot, "shotA")),
                Frame("b", 2),
                Frame("c", 3),
                Frame("m2", 4, PhotoShootUploadSort.QrPayload(shoot, "shotB")),
                Frame("d", 5),
            };

            var sorted = PhotoShootUploadSort.Sort(shoot, frames).ToDictionary(f => f.Id);

            sorted["a"].Evidence.Should().Be("Unsorted");
            sorted["m1"].ShotId.Should().Be("shotA");
            sorted["m1"].Evidence.Should().Be("MarkerFrame");
            sorted["b"].ShotId.Should().Be("shotA");
            sorted["b"].Evidence.Should().Be("QrRun");
            sorted["c"].ShotId.Should().Be("shotA");
            sorted["m2"].ShotId.Should().Be("shotB");
            sorted["d"].ShotId.Should().Be("shotB");
        }

        [Fact]
        public void Frames_are_ordered_by_capture_time_not_input_order()
        {
            const string shoot = "shoot1";
            var frames = new[]
            {
                Frame("late", 9),
                Frame("m", 1, PhotoShootUploadSort.QrPayload(shoot, "shotA")),
                Frame("early", 0),
            };

            var sorted = PhotoShootUploadSort.Sort(shoot, frames);

            sorted.Select(f => f.Id).Should().Equal("early", "m", "late");
            sorted.Single(f => f.Id == "early").Evidence.Should().Be("Unsorted");
            sorted.Single(f => f.Id == "late").ShotId.Should().Be("shotA");
        }

        [Fact]
        public void Marker_from_another_shoot_is_ignored()
        {
            var frames = new[]
            {
                Frame("other", 0, PhotoShootUploadSort.QrPayload("someOtherShoot", "shotX")),
                Frame("after", 1),
            };

            var sorted = PhotoShootUploadSort.Sort("shoot1", frames);

            sorted.Should().OnlyContain(f => f.Evidence == "Unsorted");
        }

        [Fact]
        public void Blurry_frame_fails_with_a_reason_and_no_score()
        {
            var (passed, reason) = PhotoShootUploadSort.Check(Good(sharp: 0.01));

            passed.Should().BeFalse();
            reason.Should().Be("Too blurry");
            PhotoShootUploadSort.Score(Good(sharp: 0.9)).Should().BeGreaterThan(0.5);
        }

        [Fact]
        public void Crushed_shadows_fail_but_white_backdrop_highlights_do_not()
        {
            PhotoShootUploadSort.Check(new PhotoShootUploadSort.FrameMetrics { Sharpness = 0.5, MeanLuma = 0.5, ClippedShadows = 0.3 })
                .Should().Be((false, "Crushed shadows"));
            // White seamless backdrops clip by design, so a bright frame still passes.
            PhotoShootUploadSort.Check(new PhotoShootUploadSort.FrameMetrics { Sharpness = 0.5, MeanLuma = 0.9, ClippedHighlights = 0.65 })
                .Should().Be((true, ""));
        }

        [Fact]
        public void Recommend_picks_the_sharpest_passing_frame_per_shot()
        {
            const string shoot = "shoot1";
            var frames = new[]
            {
                Frame("m", 0, PhotoShootUploadSort.QrPayload(shoot, "shotA"), Good(0.4)),
                Frame("sharp", 1, null, Good(0.9)),
                Frame("soft", 2, null, Good(0.3)),
                Frame("blurred", 3, null, Good(0.01)),
                Frame("m2", 4, PhotoShootUploadSort.QrPayload(shoot, "shotB"), Good(0.2)),
            };

            var sorted = PhotoShootUploadSort.Sort(shoot, frames).ToList();
            PhotoShootUploadSort.Recommend(sorted);

            sorted.Where(f => f.Recommended).Select(f => f.Id).Should().BeEquivalentTo(new[] { "sharp", "m2" });
            sorted.Single(f => f.Id == "blurred").Recommended.Should().BeFalse();
        }

        [Fact]
        public void Recommend_breaks_ties_by_earliest_capture()
        {
            const string shoot = "shoot1";
            var frames = new[]
            {
                Frame("m", 0, PhotoShootUploadSort.QrPayload(shoot, "shotA"), Good(0.5)),
                Frame("second", 2, null, Good(0.5)),
                Frame("first", 1, null, Good(0.5)),
            };

            var sorted = PhotoShootUploadSort.Sort(shoot, frames).ToList();
            PhotoShootUploadSort.Recommend(sorted);

            sorted.Single(f => f.Recommended).Id.Should().Be("m");
        }
    }
}
