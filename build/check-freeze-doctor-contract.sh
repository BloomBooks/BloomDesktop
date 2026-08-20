#!/bin/sh
# Guards the three Freeze Doctor contract files in src/BloomExe/FreezeDoctor against drifting away
# from their source of truth in BloomBooks/bloom-freeze-doctor.
#
# WHY THIS EXISTS. Those files describe a wire format — shared-memory offsets, kernel object names, a
# JSON schema — that two separate programs have to agree about exactly. They are maintained as copies,
# and when copies of 750 lines are kept in step by hand, they drift: these two already had, and a fix
# made in one repo had to be carried across the other by hand. Drift here fails silently and
# expensively. Bloom writes one set of offsets, the Doctor reads another, and the result is a stream
# of confident, wrong diagnostic reports that nobody can tell apart from real ones.
#
# THIS IS DELIBERATELY TEMPORARY. The real fix is to stop having copies: ship the contract as a NuGet
# package from the Doctor's repo and let Bloom consume it, at which point these files leave this repo
# and this script should be deleted along with them. See BL-16719.
#
# WHAT IT DOES NOT CHECK. Only that the copies match. The layout constants are pinned separately, by
# value, in a test in each repo — so a deliberate change that is mirrored correctly but wrong still
# has to get past those. This is the outer of two nets, not the only one.
#
# It also only compares against whatever the other repo currently says. A change made there and not
# mirrored here is caught by the next PR that touches these files, or by running this on demand — the
# workflow has a manual trigger for exactly that, since a change in the other repo cannot trigger a
# workflow in this one.

cd "$(dirname "$0")/.." || exit 1

missing_dependencies=
for dependency in git tr grep; do
  if ! command -v "$dependency" >/dev/null 2>&1; then
    missing_dependencies="$missing_dependencies $dependency"
  fi
done
if [ -n "$missing_dependencies" ]; then
  echo "Missing required commands for build/check-freeze-doctor-contract.sh:$missing_dependencies"
  exit 1
fi

TEMP_CLONE=
cleanup() {
  [ -n "$TEMP_CLONE" ] && rm -rf "$TEMP_CLONE"
}
trap cleanup EXIT

echo "Checking the Freeze Doctor contract files against BloomBooks/bloom-freeze-doctor."

OURS_DIR="src/BloomExe/FreezeDoctor"
THEIRS_SUBPATH="src/BloomFreezeDoctor.Core/Contract"
FILES="DoctorChannel.cs DoctorSession.cs DoctorSignals.cs"

# Prefer a local clone, so a developer can run this offline and against uncommitted work.
# BLOOM_FREEZE_DOCTOR_REPO overrides where that clone is.
if [ -n "$BLOOM_FREEZE_DOCTOR_REPO" ]; then
  CLONE="$BLOOM_FREEZE_DOCTOR_REPO"
  if [ ! -d "$CLONE/$THEIRS_SUBPATH" ]; then
    echo "BLOOM_FREEZE_DOCTOR_REPO is set to '$CLONE', but $CLONE/$THEIRS_SUBPATH is not there."
    exit 1
  fi
elif [ -d "../bloom-freeze-doctor/$THEIRS_SUBPATH" ]; then
  CLONE="../bloom-freeze-doctor"
else
  # Otherwise take a shallow clone. Deliberately NOT raw.githubusercontent.com: that is behind a CDN
  # which served the previous version of a file for minutes after a push, so this check reported drift
  # that did not exist. A check that cries wolf gets switched off, which would be worse than not having
  # it — and this one exists precisely to be trusted.
  TEMP_CLONE=$(mktemp -d 2>/dev/null || mktemp -d -t fdcontract)
  echo "  (no local clone found; taking a shallow one)"
  if ! git clone --depth 1 --quiet https://github.com/BloomBooks/bloom-freeze-doctor "$TEMP_CLONE" 2>/dev/null; then
    echo "Could not clone BloomBooks/bloom-freeze-doctor, so the contract files cannot be compared."
    echo "If this machine has no network, set BLOOM_FREEZE_DOCTOR_REPO to a local clone instead."
    exit 1
  fi
  CLONE="$TEMP_CLONE"
fi

# Normalising away two things we do NOT want to fail on:
#  * the namespace declaration, which is meant to differ (Bloom.FreezeDoctor vs
#    BloomFreezeDoctor.Contract) and is the one intentional difference between the copies;
#  * all whitespace, because the two repos' formatters disagree about where to wrap long lines.
#    Bloom's csharpier hook rewraps three lines in these files on every commit, so a byte comparison
#    would fail permanently and immediately be ignored, which is worse than no check at all.
# Everything that carries meaning — offsets, names, constants, logic — still has to match.
#
# Whitespace is DELETED, not squeezed to a single space. Squeezing is not enough: csharpier's
# rewrapping inserts a newline after an opening bracket, which squeezes to a space the other copy
# does not have, so `foo( 0, ...)` and `foo(0, ...)` still compared unequal and the check failed on
# formatting alone. Deleting is safe here because none of the three files contains a string literal
# with a space in it — checked, and worth re-checking if that ever changes, since inside a literal a
# space would then be invisible to this comparison.
normalize() {
  grep -v '^namespace ' | tr -d '[:space:]'
}

failed=
missing=
for file in $FILES; do
  ours="$OURS_DIR/$file"
  if [ ! -f "$ours" ]; then
    # If the file has gone, the package migration has probably happened. Say so rather than failing
    # obscurely: this script is supposed to be deleted at that point.
    missing="$missing $file"
    continue
  fi

  source_description="$CLONE/$THEIRS_SUBPATH/$file"
  theirs_raw=$(cat "$source_description" 2>/dev/null)

  if [ -z "$theirs_raw" ]; then
    echo "  COULD NOT READ $source_description — skipping $file rather than guessing."
    failed="$failed $file"
    continue
  fi

  a=$(normalize < "$ours")
  b=$(printf '%s' "$theirs_raw" | normalize)

  if [ "$a" = "$b" ]; then
    echo "  ok       $file"
  else
    echo "  DIFFERS  $file"
    failed="$failed $file"
  fi
done

if [ -n "$missing" ]; then
  echo ""
  echo "These files are no longer in $OURS_DIR:$missing"
  echo "If the contract has moved to a NuGet package, delete this script — it has done its job."
  exit 1
fi

if [ -n "$failed" ]; then
  echo ""
  echo "The Freeze Doctor contract has drifted, in:$failed"
  echo ""
  echo "These files must describe the same wire format in both repos. Copy the changed file across so"
  echo "the two agree, in whichever direction is correct, and bump DoctorChannelLayout.SchemaVersion"
  echo "in BOTH repos if the layout itself changed. To see the difference:"
  echo ""
  echo "  diff <(cat $OURS_DIR/<file>) <(cat ../bloom-freeze-doctor/$THEIRS_SUBPATH/<file>)"
  echo ""
  echo "Differences in line wrapping alone do not trigger this — the comparison ignores whitespace."
  exit 1
fi

echo "The Freeze Doctor contract files agree with the Doctor's repo."
