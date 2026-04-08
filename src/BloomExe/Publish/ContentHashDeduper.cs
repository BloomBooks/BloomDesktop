using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using SIL.IO;

namespace Bloom.Publish
{
    internal sealed class ContentHashDeduper
    {
        private readonly Dictionary<string, string> _canonicalIdentifierByHashKey =
            new Dictionary<string, string>();

        internal bool TryGetCanonicalIdentifier(
            string filePath,
            string candidateIdentifier,
            out string canonicalIdentifier,
            string bucket = ""
        )
        {
            var hashKey = MakeHashKey(filePath, bucket);
            if (_canonicalIdentifierByHashKey.TryGetValue(hashKey, out canonicalIdentifier))
                return true;

            canonicalIdentifier = candidateIdentifier;
            _canonicalIdentifierByHashKey[hashKey] = candidateIdentifier;
            return false;
        }

        private static string MakeHashKey(string filePath, string bucket)
        {
            var hash = ComputeFileHash(filePath);
            return string.IsNullOrWhiteSpace(bucket) ? hash : $"{bucket}:{hash}";
        }

        private static string ComputeFileHash(string filePath)
        {
            using var stream = RobustFile.OpenRead(filePath);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
    }
}
