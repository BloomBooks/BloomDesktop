"""Audit what taking the Crowdin translations would do to one branch.

Usage (from the repo root):
    python -I -X utf8 audit_translations.py <branch-ref> <result-tree-or-commit> <out-dir>

<result> is the tree the branch would have afterwards: for master, the squashed and rebased
l10n_master commit; for a release branch, the tree printed by
`git merge-tree --write-tree --merge-base=<parent of squashed commit> <branch> <squashed commit>`.

Every changed translation is judged against the branch's OWN English files, because a
translation only matters on a branch whose English has that id with the same wording.
Prints a summary and writes one .tsv per finding kind into <out-dir>.
"""

import collections
import pathlib
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

X = "{urn:oasis:names:tc:xliff:document:1.2}"


class Unparseable(Exception):
    """An xliff file that isn't valid XML (CheckOrFixXliff reports these too)."""


def show(ref, path):
    """Return the bytes of path at ref, or None if it doesn't exist there."""
    r = subprocess.run(["git", "show", f"{ref}:{path}"], capture_output=True)
    return r.stdout if r.returncode == 0 else None


def inner(el):
    """The markup inside an element, as text (inline html in a translation is kept)."""
    if el is None:
        return ""
    s = ET.tostring(el, encoding="unicode")
    return s.split(">", 1)[1].rsplit("</", 1)[0] if "</" in s else ""


def units(data):
    """Map trans-unit id -> source, target, approved, state for one xliff file."""
    out = {}
    if data is None:
        return out
    try:
        root = ET.fromstring(data)
    except ET.ParseError as e:
        raise Unparseable(str(e))
    for tu in root.iter(X + "trans-unit"):
        t = tu.find(X + "target")
        out[tu.get("id")] = dict(
            src=inner(tu.find(X + "source")),
            tgt=inner(t),
            approved=tu.get("approved"),
            state=None if t is None else t.get("state"),
        )
    return out


def real(u):
    """A translation someone actually made: not empty, and not the English copied over."""
    return bool(u["tgt"]) and u["tgt"] != u["src"] and u["state"] != "needs-translation"


def referenced_in_code(ref, id_):
    """Whether the id appears literally in the branch's source. 'no' can still be wrong for
    ids that code builds at runtime (e.g. TemplateBooks.PageLabel.*)."""
    r = subprocess.run(["git", "grep", "-q", "-w", "-F", id_, ref, "--", "src"], capture_output=True)
    return "yes" if r.returncode == 0 else "no"


def main():
    base, result, outdir = sys.argv[1], sys.argv[2], pathlib.Path(sys.argv[3])
    names = subprocess.run(
        ["git", "diff", "--name-only", base, result, "--", "DistFiles/localization"],
        capture_output=True, text=True, check=True,
    ).stdout.splitlines()
    counts = collections.Counter()
    rows = collections.defaultdict(list)
    for p in names:
        d, f = re.match(r"DistFiles/localization/([^/]+)/(.+)$", p).groups()
        if f.startswith("ReadMe-"):
            lang, enpath = f[len("ReadMe-"):-len(".xlf")], f"DistFiles/localization/{d}/ReadMe-en.xlf"
        else:
            lang, enpath = d, f"DistFiles/localization/en/{f}"
        if lang == "en":
            counts["STOP: an English file changed"] += 1
            rows["english-changed"].append(p)
            continue
        try:
            old, new, en = units(show(base, p)), units(show(result, p)), units(show(base, enpath))
        except Unparseable as e:
            counts["STOP: invalid XML, file not audited"] += 1
            rows["invalid-xml"].append((p, str(e)))
            continue
        for id_, o in old.items():
            if id_ not in new and id_ in en and real(o):
                counts["deleted: translation of a string this branch's English has"] += 1
                rows["deleted"].append((lang, f, id_, o["tgt"][:80]))
        for id_, n in new.items():
            o = old.get(id_)
            if o and (o["tgt"], o["approved"], o["state"]) == (n["tgt"], n["approved"], n["state"]):
                continue
            if not real(n):
                if o and real(o) and id_ in en:
                    counts["emptied: real translation replaced by English filler"] += 1
                    rows["emptied"].append((lang, f, id_, o["tgt"][:80]))
                else:
                    counts["ignored: English filler"] += 1
                continue
            if id_ not in en:
                counts["ignored: string this branch's English doesn't have"] += 1
            elif en[id_]["src"] != n["src"]:
                counts["mismatch: translates different English wording"] += 1
                rows["mismatch"].append((lang, f, id_, en[id_]["src"][:70], n["src"][:70]))
            else:
                kind = "changed" if o and real(o) else "new"
                counts[f"lands: {kind} translation of a string this branch uses"] += 1
                rows["lands"].append((lang, f, id_, kind))

    print(f"{len(names)} localization files differ")
    for c, n in sorted(counts.items()):
        print(f"{n:7}  {c}")
    lands = collections.Counter(r[0] for r in rows["lands"])
    print("lands, by language:", ", ".join(f"{l}={n}" for l, n in lands.most_common()) or "none")

    # Deletions are always audited per id, with whether the branch's code still names it.
    deleted_ids = collections.defaultdict(set)
    for lang, f, id_, _ in rows["deleted"]:
        deleted_ids[(f, id_)].add(lang)
    if deleted_ids:
        print(f"\n{len(deleted_ids)} distinct ids would lose translations:")
        rows["deleted-ids"] = []
        for (f, id_), langs in sorted(deleted_ids.items()):
            ref = referenced_in_code(base, id_)
            rows["deleted-ids"].append((f, id_, str(len(langs)), ref))
            if ref == "yes":
                print(f"  IN CODE  {f}  {id_}  ({len(langs)} languages)")
        print("  (ids not marked IN CODE are not named literally in the branch's src; "
              "see deleted-ids.tsv)")

    outdir.mkdir(parents=True, exist_ok=True)
    for old_report in outdir.glob("*.tsv"):
        old_report.unlink()
    for k, v in rows.items():
        lines = ["\t".join(r) if isinstance(r, tuple) else r for r in v]
        (outdir / f"{k}.tsv").write_text("\n".join(lines) + "\n", encoding="utf-8")


main()
