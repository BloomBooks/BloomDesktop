# Cloud Team Collections — implementation master checklist

Design: [../CloudTeamCollections.md](../CloudTeamCollections.md) · Contracts: [CONTRACTS.md](CONTRACTS.md)
Rules: agents tick checkboxes **only in their own task file**; this master file is updated
**only by the orchestrator**. Every task PR must build, pass its acceptance tests, and pass
the entire existing folder-TC test suite.

## Branching

- Integration branch: `cloud-collections` (base branch: **confirm with John** — master vs the
  active Version6.x branch). Base merged into integration weekly.
- One branch + one git worktree per task; PRs into the integration branch, merged one at a
  time by the orchestrator after code review.

## Waves

| Wave | Tasks | Parallel? | Gate |
|------|-------|-----------|------|
| 0 | [00-enablers](tasks/00-enablers.md) | No — orchestrator-led (shared hot files) | Existing TC suite green, zero behavior change |
| 1 | [01-server-schema](tasks/01-server-schema.md) · [02-edge-functions](tasks/02-edge-functions.md) · [03-auth](tasks/03-auth.md) · [07-ui-setup](tasks/07-ui-setup.md) | Yes — zero file overlap (contracts frozen first) | Each task's acceptance tests |
| 2 | [04-client-core](tasks/04-client-core.md) · [08-ui-collection-tab](tasks/08-ui-collection-tab.md) | Yes | Unit suites green |
| 3 | [05-cloud-backend](tasks/05-cloud-backend.md) → [06-api-endpoints](tasks/06-api-endpoints.md) → UI wiring | **Sequenced** (shared files) | Two-instance manual smoke |
| 4 | [09-e2e](tasks/09-e2e.md) · [10-adoption](tasks/10-adoption.md) | Yes | Full E2E matrix green; dogfood |

## Shared-file schedule (no two concurrent tasks may touch the same one)

| File | Owner |
|------|-------|
| TeamCollection.cs, TeamCollectionManager.cs | Wave 0 only (orchestrator) |
| TeamCollectionApi.cs | 06 only |
| CollectionChooserDialog | 07 only |
| FeatureRegistry.cs, BloomExe.csproj | Orchestrator at merge time |
| supabase/** | 01/02 (01 owns migrations; 02 owns functions/) |

## Status

- [ ] Wave 0 complete (folder backend provably unchanged)
- [ ] Wave 1 complete
- [ ] Wave 2 complete
- [ ] Wave 3 complete
- [ ] Wave 4 complete
- [ ] Auth option decided (colleague review — see design doc Open items)
- [ ] Safety-window duration confirmed (7 days vs 1 day)

## Merge log

(orchestrator appends: date · task · PR · notes)
