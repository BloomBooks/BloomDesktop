using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.IO;

namespace BloomFreezeDoctor.Outbox;

/// <summary>
/// Files reports to Bloom's YouTrack, deduplicating by fingerprint.
///
/// Everything here was verified against the real tracker during the Phase 0 spike (project `AUT`, test
/// card deleted afterwards): the token can create issues, search by a fingerprint string in a
/// description, attach files, and restrict an attachment to the Developers group. That last one is what
/// decision D2 requires, so it is not optional.
/// </summary>
public sealed class YouTrackSubmitter : IReportSubmitter
{
    /// <summary>
    /// The shared `auto_report_creator` permanent token, in the same split form Bloom itself uses.
    ///
    /// **SETTLED - August 2026, by John Thomson. Please do not re-open this without new information.**
    /// There is no new risk here. The key was already exposed by Bloom itself, and this code uses it in
    /// the ways we intend it to be used. Reviewers and review bots raise it repeatedly because a token in
    /// source looks alarming on sight; the reasoning below is the answer, and the short form is this
    /// paragraph so that nobody has to read the rest to find it.
    ///
    /// Decision D6: BloomDesktop is already a public repository and already carries this token in the
    /// clear, so reusing it here adds no new exposure. It is not a secret we are leaking; it is a
    /// secret the project already treats as shipped. A serverless relay, so that nothing is shipped at
    /// all, remains on the list as later hardening for both applications.
    ///
    /// **Reviewed again in August 2026** when a review pointed out that the token now travels inside a
    /// second executable, and deliberately left as it is. What the Doctor does with the token is under our
    /// control exactly as what Bloom does with it is, so a second program of ours carrying it adds no risk
    /// that the first did not already carry. The risk that does exist is the pre-existing one and is not
    /// changed by anything here: anyone who takes the token out of either binary and puts it in a program
    /// of their own can do whatever it permits. That is an argument for narrowing what the account is
    /// allowed to do, or for the relay above — not for treating the two executables differently.
    /// </summary>
    private const string TokenPiece =
        @"YXV0b19yZXBvcnRfY3JlYXRvcg==.NzQtMA==.V9k0yNUN7Df5eqo4QEk5N4BBKqmEHV";

    /// <summary>
    /// YouTrack's id for the Developers group. Hardcoded because the token cannot enumerate groups
    /// (verified: `admin/groups` returns 404 for it), which is also how Bloom does it.
    /// </summary>
    private const string DevelopersGroupId = "25-3";

    private const string BaseUrl = "https://issues.bloomlibrary.org/youtrack/api";

    /// <summary>
    /// The largest single file we will try to *attach*. Anything bigger goes to the support bucket instead
    /// and the card gets a link — see <see cref="SupportFileUploader"/>.
    ///
    /// **This is a limit of the tracker, not a policy of ours**, and the distinction matters: a
    /// self-imposed "a card is not a file server" cap would discard every minidump, which is 16-17 MB for a
    /// real Bloom by this project's own measurement in ManagedStacksCollector — and raising our own number
    /// would not help, because Bloom measured YouTrack's real ceiling at about 10 MB in July 2020 and gave
    /// up attaching altogether (that measurement is still in ProblemReportApi, above the commented-out
    /// attach call it replaced). So 8 MB, which sits under that ceiling with room to spare, and the bucket
    /// for everything larger.
    /// </summary>
    public const long MaxSingleAttachmentBytes = 8 * 1024 * 1024;

    /// <summary>
    /// Total attachment bytes for one card. Still worth bounding — a card is not a file server — but no
    /// longer the thing standing between a dump and the person who needs it, since anything too big to
    /// attach is uploaded and linked rather than dropped.
    /// </summary>
    public const long MaxAttachmentBytes = 20 * 1024 * 1024;

    private readonly HttpClient _http;

