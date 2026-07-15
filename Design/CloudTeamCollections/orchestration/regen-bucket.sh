#!/bin/bash
# Bucket the origin/master...cloud-collections diff into the SQUASH-PLAN groups.
# Usage: bucket.sh <allfiles.txt> <outdir>   -> writes g1.txt..g9.txt + unmatched.txt
ALL="$1"; OUT="$2"
mkdir -p "$OUT"
for i in 1 2 3 4 5 6 7 8 9; do : > "$OUT/g$i.txt"; done
: > "$OUT/unmatched.txt"
while IFS= read -r f; do
  case "$f" in
    Design/CloudTeamCollections/*|Design/CloudTeamCollections.md|.github/skills/xlf-strings/*|AGENTS.md) echo "$f" >> "$OUT/g1.txt" ;;
    supabase/migrations/*|supabase/tests/*|supabase/config.toml|supabase/snippets/*|supabase/.gitignore) echo "$f" >> "$OUT/g2.txt" ;;
    supabase/functions/*) echo "$f" >> "$OUT/g3.txt" ;;
    server/*) echo "$f" >> "$OUT/g4.txt" ;;
    # Group 5: client core classes + their dedicated unit tests + csproj (AWSSDK bump)
    src/BloomExe/TeamCollection/Cloud/CloudEnvironment.cs|\
    src/BloomExe/TeamCollection/Cloud/CloudAuth*.cs|\
    src/BloomExe/TeamCollection/Cloud/*AuthProvider*.cs|\
    src/BloomExe/TeamCollection/Cloud/CloudTokenStore*.cs|\
    src/BloomExe/TeamCollection/Cloud/CloudCollectionClient.cs|\
    src/BloomExe/TeamCollection/Cloud/CloudRepoCache.cs|\
    src/BloomExe/TeamCollection/Cloud/CloudBookTransfer.cs|\
    src/BloomExe/TeamCollection/Cloud/BookVersionManifest.cs|\
    src/BloomExe/TeamCollection/Cloud/S3*.cs|\
    src/BloomExe/WebLibraryIntegration/BloomS3Client.cs|\
    src/BloomExe/WebLibraryIntegration/S3Extensions.cs|\
    src/BloomTests/WebLibraryIntegration/BloomS3ClientTests.cs|\
    src/BloomExe/BloomExe.csproj|\
    src/BloomTests/TeamCollection/Cloud/CloudEnvironmentTests.cs|\
    src/BloomTests/TeamCollection/Cloud/CloudAuthTests.cs|\
    src/BloomTests/TeamCollection/Cloud/FirebaseCloudAuthProviderTests.cs|\
    src/BloomTests/TeamCollection/Cloud/CloudTokenStoreTests.cs|\
    src/BloomTests/TeamCollection/Cloud/CloudCollectionClientTests.cs|\
    src/BloomTests/TeamCollection/Cloud/CloudRepoCacheTests.cs|\
    src/BloomTests/TeamCollection/Cloud/CloudBookTransferTests.cs|\
    src/BloomTests/TeamCollection/Cloud/BookVersionManifestTests.cs) echo "$f" >> "$OUT/g5.txt" ;;
    # Group 7: HTTP API layer + their tests (before group 6's broader TeamCollection match)
    src/BloomExe/web/controllers/SharingApi.cs|\
    src/BloomExe/web/controllers/CollectionChooserApi.cs|\
    src/BloomExe/web/controllers/CollectionApi.cs|\
    src/BloomExe/web/controllers/ExternalApi.cs|\
    src/BloomExe/web/controllers/ProblemReportApi.cs|\
    src/BloomExe/web/controllers/CollectionSettingsApi.cs|\
    src/BloomExe/web/controllers/FileIOApi.cs|\
    src/BloomExe/web/ReadersApi.cs|\
    src/BloomExe/TeamCollection/TeamCollectionApi.cs|\
    src/BloomExe/Workspace/*|\
    src/BloomTests/web/controllers/*|\
    src/BloomTests/TeamCollection/TeamCollectionApiCloudTests.cs|\
    src/BloomTests/TeamCollection/WorkspaceModelTierTimingOrderingTests.cs) echo "$f" >> "$OUT/g7.txt" ;;
    # Group 9: E2E harness
    src/BloomTests/e2e/*) echo "$f" >> "$OUT/g9.txt" ;;
    # Group 6: everything else in the TeamCollection backend + app-level automation guards
    src/BloomExe/TeamCollection/*|\
    src/BloomExe/Program.cs|\
    src/BloomExe/NonFatalProblem.cs|\
    src/BloomExe/MiscUI/BrowserProgressDialog.cs|\
    src/BloomExe/ErrorReporter/HtmlErrorReporter.cs|\
    src/BloomExe/Shell.cs|\
    src/BloomExe/web/BloomServer.cs|\
    src/BloomExe/ApplicationContainer.cs|\
    src/BloomExe/Collection/CollectionSettingsDialog.cs|\
    src/BloomExe/ExperimentalFeatures.cs|\
    src/BloomExe/History/HistoryEvent.cs|\
    src/BloomExe/SubscriptionAndFeatures/FeatureRegistry.cs|\
    run.sh|\
    scripts/*|\
    src/BloomTests/TeamCollection/*|\
    src/BloomTests/ProgramTests.cs|\
    src/BloomTests/ExperimentalFeaturesTests.cs|\
    src/BloomTests/ShellTests.cs) echo "$f" >> "$OUT/g6.txt" ;;
    # Group 8: front-end + strings
    src/BloomBrowserUI/*|DistFiles/localization/en/*) echo "$f" >> "$OUT/g8.txt" ;;
    *) echo "$f" >> "$OUT/unmatched.txt" ;;
  esac
done < "$ALL"
for i in 1 2 3 4 5 6 7 8 9; do printf "g%s: %s\n" "$i" "$(wc -l < "$OUT/g$i.txt")"; done
printf "unmatched: %s\n" "$(wc -l < "$OUT/unmatched.txt")"
cat "$OUT/unmatched.txt"
