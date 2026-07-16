namespace Bloom.AiTranslation
{
    /// <summary>
    /// Per-engine configuration and validation state for one AI translation provider
    /// ("deepl", "google", or "alpha2"). A CollectionSettings always has exactly one of these for
    /// each known provider id; see CollectionSettings.EnsureAiTranslationEngines().
    /// </summary>
    public class AiTranslationEngineSettings
    {
        /// <summary>
        /// The provider this engine configuration is for: "deepl", "google", or "alpha2".
        /// </summary>
        public string ProviderId;

        /// <summary>
        /// Whether this engine is turned on for whole-book batch translation.
        /// </summary>
        public bool Enabled;

        /// <summary>
        /// API key used by the DeepL and Alpha2 providers.
        /// </summary>
        public string ApiKey = "";

        /// <summary>
        /// Service account email used by the Google provider.
        /// </summary>
        public string ServiceAccountEmail = "";

        /// <summary>
        /// Service account private key used by the Google provider.
        /// </summary>
        public string PrivateKey = "";

        /// <summary>
        /// The source language (Bloom/BCP-47 tag) to translate FROM. Only meaningful for the
        /// alpha2 provider, whose model selection is per source/target pair; deepl/google choose
        /// their source automatically per translation group and ignore this. A blank value means
        /// English ("en"); use GetEffectiveSourceLanguageTag() to read it with that default applied.
        /// </summary>
        public string SourceLanguageTag = "";

        /// <summary>
        /// Fingerprint (see AiTranslationService.GetEngineFingerprint) of this engine's
        /// configuration and the target language at the time it was last validated.
        /// </summary>
        public string ValidatedConfigurationFingerprint = "";

        /// <summary>
        /// Whether the last validation attempt for this engine succeeded.
        /// </summary>
        public bool LastValidationSucceeded;

        /// <summary>
        /// True if the last validation failed specifically because this engine's provider does
        /// not support the chosen target language (as opposed to a credential/network error). When
        /// set, the settings UI shows a "does not support ⟨language⟩, will be skipped" note and the
        /// engine is simply excluded from translation rather than treated as misconfigured.
        /// </summary>
        public bool LastValidationTargetLanguageNotSupported;

        /// <summary>
        /// The message (success text or error) from the last validation attempt.
        /// </summary>
        public string LastValidationMessage = "";

        /// <summary>
        /// The effective source language tag to translate from: the configured SourceLanguageTag
        /// if non-blank, otherwise English ("en"). Only the alpha2 provider consults this.
        /// </summary>
        public string GetEffectiveSourceLanguageTag()
        {
            return string.IsNullOrWhiteSpace(SourceLanguageTag) ? "en" : SourceLanguageTag.Trim();
        }

        /// <summary>
        /// Creates a deep (independent) copy of this engine's settings.
        /// </summary>
        public AiTranslationEngineSettings Clone()
        {
            return new AiTranslationEngineSettings
            {
                ProviderId = ProviderId,
                Enabled = Enabled,
                ApiKey = ApiKey,
                ServiceAccountEmail = ServiceAccountEmail,
                PrivateKey = PrivateKey,
                SourceLanguageTag = SourceLanguageTag,
                ValidatedConfigurationFingerprint = ValidatedConfigurationFingerprint,
                LastValidationSucceeded = LastValidationSucceeded,
                LastValidationTargetLanguageNotSupported = LastValidationTargetLanguageNotSupported,
                LastValidationMessage = LastValidationMessage,
            };
        }
    }
}