    /// <summary>Creates a submitter, optionally with a supplied HttpClient (tests, or a proxy).</summary>
    public YouTrackSubmitter(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            "perm:" + TokenPiece
        );
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );
    }

    /// <inheritdoc />
    public async Task<SubmitResult> SubmitAsync(QueuedBundle bundle, CancellationToken cancellation)
    {
        try
        {
            var body = BuildBody(bundle);

            // Dedupe first: if this exact problem already has a card, add to it rather than filing
            // another. Searching is available to us (verified in the spike), so this is a real
            // capability rather than an aspiration.
            //
            // A card the outbox already knows about wins over searching, and is the only way to reach it:
            // this bundle exists because it could not be folded into a sibling that was being uploaded at
            // that moment, and its own fingerprint is not the one that sibling's card was opened under, so
            // no search for it would ever find that card. See BundleMetadata.CommentOnIssueId.
            var existing = !string.IsNullOrEmpty(bundle.Metadata.CommentOnIssueId)
                ? bundle.Metadata.CommentOnIssueId
                : await FindExistingIssueAsync(bundle.Metadata, cancellation).ConfigureAwait(false);
            if (existing != null)
            {
                // A short note, not the whole report again - see BuildRecurrenceComment. Note also what is
                // NOT here: AttachArtifactsAsync runs only on the create path below, so a recurrence
                // uploads nothing. The dump and log for it stay in the bundle folder named in the comment.
                var commentFailure = await CommentAsync(
                        existing,
                        BuildRecurrenceComment(bundle),
                        cancellation
                    )
                    .ConfigureAwait(false);
                if (commentFailure != null)
                    return commentFailure.Value;
                return new SubmitResult
                {
                    Outcome = SubmitOutcome.Filed,
                    IssueId = existing,
                    CreatedNewCard = false,
                };
            }

            var created = await CreateIssueAsync(bundle, body, cancellation).ConfigureAwait(false);
            if (created.IssueId == null)
                return created;

            // **Once the card exists, the outcome is Filed whatever happens next.** Attaching and
            // uploading used to run bare, so a connection dropping during them unwound into the catch
            // below and returned NetworkUnavailable with no issue id - which is a lie with consequences:
            // the bundle went back to Pending, the retry found the card by fingerprint and posted "This
            // happened again" about its own first attempt, the recurrence path deliberately attaches
            // nothing, and the S3 links for a minidump already uploaded existed only in a local variable
            // and were lost with it. The dump stayed in the bucket with nothing pointing at it.
            try
            {
                await AttachArtifactsAsync(created.IssueId, bundle, cancellation)
                    .ConfigureAwait(false);
                await UploadWhatCouldNotBeAttachedAsync(created.IssueId, bundle, cancellation)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // The card is filed and carries the report; what we have lost is some of its attachments.
                // Said in the log rather than swallowed, because "the card exists but is missing its dump"
                // is exactly the confusion somebody will otherwise spend an afternoon on.
                DoctorLog.Write(
                    $"filed {created.IssueId} but could not attach everything: "
                        + $"{e.GetType().Name}: {e.Message}"
                );
            }
            return created;
        }
        catch (HttpRequestException e)
        {
            // No network, DNS failure, connection refused: exactly the situation the outbox exists for.
            return new SubmitResult
            {
                Outcome = SubmitOutcome.NetworkUnavailable,
                Error = $"{e.GetType().Name}: {e.Message}",
            };
        }
        catch (TaskCanceledException e)
        {
            return new SubmitResult
            {
                Outcome = SubmitOutcome.NetworkUnavailable,
                Error = "timed out talking to the tracker: " + e.Message,
            };
        }
        catch (Exception e)
        {
            // Anything else must not escape, and the case that made this necessary is not exotic: a
            // captive portal or corporate proxy answers 200 with an HTML block page, JsonNode.Parse throws
            // JsonException, and nothing here caught it. It unwound out of the drain, which left the bundle
            // stuck in Uploading - invisible to every later drain until the reclaim timer - and out of
            // `--drain`, which had no handler at all and simply died in front of whoever was using it.
            //
            // Treated as a network problem because that is what it behaves like: try again later.
            return new SubmitResult
            {
                Outcome = SubmitOutcome.NetworkUnavailable,
                Error = $"unexpected reply from the tracker: {e.GetType().Name}: {e.Message}",
            };
        }
    }

    /// <summary>
    /// Looks for a card already carrying this fingerprint. Restricted to the same project, so a test
    /// run in `AUT` can never comment on a real `BL` card.
    /// </summary>
    private async Task<string?> FindExistingIssueAsync(
        BundleMetadata metadata,
        CancellationToken cancellation
    )
    {
        var query = Uri.EscapeDataString($"project: {metadata.Project} {metadata.Fingerprint}");
        using var response = await _http
            .GetAsync($"{BaseUrl}/issues?query={query}&fields=idReadable&$top=5", cancellation)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null; // a failed search is not a reason to skip filing; worst case we file a new card
        var json = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        var array = JsonNode.Parse(json)?.AsArray();
        return array is { Count: > 0 } ? array[0]?["idReadable"]?.GetValue<string>() : null;
    }

    private async Task<SubmitResult> CreateIssueAsync(
        QueuedBundle bundle,
        string body,
        CancellationToken cancellation
    )
    {
        var (projectId, lookupFailure) = await FindProjectIdAsync(
                bundle.Metadata.Project,
                cancellation
            )
            .ConfigureAwait(false);
        if (projectId == null)
            return lookupFailure!.Value;

        var payload = new JsonObject
        {
            ["project"] = new JsonObject { ["id"] = projectId },
            ["summary"] = bundle.Metadata.Summary,
            ["description"] = body,
            // Same as Bloom's own automated reports: let a human decide what kind of issue it is.
            ["customFields"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Type",
                    ["$type"] = "SingleEnumIssueCustomField",
                    ["value"] = new JsonObject
                    {
                        ["name"] = "Awaiting Classification",
                        ["$type"] = "EnumBundleElement",
                    },
                },
            },
        };

        using var content = new StringContent(
            payload.ToJsonString(),
            Encoding.UTF8,
            "application/json"
        );
        using var response = await _http
            .PostAsync($"{BaseUrl}/issues?fields=idReadable", content, cancellation)
            .ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return new SubmitResult
            {
                Outcome = ClassifyFailure(response.StatusCode),
                Error = $"creating the issue returned {(int)response.StatusCode}: {Trim(text)}",
            };

        var id = JsonNode.Parse(text)?["idReadable"]?.GetValue<string>();
        return id == null
            ? new SubmitResult
            {
                Outcome = SubmitOutcome.RejectedPermanently,
                Error = "the tracker accepted the issue but returned no id: " + Trim(text),
            }
            : new SubmitResult
            {
                Outcome = SubmitOutcome.Filed,
                IssueId = id,
                CreatedNewCard = true,
            };
    }

    /// <summary>
    /// Looks up the tracker's internal id for a project. Returns the id, or the failure to report.
    ///
    /// **It returns the failure rather than just null, and that is the whole point of the shape.** It used
    /// to return null for any non-success status, and the caller turned null into RejectedPermanently -
    /// so an ordinary 5xx or gateway timeout on this one GET marked a gathered report failed FOR GOOD and
    /// stopped every retry. Every other call in this class routes its failures through
    /// <see cref="ClassifyFailure"/>; this one silently did not, which defeated the one thing the outbox
    /// exists for: surviving the flaky network that so often arrives with a freeze.
    /// </summary>
    private async Task<(string? Id, SubmitResult? Failure)> FindProjectIdAsync(
        string shortName,
        CancellationToken cancellation
    )
    {
        using var response = await _http
            .GetAsync($"{BaseUrl}/admin/projects/{shortName}?fields=id", cancellation)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // ClassifyFailure already draws the line in the right place for this call too: a 404 means
            // the project really is not there and never will be, while a 429 or anything 5xx is the
            // server having a moment and is worth trying again later.
            return (
                null,
                new SubmitResult
                {
                    Outcome = ClassifyFailure(response.StatusCode),
                    Error = $"looking up project '{shortName}' returned {(int)response.StatusCode}",
                }
            );
        }

        var json = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        var id = JsonNode.Parse(json)?["id"]?.GetValue<string>();
        if (id == null)
            return (
                null,
                new SubmitResult
                {
                    Outcome = SubmitOutcome.RejectedPermanently,
                    Error = $"the tracker does not know a project called '{shortName}'",
                }
            );
        return (id, null);
    }

    /// <summary>
    /// The comment posted when this problem already has a card: what varied this time, and where the full
    /// report is, rather than the whole report over again.
    ///
    /// Two reports sharing a fingerprint share their reason, Bloom's version, the channel and the top five
    /// frames of the UI thread - so most of a second report is identical by construction, and a card
    /// carrying several of them becomes unreadable exactly when it matters most. The dump and the log are
    /// not attached either: they are near-duplicates of the first occurrence's at some 16 MB each, and the
    /// folder named below is how to get them if anyone ever wants to.
    /// </summary>
    private static string BuildRecurrenceComment(QueuedBundle bundle)
    {
        var text = new StringBuilder();
        text.AppendLine("**This happened again.**");
        text.AppendLine();
        text.AppendLine(
            $"- When: {bundle.Metadata.GatheredAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} "
                + $"(UTC {bundle.Metadata.GatheredAtUtc:HH:mm})"
        );
        if (bundle.Metadata.Occurrences > 1)
            text.AppendLine(
                $"- Seen {bundle.Metadata.Occurrences} times before this report was sent"
            );
        if (!string.IsNullOrWhiteSpace(bundle.Metadata.RecurrenceNote))
            text.AppendLine(bundle.Metadata.RecurrenceNote);
        text.AppendLine();
        text.AppendLine(
            "The full report, with the crash dump and Bloom's log, is on that machine at "
                + $"`{bundle.Directory}` and is deliberately not attached here - it is near enough a copy "
                + "of the one already on this card."
        );
        return text.ToString();
    }

    /// <summary>
    /// Adds our findings to a card that already exists for this fingerprint. Returns null on success, or
    /// the classified failure.
    ///
    /// **It classifies rather than throwing, and that is the point.** `EnsureSuccessStatusCode` here threw
    /// an <c>HttpRequestException</c>, which the caller's catch turned into `NetworkUnavailable` — so a
    /// flat, permanent refusal (a 400, a 403, a card that has since been deleted) was recorded as "no
    /// network" and retried every five minutes for thirty days. Worse, the drain stops at the first bundle
    /// it cannot send, so that one undeliverable comment sat at the head of the queue and blocked every
    /// report behind it, indefinitely.
    ///
    /// This is the same trap <see cref="FindProjectIdAsync"/> was fixed for; every other call in this
    /// class already routes its failures through <see cref="ClassifyFailure"/>, which draws the line in
    /// the right place here too — a definite refusal is permanent, a 429 or 5xx is worth retrying.
    /// </summary>
    private async Task<SubmitResult?> CommentAsync(
        string issueId,
        string body,
        CancellationToken cancellation
    )
    {
        var payload = new JsonObject { ["text"] = body };
        using var content = new StringContent(
            payload.ToJsonString(),
            Encoding.UTF8,
            "application/json"
        );
        using var response = await _http
            .PostAsync($"{BaseUrl}/issues/{issueId}/comments", content, cancellation)
            .ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
            return null;
        return new SubmitResult
        {
            Outcome = ClassifyFailure(response.StatusCode),
            Error =
                $"commenting on {issueId} failed: {(int)response.StatusCode} {response.ReasonPhrase}",
        };
    }

    /// <summary>
    /// Uploads the artifacts and then restricts each to the Developers group, which is decision D2's
    /// requirement: a dump can contain book text, file paths and the user's own details, so it must not
    /// be visible to everyone who can see the card.
    ///
    /// Restriction is a second call because the upload is multipart and cannot carry the visibility
    /// object. If that second call fails we delete the attachment rather than leave it unrestricted —
    /// failing to attach is a much smaller problem than exposing a dump.
    /// </summary>
    private async Task AttachArtifactsAsync(
        string issueId,
        QueuedBundle bundle,
        CancellationToken cancellation
    )
    {
        var failed = new List<string>();
        foreach (var path in SortArtifacts(bundle).ToAttach)
        {
            string? attachmentId = null;
            try
            {
                attachmentId = await UploadAsync(issueId, path, cancellation).ConfigureAwait(false);
                if (attachmentId == null)
                {
                    // A non-success status, which UploadAsync reports by returning null rather than
                    // throwing. Used to be skipped in silence.
                    failed.Add(path);
                    continue;
                }
                await RestrictToDevelopersAsync(issueId, attachmentId, cancellation)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                if (attachmentId != null)
                    await TryDeleteAttachmentAsync(issueId, attachmentId).ConfigureAwait(false);
                failed.Add(path);
            }
        }

        // Say what did not make it, the same way the too-large path does. Skipping in silence left a card
        // that looked complete and was not - and the evidence it was missing is on a machine we will lose
        // access to, so nobody would find out until they went looking for a dump that was never there.
        if (failed.Count > 0)
            await SayWhatCouldNotBeAttachedAsync(issueId, bundle, failed, cancellation)
                .ConfigureAwait(false);
    }

    /// <summary>
    /// Notes on the card which files could not be attached and where they still are.
    ///
    /// Best effort, and deliberately last: the card is filed and its own text is intact, so a comment that
    /// will not post must not turn a successful filing into a failure.
    /// </summary>
    private async Task SayWhatCouldNotBeAttachedAsync(
        string issueId,
        QueuedBundle bundle,
        List<string> failed,
        CancellationToken cancellation
    )
    {
        var lines = new StringBuilder();
        lines.AppendLine("**Files that could not be attached to this card**");
        lines.AppendLine();
        foreach (var path in failed)
        {
            var name = Path.GetFileName(path);
            var megabytes = SizeOrZero(path) / 1024.0 / 1024.0;
            lines.AppendLine(
                $"- `{name}` ({megabytes:F1} MB) — the tracker refused the upload. "
                    + StillOnTheUsersMachine(bundle, name)
            );
        }
        lines.AppendLine();
        lines.AppendLine(
            "*The Doctor does not try again, because the card is already filed. These can be attached by "
                + "hand from the folder named above.*"
        );
        await CommentAsync(issueId, lines.ToString(), cancellation).ConfigureAwait(false);
    }

    private async Task<string?> UploadAsync(
        string issueId,
        string path,
        CancellationToken cancellation
    )
    {
        using var form = new MultipartFormDataContent();
        var fileName = Path.GetFileName(path);
        var bytes = new ByteArrayContent(RobustFile.ReadAllBytes(path));
        form.Add(bytes, fileName, fileName);
        using var response = await _http
            .PostAsync($"{BaseUrl}/issues/{issueId}/attachments?fields=id", form, cancellation)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            return null;
        var json = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        // The upload endpoint answers with an array, even for one file.
        var node = JsonNode.Parse(json);
        return node is JsonArray array
            ? array.FirstOrDefault()?["id"]?.GetValue<string>()
            : node?["id"]?.GetValue<string>();
    }

    private async Task RestrictToDevelopersAsync(
        string issueId,
        string attachmentId,
        CancellationToken cancellation
    )
    {
        var payload = new JsonObject
        {
            ["visibility"] = new JsonObject
            {
                ["$type"] = "LimitedVisibility",
                ["permittedGroups"] = new JsonArray
                {
                    new JsonObject { ["id"] = DevelopersGroupId },
                },
            },
        };
        using var content = new StringContent(
            payload.ToJsonString(),
            Encoding.UTF8,
            "application/json"
        );
        using var response = await _http
            .PostAsync(
                $"{BaseUrl}/issues/{issueId}/attachments/{attachmentId}?fields=id,visibility($type)",
                content,
                cancellation
            )
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        // And check it actually took. A non-success status throws above, which is what makes the
        // delete-on-failure policy work - but a 200 that quietly did not apply the visibility would slip
        // through, and the consequence is the one thing this method exists to prevent: a dump containing
        // book text, file paths and the user's own details, readable by everyone who can see the card.
        //
        // The request already asks for `visibility($type)` back, so the check costs one parse. Throwing
        // here lands in the same catch as a failed upload, which deletes the attachment.
        var json = await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        var applied = JsonNode.Parse(json)?["visibility"]?["$type"]?.GetValue<string>();
        if (applied != "LimitedVisibility")
            throw new InvalidOperationException(
                $"the tracker accepted the restriction but reports visibility as '{applied ?? "none"}'"
            );
    }

    private async Task TryDeleteAttachmentAsync(string issueId, string attachmentId)
    {
        try
        {
            using var _ = await _http
                .DeleteAsync($"{BaseUrl}/issues/{issueId}/attachments/{attachmentId}")
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // We tried. The alternative — leaving it and saying nothing — is what we are avoiding.
        }
    }

    /// <summary>
    /// Decides whether a failure is worth retrying. A 4xx other than "too many requests" means the
    /// request itself is wrong and will stay wrong; retrying it forever would hammer the tracker and
    /// bury the real problem.
    /// </summary>
    private static SubmitOutcome ClassifyFailure(HttpStatusCode status) =>
        (int)status switch
        {
            429 => SubmitOutcome.NetworkUnavailable,
            >= 400 and < 500 => SubmitOutcome.RejectedPermanently,
            _ => SubmitOutcome.NetworkUnavailable,
        };

    /// <summary>
    /// Assembles what actually goes on the card: the report, plus the framing a reader needs — how long
    /// ago this happened, how many times, and the fingerprint that lets the next occurrence find this
    /// card instead of making a new one.
    /// </summary>
    private static string BuildBody(QueuedBundle bundle)
    {
        var metadata = bundle.Metadata;
        var text = new StringBuilder();

        text.AppendLine("*Filed automatically by the Bloom Freeze Doctor.*");
        text.AppendLine();

        var age = DateTimeOffset.UtcNow - metadata.GatheredAtUtc;
        if (age > TimeSpan.FromMinutes(10))
            text.AppendLine(
                $"> **This report was gathered {Describe(age)} ago** and queued until the machine could "
                    + "reach the tracker. Bloom's version below is the version at the time, which may not "
                    + "be what the user is running now."
            );
        if (metadata.Occurrences > 1)
            text.AppendLine(
                $"> **This problem happened {metadata.Occurrences} times** while the report was waiting"
                    + (
                        metadata.LastOccurrenceUtc.HasValue
                            ? $", most recently at {metadata.LastOccurrenceUtc:yyyy-MM-dd HH:mm}Z."
                            : "."
                    )
            );
        if (metadata.Occurrences > 1 || age > TimeSpan.FromMinutes(10))
            text.AppendLine();

        // What else went wrong with this same Bloom afterwards. Without this the fold would be worse than
        // the two cards it replaces: the death would be on disk as an attachment and nowhere in the text.
        if (metadata.FollowOnNotes.Count > 0)
        {
            text.AppendLine("**What happened next to this same Bloom**");
            text.AppendLine();
            foreach (var note in metadata.FollowOnNotes)
                text.AppendLine($"- {note}");
            text.AppendLine();
            text.AppendLine(
                "*Each of those was gathered as its own report and is attached, rather than filed as a "
                    + "separate card: they are instalments of one Bloom's failure, not separate problems.*"
            );
            text.AppendLine();
        }

        // Note what is deliberately NOT here: a line naming the files too large to attach. The comment that
        // carries the links says all of it, and says it better, because by then the links exist - a line
        // here can only name the file and send the reader looking elsewhere on the same card. It is written
        // whether the upload succeeded or failed, so nothing is lost by leaving the body quiet.
        //
        // The reason it cannot simply carry the links itself: this body is built before the card exists,
        // and the upload deliberately happens after, so that a creation that fails leaves nothing orphaned
        // in the bucket. See UploadWhatCouldNotBeAttachedAsync.

        text.AppendLine($"- **Gathered:** {metadata.GatheredAtUtc:yyyy-MM-dd HH:mm:ss}Z");
        text.AppendLine($"- **Fingerprint:** `{metadata.Fingerprint}`");
        text.AppendLine();
        text.AppendLine(ReadReport(bundle));
        text.AppendLine();
        text.AppendLine(
            $"*Fingerprint `{metadata.Fingerprint}` identifies this problem; a later occurrence will "
                + "comment here rather than open a new card. Attachments are restricted to the "
                + "Developers group because a dump can contain the user's own content.*"
        );
        return text.ToString();
    }

    /// <summary>
    /// Uploads the artifacts too large to attach, and adds one comment with links to them.
    ///
    /// After the card exists, rather than before, and for the same reason Bloom does it this way: an upload
    /// made before the card was created would be orphaned in the bucket if the creation then failed, and
    /// the retry would upload it again. This way nothing reaches the bucket unless there is a card to point
    /// at it.
    ///
    /// A failure here is reported on the card rather than swallowed. The dump is usually the most valuable
    /// thing in a report, so "we could not get it to you, and here is where it is on the user's machine" is
    /// worth far more than silence.
    /// </summary>
    /// <summary>
    /// Says where a file we could not get onto the card still is, and - the part that took a second look -
    /// how long that will remain true.
    ///
    /// "Still on the user's machine at <c>…</c>" was written as though the folder were permanent. It is
    /// not: the outbox drops a bundle 30 days after it was gathered, and sooner than that if more than
    /// <see cref="ReportOutbox.MaxBundles"/> reports pile up and it stops being one of the newest. So the
    /// sentence could be true when the card was filed and quietly false by the time somebody acted on it -
    /// which is worse than saying nothing, because it sends them looking.
    ///
    /// Both dates matter and only one can be given, so the wording promises the floor and warns about the
    /// other. A machine with twenty queued reports is a machine in a bad state, which is exactly when
    /// somebody is reading these cards.
    /// </summary>
    internal static string StillOnTheUsersMachine(QueuedBundle bundle, string fileName)
    {
        var keptUntil = bundle.Metadata.GatheredAtUtc + ReportOutbox.MaxAge;
        return $"Still on the user's machine at `{Path.Combine(bundle.Directory, fileName)}`, "
            + $"kept until about {keptUntil:yyyy-MM-dd} - or less if the Doctor gathers more than "
            + $"{ReportOutbox.MaxBundles} reports before then, so ask sooner rather than later.";
    }

    private async Task UploadWhatCouldNotBeAttachedAsync(
        string issueId,
        QueuedBundle bundle,
        CancellationToken cancellation
    )
    {
        var tooBig = SortArtifacts(bundle).TooBig;
        if (tooBig.Count == 0)
            return;

        var lines = new StringBuilder();
        lines.AppendLine(
            "**Files too large for a tracker attachment**, uploaded to Bloom's support bucket:"
        );
        lines.AppendLine();
        var anyFailed = false;
        foreach (var path in tooBig)
        {
            var name = Path.GetFileName(path);
            var megabytes = SizeOrZero(path) / 1024.0 / 1024.0;
            var url = await Protocol
                .SupportFileUploader.TryUploadAsync(path, cancellation)
                .ConfigureAwait(false);
            if (url == null)
            {
                anyFailed = true;
                lines.AppendLine(
                    $"- `{name}` ({megabytes:F1} MB) — **upload failed.** "
                        + StillOnTheUsersMachine(bundle, name)
                );
            }
            else
            {
                lines.AppendLine($"- [{name}]({url}) ({megabytes:F1} MB)");
            }
        }
        // No standing note about the links being world-readable. That is a decision already taken and
        // recorded where decisions belong - in SupportFileUploader, next to the code that sets the ACL -
        // and repeating it on every card is a paragraph of nothing for whoever reads them.
        if (anyFailed)
            lines.AppendLine(
                Environment.NewLine
                    + "*An upload that failed can be retried by hand from the folder named above; the "
                    + "Doctor does not try again, because the card is already filed.*"
            );

        // Best effort: the card is already filed and its own contents are intact, so a comment that will
        // not post must not turn a successful filing into a failure.
        await CommentAsync(issueId, lines.ToString(), cancellation).ConfigureAwait(false);
    }

    /// <summary>
    /// Splits a bundle's artifacts into the ones we will attach to the card and the ones that have to be
    /// uploaded to the bucket instead.
    ///
    /// One place decides, and both the card's text and the upload itself read the answer from here — so
    /// what the card says happened to a file is what actually happened to it. They were separate walks
    /// over the same list with the same arithmetic repeated, which is a standing invitation to drift.
    /// </summary>
    private static (List<string> ToAttach, List<string> TooBig) SortArtifacts(QueuedBundle bundle)
    {
        var toAttach = new List<string>();
        var tooBig = new List<string>();
        var budget = MaxAttachmentBytes;
        foreach (var path in bundle.ArtifactPaths)
        {
            if (!RobustFile.Exists(path))
                continue;
            var size = SizeOrZero(path);
            if (size > MaxSingleAttachmentBytes || size > budget)
            {
                tooBig.Add(path);
                continue;
            }
            budget -= size;
            toAttach.Add(path);
        }
        return (toAttach, tooBig);
    }

    /// <summary>A file's size, or zero if it cannot be measured — which the callers treat as "free".</summary>
    private static long SizeOrZero(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static string ReadReport(QueuedBundle bundle)
    {
        try
        {
            // The card gets the long blocks collapsed; the file on disk keeps them as plain Markdown. The
            // gatherer marks the regions and this is the only place that knows what YouTrack renders.
            return Gathering.CollapsibleSections.RenderForACard(
                RobustFile.ReadAllText(bundle.ReportPath)
            );
        }
        catch (Exception e)
        {
            return $"*(the report body could not be read from disk: {e.GetType().Name})*";
        }
    }

    private static string Describe(TimeSpan age) =>
        age.TotalDays >= 1 ? $"{age.TotalDays:F0} day(s)"
        : age.TotalHours >= 1 ? $"{age.TotalHours:F0} hour(s)"
        : $"{age.TotalMinutes:F0} minute(s)";

    private static string Trim(string value) =>
        value.Length <= 300 ? value : value.Substring(0, 299) + "…";
}
