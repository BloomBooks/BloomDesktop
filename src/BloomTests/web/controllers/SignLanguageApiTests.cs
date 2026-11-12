using Bloom.web.controllers;
using NUnit.Framework;

namespace BloomTests.web.controllers
{
    /// <summary>
    /// Tests for how SignLanguageApi decides whether an imported video must be re-encoded
    /// (SignLanguageApi.PlanVideoReencode). The inputs are what ffmpeg prints about a video
    /// (`ffmpeg -hide_banner -i video.mp4`), taken from real videos.
    /// </summary>
    [TestFixture]
    public class SignLanguageApiTests
    {
        private const string VideoPath = @"C:\book\video\imported.mp4";
        private const string TempPath = @"C:\book\video\imported.mp4.tmp.mp4";

        /// <summary>
        /// ffmpeg's description of a video from the Rwandan Sign Language book "Umubiri Wanjye"
        /// (BL-17087): HEVC, 1280x720, 50 fps, no sound.
        /// </summary>
        private const string LargeHevcDescription =
            @"Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'C:/video/d8e8e619-5fe0-4cc2-905d-1ca5b8e3d48d.mp4':
  Metadata:
    major_brand     : mp42
    minor_version   : 1
    compatible_brands: isommp41mp42
    creation_time   : 2026-09-21T15:02:01.000000Z
  Duration: 00:00:03.94, start: 0.000000, bitrate: 6406 kb/s
  Stream #0:0[0x1](und): Video: hevc (Main) (hvc1 / 0x31637668), yuv420p(tv, bt709, progressive), 1280x720 [SAR 1:1 DAR 16:9], 6296 kb/s, 50 fps, 50 tbr, 50k tbn (default)
    Metadata:
      creation_time   : 2026-09-21T15:02:01.000000Z
      handler_name    : Core Media Video
";

        /// <summary>
        /// ffmpeg's description of the same video scaled down to 1024x576 and kept as HEVC: small
        /// enough that its size alone does not call for re-encoding.
        /// </summary>
        private const string SmallHevcDescription =
            @"Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'rsl-small-hevc.mp4':
  Metadata:
    major_brand     : isom
    minor_version   : 512
    compatible_brands: isomiso2mp41
    encoder         : Lavf57.56.100
  Duration: 00:00:03.94, start: 0.000000, bitrate: 159 kb/s
  Stream #0:0[0x1](und): Video: hevc (Main) (hev1 / 0x31766568), yuv420p(tv, progressive), 1024x576 [SAR 1:1 DAR 16:9], 150 kb/s, 50 fps, 50 tbr, 12800 tbn (default)
    Metadata:
      handler_name    : VideoHandler
";

        /// <summary>
        /// ffmpeg's description of an H.264 video from the same book: 1024x576, 50 fps, no sound.
        /// </summary>
        private const string SmallH264Description =
            @"Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'C:/video/ac4cd898-45d3-43de-adfb-1b8f58ad8d5f.mp4':
  Metadata:
    major_brand     : isom
    minor_version   : 512
    compatible_brands: isomiso2avc1mp41
    encoder         : Lavf62.3.100
  Duration: 00:00:04.84, start: 0.000000, bitrate: 637 kb/s
  Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p(tv, bt709, progressive), 1024x576 [SAR 1:1 DAR 16:9], 630 kb/s, 50 fps, 50 tbr, 12800 tbn (default)
    Metadata:
      handler_name    : Core Media Video
      encoder         : Lavc62.11.100 libx264
";

        /// <summary>
        /// ffmpeg's description of the small HEVC video above re-encoded as MPEG-4 Part 2, the format
        /// many older cameras and phones record: 1024x576, 50 fps, no sound.
        /// </summary>
        private const string SmallMpeg4Description =
            @"Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'rsl-small-mpeg4.mp4':
  Metadata:
    major_brand     : isom
    minor_version   : 512
    compatible_brands: isomiso2mp41
    encoder         : Lavf57.56.100
  Duration: 00:00:03.94, start: 0.000000, bitrate: 591 kb/s
  Stream #0:0[0x1](und): Video: mpeg4 (Simple Profile) (mp4v / 0x7634706D), yuv420p, 1024x576 [SAR 1:1 DAR 16:9], 587 kb/s, 50 fps, 50 tbr, 12800 tbn (default)
    Metadata:
      handler_name    : VideoHandler
";

        /// <summary>The line ffmpeg prints for an AAC sound track.</summary>
        private const string AudioStreamLine =
            "  Stream #0:1[0x2](und): Audio: aac (LC) (mp4a / 0x6134706D), 44100 Hz, mono, fltp, 69 kb/s (default)\n";

        [Test]
        public void PlanVideoReencode_SmallH264Video_IsLeftAsItIs()
        {
            var plan = SignLanguageApi.PlanVideoReencode(
                SmallH264Description,
                VideoPath,
                TempPath,
                true
            );

            Assert.That(
                plan,
                Is.Null,
                "a small, silent H.264 video at 50 fps needs no re-encoding"
            );
        }

