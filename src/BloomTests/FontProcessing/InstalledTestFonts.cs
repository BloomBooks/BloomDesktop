using Bloom.FontProcessing;

namespace BloomTests.FontProcessing
{
    /// <summary>
    /// Finds a real font file on this computer for the tests that need font metadata that Bloom
    /// can actually read. A test that finds nothing here reports itself as ignored.
    /// </summary>
    public static class InstalledTestFonts
    {
        // Fonts whose license allows embedding and which a Bloom developer or build agent is
        // likely to have. The first one that is installed is used.
        private static readonly string[] kCandidatesWithGoodLicense =
        {
            "Andika",
            "Charis SIL",
            "Charis",
            "Gentium Plus",
            "Gentium Book Plus",
            "Doulos SIL",
        };

        /// <summary>
        /// The files of an installed font family whose license allows embedding.
        /// </summary>
        /// <param name="family">the family name, or null if none was found</param>
        /// <returns>the group of files, or null if none was found</returns>
        public static FontGroup FindFamilyWithGoodLicense(out string family)
        {
            foreach (var candidate in kCandidatesWithGoodLicense)
            {
                var group = FontFileFinder
                    .GetInstance(isReuseAllowed: false)
                    .GetGroupForFont(candidate);
                if (group?.Normal == null)
                    continue;
                // Bloom serves some of these families itself, as .woff2 files. Those are not
                // files Bloom stores for a collection, and the tests need one that is.
                if (!EmbeddedFonts.IsFontFileWeStore(group.Normal))
                    continue;
                if (new FontMetadata(candidate, group).determinedSuitability != FontMetadata.kOK)
                    continue;
                family = candidate;
                return group;
            }
            family = null;
            return null;
        }
    }
}
