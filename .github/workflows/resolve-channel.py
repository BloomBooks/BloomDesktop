"""Decides what a release-installer run should do, and refuses if anything looks wrong.

Run by the `plan` job of release-installer.yml. Everything it needs arrives in the
environment; everything it decides goes to $GITHUB_OUTPUT.

Its first job is to check that release-installer.yml is internally consistent. Four things in
that file are per-branch and say overlapping things -- the push trigger's branch,
EXPECTED_BRANCH, ALLOWED_CHANNELS, and the `channel` dropdown's options -- because GitHub will
populate neither a branch filter nor a dropdown from a file. Repetition that a merge can take
half of is a hazard, so rather than trusting it we read the file and compare. That is also why
this is Python and not pwsh: it can use a real YAML parser instead of a regex that works right
up until it doesn't.
"""

import json
import os
import sys

import yaml

WORKFLOW = ".github/workflows/release-installer.yml"
CATALOGUE = "build/channels.json"

# Bloom.proj spells the stable channel as an empty string -- that is what produces Bloom.exe
# rather than BloomAlpha.exe, and installers/ rather than installersAlpha/ -- and it errors if
# handed the literal "Release". This is the one place that translation happens; everywhere a
# person looks, including latest.Release.json, the channel is called Release.
STABLE = "Release"


class Stop(Exception):
    """A reason to fail the run, phrased for whoever has to fix it."""


def env(name):
    return (os.environ.get(name) or "").strip()


def emit(**pairs):
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as f:
        for key, value in pairs.items():
            f.write("%s=%s\n" % (key, value))


def nothing_to_do(why):
    print(why)
    emit(should_run="false", channel_name="", channel="", publish="false",
         require_signatures="false", range_base="", range_max="")
    sys.exit(0)


def load_catalogue():
    """Every known channel and the band of patch numbers its versions are drawn from."""
    with open(CATALOGUE, encoding="utf-8") as f:
        channels = json.load(f)["channels"]

    bands, must_sign = {}, {}
    for name, spec in channels.items():
        band = spec.get("range")
        if not isinstance(band, list) or len(band) != 2 or band[0] > band[1]:
            raise Stop("%s: channel '%s' has an invalid range." % (CATALOGUE, name))
        bands[name] = (int(band[0]), int(band[1]))
        if "mustBeSigned" not in spec:
            raise Stop("%s: channel '%s' does not say whether it mustBeSigned." % (CATALOGUE, name))
        must_sign[name] = bool(spec["mustBeSigned"])

    # Overlapping bands would let one channel mint versions that read as another's, which is
    # the whole thing the bands exist to prevent.
    ordered = sorted(bands.items(), key=lambda kv: kv[1])
    for (a_name, a), (b_name, b) in zip(ordered, ordered[1:]):
        if b[0] <= a[1]:
            raise Stop("%s: the bands for '%s' (%d-%d) and '%s' (%d-%d) overlap."
                       % (CATALOGUE, a_name, a[0], a[1], b_name, b[0], b[1]))
    return bands, must_sign


def check_this_file_agrees_with_itself(expected_branch, allowed, continuous, bands):
    """The four per-branch settings must tell the same story, and a known one."""
    with open(WORKFLOW, encoding="utf-8") as f:
        # PyYAML reads the `on:` key as the boolean True, this being YAML.
        workflow = yaml.safe_load(f)

    triggers = workflow.get(True) or workflow.get("on") or {}
    branches = (triggers.get("push") or {}).get("branches") or []
    if branches != [expected_branch]:
        raise Stop(
            "%s builds on %s but EXPECTED_BRANCH says '%s'. A copy of this workflow serves "
            "exactly one branch; this one has been merged from somewhere else or half-edited."
            % (WORKFLOW, branches or "no branch", expected_branch))

    options = ((triggers.get("workflow_dispatch") or {}).get("inputs") or {}) \
        .get("channel", {}).get("options") or []
    if sorted(options) != sorted(allowed):
        raise Stop(
            "The channel dropdown offers %s but ALLOWED_CHANNELS says %s. They are the same "
            "list written twice, because GitHub cannot build a dropdown from a file, so they "
            "have to be edited together." % (options or "nothing", allowed or "nothing"))

    unknown = [c for c in allowed if c not in bands]
    if unknown:
        raise Stop("ALLOWED_CHANNELS names %s, which %s not in %s. Every channel needs a band "
                   "of patch numbers before anything can publish to it."
                   % (", ".join(unknown), "are" if len(unknown) > 1 else "is", CATALOGUE))

    if continuous and continuous not in allowed:
        raise Stop("CONTINUOUS_CHANNEL is '%s', which is not in ALLOWED_CHANNELS (%s). A branch "
                   "cannot publish continuously to a channel it is not allowed to publish to."
                   % (continuous, ", ".join(allowed) or "nothing"))


def main():
    expected_branch = env("EXPECTED_BRANCH")
    allowed = env("ALLOWED_CHANNELS").split()
    continuous = env("CONTINUOUS_CHANNEL")
    event, ref = env("EVENT_NAME"), env("REF")

    bands, must_sign = load_catalogue()
    check_this_file_agrees_with_itself(expected_branch, allowed, continuous, bands)

    # With the trigger and EXPECTED_BRANCH agreeing, a push can only arrive on the named
    # branch, so a mismatch here means something stranger than a stale merge. Say so rather
    # than passing quietly.
    if ref != "refs/heads/%s" % expected_branch:
        raise Stop("This workflow serves %s; refusing to run on %s." % (expected_branch, ref))

    if event == "push":
        if not continuous:
            nothing_to_do("%s publishes no channel continuously; nothing to do."
                          % expected_branch)
        name, publish = continuous, True
    else:
        name = env("CHANNEL_INPUT")
        if not name:
            raise Stop("No channel was chosen.")
        # GitHub already refuses a dispatch whose channel is not in the dropdown, with a 422.
        # This is the same rule enforced where it cannot be edited away by one hand-edit.
        if name not in allowed:
            raise Stop("%s may not publish to %s. It is allowed: %s."
                       % (expected_branch, name, ", ".join(allowed) or "nothing"))
        publish = env("PUBLISH_INPUT") == "true"

    if env("DRY_RUN_ONLY") == "true" and publish:
        print("DRY_RUN_ONLY is set: building %s, but not publishing it." % name)
        publish = False

    # Whether an unsigned build may go out is a property of the channel, not of how the run
    # was started: Beta and Release reach people who did not choose to be guinea pigs. Every
    # run still signs what it can and reports what it got; this only decides whether an
    # unsigned result stops the run. Nothing is being released on a run that does not publish,
    # so a dry run reports and carries on, which is what makes it usable without credentials.
    require_signatures = must_sign[name] and publish

    base, top = bands[name]
    channel = "" if name == STABLE else name
    print("channel=%s (msbuild: '%s') band=%d-%d mustBeSigned=%s publish=%s requireSignatures=%s"
          % (name, channel, base, top, must_sign[name], publish, require_signatures))
    emit(should_run="true", channel_name=name, channel=channel,
         publish=str(publish).lower(), require_signatures=str(require_signatures).lower(),
         range_base=base, range_max=top)


if __name__ == "__main__":
    try:
        main()
    except Stop as stop:
        sys.exit("%s" % stop)
