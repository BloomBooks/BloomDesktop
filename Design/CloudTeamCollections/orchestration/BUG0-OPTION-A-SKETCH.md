# Bug #0 option (a) implementation sketch — server-side "seat"

Prepared while the bot gauntlet runs; NOT committed anywhere. If John picks (a), this is the plan.

## Concept
A "seat" = one local collection folder on one machine. Lock takeover is only legitimate within
the same seat (the true shared-computer scenario: account B opens the exact folder account A
used). Two folders on one machine are two seats.

## Server (one migration, purely additive like 20260709000007)
- `alter table tc.books add column locked_seat text;` (nullable; null = legacy/unknown seat).
- `checkout_book(p_book_id, p_machine, p_seat)`: new optional param, stored on lock acquire.
  (PostgREST tolerates the extra param only if the SQL function signature adds it — bump the
  function, keep old 2-arg overload delegating with p_seat=null so old clients don't break;
  CONTRACTS.md addition to note.)
- `checkout_book_takeover(p_book_id, p_machine, p_seat)`: takeover requires
  `locked_by_machine = p_machine AND locked_seat IS NOT DISTINCT FROM p_seat AND locked_seat IS NOT NULL`
  — i.e. seat must match AND be known; a null (legacy) seat refuses takeover (fail safe).
- `unlock_book` / `force_unlock` / `checkin_finish_tx`: clear locked_seat wherever locked_by is
  cleared (audit which already clear locked_by_machine; mirror that).
- pgTAP: same-seat takeover OK; same-machine-different-seat REFUSED; null-seat REFUSED;
  cross-machine REFUSED (existing); checkout stores seat; unlock clears it.

## Client
- Seat id: stable hash of the local collection folder path + machine
  (e.g. first 16 hex of SHA256(lowercased full path)). Compute in TeamCollectionManager or
  CloudTeamCollection (has _localCollectionFolder). NOT the raw path (privacy in server rows).
- CloudCollectionClient.CheckoutBook/CheckoutBookTakeover: add seat param.
- CloudTeamCollection.TryLockInRepo / TryTakeOverLock: pass the seat.
- CanTakeOverLockOnThisMachine: unchanged machine check client-side (server enforces seat);
  optionally ALSO gate client-side if the cache carries locked_seat (cache delta shape would
  need the column too — get_collection_state view addition).
- Cache: add locked_seat to CloudRepoCache book rows + snapshot/delta parsing (or skip caching
  it and let the server be sole enforcer — SIMPLER: skip cache change; client attempts
  takeover, server refuses, AttemptLock then correctly reports "locked by other").
  RECOMMENDED: skip the cache/client gate entirely; server-only enforcement. Client behavior
  on refusal already falls back to the ordinary "locked by someone else" path (verify
  TryTakeOverLock's failure handling does this — it should treat {success:false} like a lost
  checkout race).

## e2e-4 expectation
With (a): alice's attemptLock → takeover refused (different seat) → lock stays bob's →
spec's final assertions pass unchanged. e2e-10's takeover (same folder = same seat) keeps
passing (its bob-takeover opens ALICE's folder).

## Estimate
Migration + pgTAP ~1h careful work incl. running against local stack; client param plumbing
~20 min; e2e-4 + e2e-10 rerun ~10 min.
