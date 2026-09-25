# Cloud Team Collections backend: TEST-ONLY MIRROR

Everything under `schemas/`, `migrations/`, `tests/` and `functions/` here (plus the root
`deno.json`/`deno.lock`, `build/regen-init-migration.sh`, `server/dev/seed.sql`,
`server/provision-aws.ps1` and `Design/CloudTeamCollections/{CONTRACTS,SCHEMA,GOING-LIVE}.md`)
is a copy of the backend in **BloomBooks/bloom-core-supabase**, taken at commit **d7caf31**
("Let the client make the checkout GUID; no GUID from check-in (BL-16531)"). It is here only so the client's live tests and the E2E harness
(`src/BloomTests/e2e`) can run a local stack from this repo.

**Do not edit these files here.** Make changes in bloom-core-supabase, then refresh the mirror:

```powershell
build/sync-backend-from-core.ps1 -CoreRepo <path to a clean bloom-core-supabase checkout>
bash build/regen-init-migration.sh   # must leave supabase/migrations unchanged
```

The copy is exact apart from path rewrites (`team-collections/dev/` there is `server/dev/`
here, and so on; see the script). `config.toml` and the rest of `server/dev/` are maintained
here by hand: this repo's `project_id` (which names the Docker network MinIO joins) and seed
path differ from bloom-core-supabase's.
