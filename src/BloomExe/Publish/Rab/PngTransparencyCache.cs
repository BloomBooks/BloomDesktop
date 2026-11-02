using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Bloom.ImageProcessing;
using Newtonsoft.Json;
using SIL.IO;
using SIL.Reporting;
using SIL.Windows.Forms.ImageToolbox;

namespace Bloom.Publish.Rab
{
    /// <summary>
    /// Remembers whether each PNG has transparent pixels, the question publishing asks when
    /// deciding whether a PNG can become a much smaller JPEG (see ImageUtils.AdjustImageForDisplay).
    /// The size estimate needs the same answer (BL-17062), but getting it means decoding the
    /// picture, about 60ms for a big one, so answers are kept in a file and only worked out again
    /// when a picture changes.
    /// </summary>
    public class PngTransparencyCache
    {
        // PNG colour types (the byte after the bit depth in the IHDR chunk).
        private const byte kPngColorTypeGrayscaleWithAlpha = 4;
        private const byte kPngColorTypeRgbWithAlpha = 6;
        private static readonly byte[] kPngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        private class Entry
        {
            public long Length;
            public long LastWriteTicks;
            public bool HasTransparency;
        }

        private readonly string _filePath;
        private readonly object _lock = new object();
        private readonly object _saveLock = new object();
        private readonly Dictionary<string, Entry> _entries;
        private bool _dirty;

        /// <summary>
        /// Make a cache kept in <paramref name="filePath"/>, loading what is already there.
        /// </summary>
        public PngTransparencyCache(string filePath)
        {
            _filePath = filePath;
            _entries = Load(filePath);
        }

        private static Dictionary<string, Entry> Load(string filePath)
        {
            var empty = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            if (!RobustFile.Exists(filePath))
                return empty;
            try
            {
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, Entry>>(
                    RobustFile.ReadAllText(filePath)
                );
                return loaded == null
                    ? empty
                    : new Dictionary<string, Entry>(loaded, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception error) when (error is JsonException || error is IOException)
            {
                // A damaged cache only costs checking the pictures again.
                return empty;
            }
        }

        /// <summary>
        /// Whether the PNG at <paramref name="path"/> has transparency, if we know for its current
        /// size and modified time; null if it hasn't been checked since it last changed.
        /// </summary>
        public bool? GetCachedAnswer(FileInfo file)
        {
            lock (_lock)
            {
                if (
                    _entries.TryGetValue(file.FullName, out var entry)
                    && entry.Length == file.Length
                    && entry.LastWriteTicks == file.LastWriteTimeUtc.Ticks
                )
                    return entry.HasTransparency;
            }
            return null;
        }

        /// <summary>
        /// Decode the PNG at <paramref name="path"/>, check it for transparency the way publishing
        /// does, and remember the answer. A picture that can't be read counts as transparent,
        /// which is the larger estimate, and is logged.
        /// </summary>
        public bool CheckAndRemember(string path)
        {
            var file = new FileInfo(path);
            bool hasTransparency;
            try
            {
                using var image = PalasoImage.FromFileRobustly(path);
                hasTransparency = ImageUtils.HasTransparency(image.Image);
            }
            catch (Exception error)
            {
                Logger.WriteError(
                    $"Could not check {path} for transparency while estimating app size",
                    error
                );
                hasTransparency = true;
            }
            lock (_lock)
            {
                _entries[file.FullName] = new Entry
                {
                    Length = file.Length,
                    LastWriteTicks = file.LastWriteTimeUtc.Ticks,
                    HasTransparency = hasTransparency,
                };
                _dirty = true;
            }
            return hasTransparency;
        }

        /// <summary>
        /// Write the cache to its file if anything has been added since it was loaded or last saved.
        /// Entries for pictures that no longer exist are dropped.
        /// </summary>
        public void Save()
        {
            // One save at a time, from taking the snapshot to finishing the write, so an older
            // snapshot can never be written over a newer one.
            lock (_saveLock)
            {
                string json;
                lock (_lock)
                {
                    if (!_dirty)
                        return;
                    foreach (
                        var gone in _entries.Keys.Where(path => !RobustFile.Exists(path)).ToList()
                    )
                        _entries.Remove(gone);
                    json = JsonConvert.SerializeObject(_entries);
                    _dirty = false;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath));
                RobustFile.WriteAllText(_filePath, json);
            }
        }

        /// <summary>
        /// Whether the PNG at <paramref name="path"/> could have transparent pixels, judged from
        /// its header alone, which is cheap even for a huge picture. A PNG can only be transparent
        /// if its colour type includes an alpha channel or it has a tRNS chunk (which must come
        /// before the image data). False means "certainly opaque", so there is no need to decode
        /// it; true means only "might be". Anything we can't make sense of counts as "might be".
        /// </summary>
        internal static bool PngCanHaveTransparency(string path)
        {
            try
            {
                using var stream = RobustFile.OpenRead(path);
                using var reader = new BinaryReader(stream);
                if (!reader.ReadBytes(kPngSignature.Length).SequenceEqual(kPngSignature))
                    return true;
                var sawHeader = false;
                // Each chunk is a 4-byte big-endian length, a 4-byte type, the data, and a 4-byte CRC.
                while (stream.Position + 8 <= stream.Length)
                {
                    var lengthBytes = reader.ReadBytes(4);
                    var length =
                        (lengthBytes[0] << 24)
                        | (lengthBytes[1] << 16)
                        | (lengthBytes[2] << 8)
                        | lengthBytes[3];
                    var type = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    if (length < 0)
                        return true;
                    switch (type)
                    {
                        case "IHDR":
                            if (length < 10)
                                return true;
                            reader.ReadBytes(9); // width, height, bit depth
                            var colorType = reader.ReadByte();
                            if (
                                colorType == kPngColorTypeGrayscaleWithAlpha
                                || colorType == kPngColorTypeRgbWithAlpha
                            )
                                return true;
                            sawHeader = true;
                            stream.Seek(length - 10 + 4, SeekOrigin.Current);
                            break;
                        case "tRNS":
                            return true;
                        case "IDAT":
                        case "IEND":
                            // tRNS can't come after the image data, so we have seen all we need.
                            return !sawHeader;
                        default:
                            stream.Seek((long)length + 4, SeekOrigin.Current);
                            break;
                    }
                }
                return true;
            }
            catch (Exception error)
                when (error is IOException || error is UnauthorizedAccessException)
            {
                return true;
            }
        }
    }
}
