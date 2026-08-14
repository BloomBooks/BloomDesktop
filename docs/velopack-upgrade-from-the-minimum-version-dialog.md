# Upgrading in place from the "needs a newer Bloom" dialog

**Status:** implemented on this spike branch, not on the BL-16690 PR. The analysis below was
written first; see "What was actually built" at the end for how it turned out and where the
implementation differs from the plan.

BL-16690 added a dialog that appears when a collection declares a `MinimumBloomVersion` newer
than the Bloom being run. It offers two ways forward: open a different collection, or **Upgrade
Bloom** — which on the PR branch opens `bloomlibrary.org/downloads` in the browser and quits.

The obvious question is why it doesn't just use the updater Bloom already has. This is what that
would take.

## Why it wasn't done that way to begin with

Not because there's no browser. The dialog is a `ReactDialog`, so there is one. (Devin checked
this on the PR and confirmed it works pre-collection because `common/closeReactDialog` is
registered on `CommonApi` at the application level in `ApplicationContainer`, so
`BloomServer.EnsureListening()` is enough.)

The real reasons are below, in the order they'd have to be dealt with.

---

## 1. The updater reports everything through toasts, and there is no toast host yet

`ApplicationUpdateSupport.CheckForAVelopackUpdate` communicates **only** by toast:
`ShowToastForUpToDate`, `ShowToastForFoundUpdates`, `ShowToastForDownloading`,
`ShowToastForDownloadedWaitingForRestart`, `ShowToastForError`.

`ToastService.ShowToast` sends a websocket bundle on the `"toast"` client context
(`src/BloomExe/web/ToastService.cs`). The only thing that renders it is `ToastHost`, and
`ToastHost` is mounted in exactly one place:

```
src/BloomBrowserUI/app/App.tsx:74   <ToastHost />
```

`App.tsx` is the main workspace, which does not exist until a collection is open. So at the point
our dialog runs, every message the updater emits goes into the void — including "you are up to
date" and every error. The user would click Upgrade and see *nothing at all*.

**What it takes:** a progress UI of our own for this path. Our dialog already proves a
`ReactDialog` works here, so this is a new small dialog (checking → downloading → ready to
restart → failed), not new infrastructure.

## 2. The entry point is `async void`, so we can't wait for it or find out what happened

```csharp
// src/BloomExe/ApplicationUpdateSupport.cs:87
internal static async void CheckForAVelopackUpdate(
    BloomUpdateMessageVerbosity verbosity,
    Action restartBloom
)
```

Fire-and-forget, returning nothing. Our caller needs to know the outcome — did it update, is it
already the newest available, did it fail — because what the dialog does next depends on it.

**What it takes:** split the body into an awaitable core that *returns* an outcome, and keep the
existing method as a thin toast-driven wrapper so the workspace path is unchanged:

```csharp
internal enum UpdateAttempt { UpdatedAwaitingRestart, AlreadyNewest, NoChannelUrl, Failed }

internal static async Task<UpdateAttempt> TryUpdateAsync(BloomUpdateMessageVerbosity verbosity,
                                                         IUpdateProgressSink sink)

// unchanged signature, now a wrapper that reports through toasts
internal static async void CheckForAVelopackUpdate(BloomUpdateMessageVerbosity verbosity,
                                                   Action restartBloom)
```

The state machine (`_status`, `_bloomUpdateManager`, `_newVersion`) is already static and
single-instance, so it survives the refactor; the work is separating "what happened" from "how we
told the user."

## 3. Restarting assumes a Shell that doesn't exist yet

The `restartBloom` callback passed in from the workspace is:

```csharp
// WorkspaceView.RestartBloom()
shell.QuitForVersionUpdate = true;
shell.Close();
```

There is no `Shell` before a collection opens.

**What it takes:** pass `ProgramExit.Exit()` instead. That is enough, because
`DownloadAndApplyUpdates` hooks `Application.ApplicationExit` and calls
`WaitExitThenApplyUpdates` there — the update is applied on the way out either way. The
`QuitForVersionUpdate` flag only matters for the workspace's own shutdown bookkeeping.

## 4. **The one that actually decides it: Velopack has no idea what version we need**

This is the blocker that isn't a matter of effort.

`UpdateManager.CheckForUpdatesAsync()` answers one question: *is there anything newer on this
channel's feed?* It returns `null` for "no." The feed is per-channel —
`UpdateVersionTable.LookupURLOfUpdate` maps the running version to a channel-specific URL, and
`ApplicationUpdateSupport.GetUpdateUrl` resolves it.