        [Test]
        public void PlanVideoReencode_SmallHevcVideo_IsReencodedToH264AndMustReplaceTheOriginal()
        {
            var plan = SignLanguageApi.PlanVideoReencode(
                SmallHevcDescription,
                VideoPath,
                TempPath,
                true
            );

            Assert.That(
                plan,
                Is.Not.Null,
                "an HEVC video must be re-encoded even when it is small"
            );
            Assert.That(plan.MustChangeCodec, Is.True);
            Assert.That(plan.Parameters, Does.Contain("-c:v libx264"));
            Assert.That(plan.Parameters, Does.Contain($"-i \"{VideoPath}\""));
            Assert.That(plan.Parameters, Does.EndWith($"\"{TempPath}\""));
            // Not resized: only kept to even dimensions, as H.264 requires.
            Assert.That(plan.Parameters, Does.Contain("-vf \"scale=-2:trunc(ih/2)*2\""));
            Assert.That(plan.Parameters, Does.Not.Contain("fps=fps="));
            Assert.That(plan.HasAudio, Is.False);
            Assert.That(plan.Duration.TotalSeconds, Is.EqualTo(3.94).Within(0.001));
        }

        [Test]
        public void PlanVideoReencode_SmallMpeg4Video_IsReencodedToH264AndMustReplaceTheOriginal()
        {
            var plan = SignLanguageApi.PlanVideoReencode(
                SmallMpeg4Description,
                VideoPath,
                TempPath,
                true
            );

            Assert.That(
                plan,
                Is.Not.Null,
                "an MPEG-4 Part 2 video must be re-encoded even when it is small"
            );
            Assert.That(plan.MustChangeCodec, Is.True);
            Assert.That(plan.Parameters, Does.Contain("-c:v libx264"));
            Assert.That(plan.Parameters, Does.Contain("-vf \"scale=-2:trunc(ih/2)*2\""));
        }

        [Test]
        public void PlanVideoReencode_SmallAv1Video_IsLeftAsItIs()
        {
            // AV1 plays in Chromium; our reduced ffmpeg can't decode it, so trying would only fail.
            var description = SmallH264Description.Replace(
                "Video: h264 (High) (avc1 / 0x31637661)",
                "Video: av1 (Main) (av01 / 0x31307661)"
            );
            Assert.That(
                description,
                Does.Contain("Video: av1 "),
                "test setup: the description should now be of an AV1 video"
            );

            var plan = SignLanguageApi.PlanVideoReencode(description, VideoPath, TempPath, true);

            Assert.That(plan, Is.Null);
        }

        [Test]
        public void PlanVideoReencode_LargeHevcVideo_IsScaledDownAndMustReplaceTheOriginal()
        {
            var plan = SignLanguageApi.PlanVideoReencode(
                LargeHevcDescription,
                VideoPath,
                TempPath,
                true
            );

            Assert.That(plan, Is.Not.Null);
            Assert.That(plan.MustChangeCodec, Is.True);
            Assert.That(plan.Parameters, Does.Contain("-c:v libx264"));
            Assert.That(plan.Parameters, Does.Contain("-vf \"scale=-2:trunc(576/2)*2\""));
        }

        [Test]
        public void PlanVideoReencode_SmallHevcVideoWithSoundInSignLanguageBook_IsReencodedWithoutSound()
        {
            var description = SmallHevcDescription + AudioStreamLine;
            Assert.That(
                description,
                Does.Contain(": Audio: "),
                "test setup: the description should include a sound track"
            );

            var plan = SignLanguageApi.PlanVideoReencode(description, VideoPath, TempPath, true);

            Assert.That(plan, Is.Not.Null);
            Assert.That(plan.HasAudio, Is.True);
            Assert.That(plan.MustChangeCodec, Is.True);
            Assert.That(plan.Parameters, Does.Contain("-c:v libx264"));
            Assert.That(plan.Parameters, Does.Contain(" -an "));
        }

        [Test]
        public void PlanVideoReencode_LargeH264Video_IsScaledDownButMayKeepTheOriginal()
        {
            var description = SmallH264Description.Replace("1024x576", "1920x1080");
            Assert.That(
                description,
                Does.Contain("1920x1080"),
                "test setup: the description should now be of a 1080p video"
            );

            var plan = SignLanguageApi.PlanVideoReencode(description, VideoPath, TempPath, true);

            Assert.That(plan, Is.Not.Null);
            Assert.That(
                plan.MustChangeCodec,
                Is.False,
                "an H.264 video can keep its original if re-encoding makes it larger"
            );
            Assert.That(plan.Parameters, Does.Contain("-vf \"scale=-2:trunc(576/2)*2\""));
        }
    }
}
