# Upgrading in place from the "needs a newer Bloom" dialog

**Status:** investigation only — nothing here is implemented. Spike branch for BL-16690.

BL-16690 added a dialog that appears when a collection declares a `MinimumBloomVersion` newer
than the Bloom being run. It offers two ways forward: open a different collection, or **Upgrade
Bloom** — which today opens `bloomlibrary.org/installers` in the browser and quits.

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