So consider the case the dialog exists for. A user on **Release 6.4** opens a collection that
requires **6.5**, at a time when 6.5 has only reached Alpha:

1. We call `CheckForUpdatesAsync()`.
2. Release's feed has nothing newer than 6.4.
3. It returns `null`, and the existing code path shows "you are up to date."

The user is told they are up to date, by the same dialog that just told them their Bloom is too
old. They are now stuck with **no way forward at all** — which is strictly worse than the website,
which at least lists every channel's installers.

**What it takes:** the caller has to compare the version Velopack is *offering* against the
version the collection *demands*, and treat "the newest thing on your channel is still too old" as
its own outcome with its own message — something like *"The newest Bloom available on your Release
channel is 6.4.42, but this collection needs 6.5. You can download it from the website."* — and
then fall back to the website link.

That comparison is genuinely new logic, and it is the only part of this that cannot be assembled
from what already exists. `_newVersion.TargetFullRelease.Version` is the value to compare, and
`MinimumBloomVersionCheck.IsVersionSufficient` is the comparison to reuse so the two agree about
what "new enough" means.

## 5. The updater is switched off in several situations

`WorkspaceView.CheckForUpdatesImpl` refuses to run in three cases, each with its own message:

| Condition | Today's message |
|---|---|
| `Debugger.IsAttached` | "Sorry, you cannot check for updates from the debugger." |
| `InstallerSupport.SharedByAllUsers()` | "Your system administrator manages Bloom updates for this computer." |
| `ApplicationUpdateSupport.IsDev` | "Checking for updates is disabled on developer builds." |

All three apply equally here, and the middle one is a real deployment (school labs). In each we
would have to fall back to the website link rather than silently do nothing.

`Settings.Default.AutoUpdate` matters too: when it is off, the workspace path only *offers* the
download. From this dialog the user has explicitly asked to upgrade, so we would download
regardless — the same reasoning the existing code already uses for its "Update Now" toast.

## 6. Windows only

The whole updater body is inside `#if !__MonoCS__`. Linux would keep the website path, so both
paths have to stay anyway.

---

## What it adds up to

| Piece | Size | Risk |
|---|---|---|
| Awaitable core + toast wrapper | Moderate | Touches the shared update path used by the workspace — needs care not to regress normal updating |
| Progress dialog for the pre-collection case | Moderate | Self-contained; the pattern is proven |
| Version-aware "is the offer good enough" check | Small | New logic, but small and testable |
| Restart without a Shell | Small | Low |
| Guard parity + website fallback | Small | Low |

Perhaps a day or two of work, most of it in the refactor and the new dialog rather than in
anything conceptually hard.

## Recommendation

**Not worth doing for BL-16690, and possibly not worth doing at all in this form.**

The case where an in-place upgrade would help — the needed version is already published on the
user's own channel — is precisely the case where the user was going to be fine anyway; they get a
normal update prompt the next time Bloom checks. The case the dialog exists for is the *other*
one: the collection has moved ahead of what the user's channel offers. There, Velopack's honest
answer is "nothing newer here," and the website is the only real way forward.

So the in-place upgrade would add a day or two of work, a refactor of a path that currently works,
and a new dialog — in exchange for smoothing the case that was never really stuck, while still
needing the website fallback for the case that is.

If it is done anyway, **item 4 is not optional**. Shipping the naive version — call Velopack, show
its answer — would tell blocked users they are up to date and leave them with nowhere to go.

### A cheaper alternative worth considering

Most of the benefit for a fraction of the work: keep sending the user to the web, but send them
somewhere better. Pass the required version to the downloads page, or pick the URL by channel, so
they land on *the version this collection needs* rather than a generic installers list. That is a
URL change, and it fixes the "which of these do I click?" problem that is the actual friction.

---

# What was actually built

Implemented on this branch after the analysis above. The shape is smaller than the plan expected,
because two of the five obstacles turned out not to need the work they looked like they needed.

## The flow

Clicking **Upgrade Bloom** now:

1. Tries to update in place — find the update, download it, install it when Bloom exits.
2. Tells the user what happened, in a plain message box.
3. Quits.

If the in-place route can't deliver, it opens the download page on the way out, so the user still
has somewhere to go.

## What it does *not* need

**No toast host, and no toast.** Rather than route the existing toast-driven code through a new
progress UI, `ApplicationUpdateSupport.TryDownloadUpdateWithoutToasts` does the update work and
*returns what happened*, saying nothing itself. The caller decides what to show. Bloom's ordinary
message box is enough, and that already works before a collection is open.

