# BloomFreezeDoctor.Protocol

The protocol between Bloom and the Bloom Freeze Doctor — a diagnostic tool that watches Bloom and
reports freezes, unreported crashes, and processes whose window has gone but which are still
running. Both live in this repository: Bloom's side is `src/BloomExe/FreezeDoctor/`, the Doctor is
`src/BloomFreezeDoctor` and `src/BloomFreezeDoctor.Core`.

"Protocol" rather than "contract" because it is more than the shared data: alongside the layouts it
defines the kernel object naming scheme and the signalling handshakes the two use to ask each other
for something.

It is a plain project both sides reference, not a published package. It exists so that one wire
format has one definition instead of two hand-maintained copies. It contains:

- **`DoctorChannel`** — a small fixed-layout page in shared memory, written by Bloom and read by the
  Doctor, carrying a UI-thread heartbeat and what Bloom believes it is doing. Shared memory rather
  than a request/response API because the Doctor has to be able to read it when Bloom is wedged, and
  a wedged Bloom cannot answer anything.
- **`DoctorSession`** — a small JSON file per Bloom run, holding the facts that must outlive the
  process: above all which log file that run is writing to, which a watcher cannot reliably work out
  from outside.
- **`DoctorSignals`** — the named events the two use to reach each other: the Doctor announcing that
  it is watching, asking a stuck Bloom to exit, and the handshake around dumping a dying one.

Both sides pin the layout by value in a test. There is only one definition of it now, so the two
cannot hold copies that disagree — but they can still get out of step over a *version*, and the
pinned tests are what turn that into a failed build rather than a stream of plausible wrong reports.

Licensed under the MIT licence. © SIL Global.
