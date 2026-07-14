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
        /// Fingerprint (see AiTranslationService.GetEngineFingerprint) of this engine's
        /// configuration and the target language at the time it was last validated.
        /// </summary>
        public string ValidatedConfigurationFingerprint = "";

        /// <summary>
        /// Whether the last validation attempt for this engine succeeded.
        /// </summary>
        public bool LastValidationSucceeded;

        /// <summary>
        /// The message (success text or error) from the last validation attempt.
        /// </summary>
        public string LastValidationMessage = "";

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
                ValidatedConfigurationFingerprint = ValidatedConfigurationFingerprint,
                LastValidationSucceeded = LastValidationSucceeded,
                LastValidationMessage = LastValidationMessage,
            };
        }
    }
}