The one place the old code would still have spoken up on its own is `GetUpdateUrl`, which reports
connection failures by toast — but only when asked to be `Verbose`. Calling it `Quiet` makes it
return a plain false instead, which is what we want.

**No awaitable refactor of the existing path.** The plan assumed `CheckForAVelopackUpdate` would
have to be split into an awaitable core with the toast version as a wrapper. In the event the new
method simply uses the same Velopack calls directly (`CheckForUpdatesAsync`, `DownloadUpdatesAsync`,
`WaitExitThenApplyUpdates`) and shares the class's existing state. The workspace's update path is
untouched, which is the safer outcome: nothing that works today was rearranged.

## The part that did matter: Velopack cannot be asked for a *particular* version

This is item 4 in the analysis, and it survived contact with the code exactly as described.
`CheckForUpdatesAsync` answers "is there anything newer on this channel", not "is there something
at least this new". So the implementation checks the answer itself:

```csharp
// MinimumBloomVersionCheck.UpgradeBloom
if (downloadedVersion != null && IsVersionSufficient(minimumVersion, downloadedVersion))
{
    ArrangeToInstallDownloadedUpdateOnExit();
    ...
}
// otherwise: say so plainly, and open the website instead
```

It reuses `IsVersionSufficient` — the same comparison that decided the collection was off-limits in
the first place — so the two can never disagree about what "new enough" means.

Three cases end at the website rather than an install:

- **Nothing newer on this channel.** The user is told what the newest version available to them is
  and what the collection needs, rather than the bare "you are up to date" that the normal path
  would produce — which would be a baffling thing to hear seconds after being told this Bloom is
  too old.
- **This Bloom can't update itself** — a developer build, one an administrator manages, or one
  under the debugger. The same three conditions `WorkspaceView` refuses on.

There is a fourth case that does **not** end at the website: an update that is newer than what the
user has but still short of what the collection needs. We install it anyway. Getting as far as the
channel allows is real progress, and the situation resolves itself — the upgraded Bloom reopens the
same collection, meets this same dialog, and the user decides again from a better starting point.
The message says so plainly ("that is newer than what you have, but this collection needs 6.6, so
you will need to upgrade again after this"), because the one thing that would be genuinely
confusing is upgrading, coming back, and meeting the same complaint with no explanation.

(An earlier draft of this branch refused to install in that case, on the grounds that we shouldn't
apply an update that doesn't achieve what the user asked for. That was wrong: it left them exactly
where they started, having declined an upgrade they could have had.)

## Smaller decisions

- **The user is told before the window disappears.** A downloaded upgrade ends with "Bloom X has
  been downloaded. Bloom will now close in order to install it." Without it, choosing Upgrade would
  simply make Bloom vanish, which reads as a crash.
- **Bloom does not restart itself** (`WaitExitThenApplyUpdates(null, true, false)`). The user was
  trying to open a collection this Bloom can't handle, so there is nothing useful to return to
  until the new version is installed.
- **No external-link icon on the Upgrade button here.** The PR-branch version always leaves Bloom
  for the website, so it carries the same marker the Help menu uses for that. This one normally
  upgrades Bloom itself and only falls back to the web when it can't, so marking it as leaving
  Bloom would be wrong most of the time.
- **The blocked collection is remembered after a successful download.** BL-16690 deliberately does
  *not* record a collection it refused to open, so that abandoning the upgrade doesn't strand the
  user on it. But once the upgrade is downloaded, the next Bloom to start really can open it, and
  landing straight back in it is what the user was trying to do — so `Program.OpenCollection` makes
  that one exception.
- **`Task.Run(...).GetAwaiter().GetResult()`.** The update work awaits network calls, and the
  caller is a synchronous method on the UI thread. Awaiting directly and blocking would deadlock;
  running it off the UI thread does not.

## What is not covered

- **No progress while downloading.** Bloom sits with no window for the length of the download.
  Acceptable for a spike; a real version wants at least a busy indicator.
- **Linux keeps the website route**, since the whole updater is inside `#if !__MonoCS__`.
- **Not tested against a live update feed.** The logic is exercised by reading; nobody has watched
  this actually download and install a newer Bloom. That is the first thing to do before taking it
  seriously.

## Does this change the recommendation?

Not really. It is less work than expected, and the code is contained. But the reasoning in the
analysis still holds: the case where an in-place upgrade helps is the case where the user would
have been fine anyway, and the case the dialog exists for — a collection that has moved ahead of
what the user's channel offers — still ends at the website. What this branch buys is that the
website is now the *fallback* rather than the only answer, and that when it is the fallback the
user is told why.
