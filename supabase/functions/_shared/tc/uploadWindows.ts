// The two time windows that keep the stale-upload sweep from ever deleting an S3 version a
// check-in commits, with no lock shared between S3 and the database (CONTRACTS.md
// "Orphaned-upload sweep"):
//
//   - checkin-finish / collection-files-finish commit a captured upload only if its S3
//     LastModified is newer than UPLOAD_COMMIT_WINDOW_MS ago (older ones are refused as
//     MissingOrBadUploads with `stalePaths`, and the client uploads them again); and
//   - sweep-stale-uploads deletes only versions older than UPLOAD_SWEEP_GRACE_MS.
//
// A finish reads LastModified and then commits within one edge-function invocation (minutes
// at most), so any version it commits is younger than UPLOAD_COMMIT_WINDOW_MS plus that run
// time, while every version the sweep would delete is older than UPLOAD_SWEEP_GRACE_MS. As
// long as the gap between the two (UPLOAD_WINDOW_SAFETY_MARGIN_MS) covers the longest finish
// run plus any clock skew between S3 and the edge functions, the two sets never meet, whatever
// the sweep's database re-check misses. tc-invariants-test.ts enforces the gap.

const HOUR_MS = 60 * 60 * 1000;

/** A finish refuses to commit an upload whose S3 LastModified is older than this. */
export const UPLOAD_COMMIT_WINDOW_MS = 24 * HOUR_MS;

/** The sweep never deletes a version younger than this. */
export const UPLOAD_SWEEP_GRACE_MS = 48 * HOUR_MS;

/** The least the sweep's grace must exceed the commit window by: far more than an edge
 * function can run (a few minutes) plus plausible clock skew. */
export const UPLOAD_WINDOW_SAFETY_MARGIN_MS = 12 * HOUR_MS;
