# Papercuts

Small dev/agent/tooling friction points captured mid-task (see the `papercut` skill).

- Running `dotnet test` (or any BloomExe build) while a `./go.sh` / `dotnet watch`
  Bloom is live fails at the copy-to-output step: the running process locks both
  `output/Debug/AnyCPU/Bloom.exe` (native apphost) and, once hot-reload deltas have
  been applied, `Bloom.dll` too (MSB3026/MSB3027 "being used by another process").
  Compilation itself succeeds; only the copy fails, so the tests never run.
  Workaround that neither kills the running instance nor touches the locked output:
  redirect the whole build to a scratch dir and skip the apphost, e.g.
  `dotnet test src/BloomTests/BloomTests.csproj --filter ... -p:UseAppHost=false -p:OutDir=<abs-scratch-path-with-trailing-slash>`.
