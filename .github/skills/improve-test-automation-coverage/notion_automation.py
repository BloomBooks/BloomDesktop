"""Read and update the `Automation` lifecycle of test cases in the Notion "Test Case Runs" database.

The improve-test-automation-coverage skill (controller and workers) drives this script so that
no agent hand-rolls Notion REST calls. The token comes from the BLOOM_TESTCASE_NOTION
environment variable (Windows User scope; read it with
[Environment]::GetEnvironmentVariable('BLOOM_TESTCASE_NOTION','User') in PowerShell).

Usage:

    py notion_automation.py list-planned [--suite 6.5]
        Print every card in the suite run whose Automation is Planned, lowest Test Case ID
        first, as JSON: one object per card with testCaseId, pageId, url, title, summary,
        automation, automationNotes, areas, and priority.

    py notion_automation.py show <testCaseId> [--suite 6.5]
        Print the card's properties plus its Test Steps (the to_do blocks) as JSON.

    py notion_automation.py claim <testCaseId> [--suite 6.5]
        Re-read the card; if Automation is still Planned, set it to Building and print
        {"claimed": true}. Otherwise print {"claimed": false, "automation": "<value>"} and exit 3.
        This is the collision guard between developers who run the skill at the same time.

    py notion_automation.py set <testCaseId> <status> [--note "<text>"] [--suite 6.5]
        Set Automation to one of Manual, Planned, Building, "PR Pending", Automated, Partial,
        "Has automation problems", "Keep manual". With --note, replace Automation Notes with
        the text.

    py notion_automation.py note <testCaseId> "<text>" [--suite 6.5]
        Replace Automation Notes only.

    py notion_automation.py brief <testCaseId> --out <file> [--unattended] [--suite 6.5]
        Fill worker-brief.md (beside this script) for the card and write it to <file>. With
        --unattended the brief tells the worker never to ask the developer and how to decide alone. The
        placeholders are replaced literally, with the skill folder written with forward slashes,
        so no shell quoting or regex can mangle the path. Use this rather than sed.
"""

import argparse, datetime, json, os, pathlib, sys, time, urllib.request, urllib.error

BASE = "https://api.notion.com/v1/"
DATABASE_ID = "38c4bb19-df12-8123-8bc8-e65b962cb12f"
DEFAULT_SUITE = "6.5"
ATTENDED_QUESTIONS_RULE = """- **Ask the developer whenever the card is ambiguous.** Use the `AskUserQuestion` tool in this terminal;
  the developer watches Orca and answers there. Ask about intent, scope, and what "pass" means. Do not
  ask about things the add-e2e-test skill already decides."""

UNATTENDED_QUESTIONS_RULE = """- **Nobody answers questions in this run.** The developer is away. Never use `AskUserQuestion`; a
  question in this terminal blocks the run until it times out. Decide for yourself, with these
  rules. A small ambiguity that a careful tester would resolve the same way gets the
  conservative reading; write the reading down in the PR description and in the `PR Pending`
  note. A question of intent that the card leaves open (what counts as pass, which items are in
  scope, whether a hard step may be skipped) is a card problem: set `Has automation problems`
  with every question you would have asked in the note, and stop. Do not guess at intent."""

ATTENDED_UNDECIDED_RULE = "If you cannot decide, ask the developer with `AskUserQuestion`."

UNATTENDED_UNDECIDED_RULE = ("If you cannot decide, the card is not ready: use the `Has automation problems` path "
                             "below and put the open questions in the note.")

STATUSES = ["Manual", "Planned", "Building", "PR Pending", "Automated", "Partial", "Has automation problems", "Keep manual"]


def api(method, path, body=None):
    """One JSON call to the Notion API. Waits and retries on HTTP 429; raises on any other non-2xx answer.

    Notion rate-limits the token, and a controller plus several workers share this one token, so a
    429 is routine rather than an error. Notion says how long to wait in the Retry-After header.
    """
    data = json.dumps(body).encode() if body is not None else None
    headers = {
        "Authorization": "Bearer " + os.environ["BLOOM_TESTCASE_NOTION"],
        "Notion-Version": "2022-06-28",
        "Content-Type": "application/json",
    }
    for attempt in range(6):
        request = urllib.request.Request(BASE + path, data=data, method=method, headers=headers)
        try:
            return json.load(urllib.request.urlopen(request))
        except urllib.error.HTTPError as e:
            if e.code == 429 and attempt < 5:
                wait = float(e.headers.get("Retry-After", "10"))
                sys.stderr.write(f"[notion] 429 rate limited; waiting {wait:.0f}s (attempt {attempt + 1})\n")
                time.sleep(wait)
                continue
            sys.stderr.write(e.read().decode())
            raise


def plain(rich_text):
    return "".join(run["plain_text"] for run in rich_text)


def title_of(props):
    for prop in props.values():
        if prop["type"] == "title":
            return plain(prop["title"])
    raise KeyError("no title property")


def select_of(props, name):
    value = props[name]["select"]
    return value["name"] if value else None


def query(suite, extra_filters):
    """Every card in the suite run that matches the extra filters."""
    filters = [{"property": "Test Suite Run", "select": {"equals": suite}}] + extra_filters
    results, cursor = [], None
    while True:
        body = {"filter": {"and": filters}, "page_size": 100}
        if cursor:
            body["start_cursor"] = cursor
        page = api("POST", f"databases/{DATABASE_ID}/query", body)
        results += page["results"]
        if not page["has_more"]:
            return results
        cursor = page["next_cursor"]


def find_card(test_case_id, suite):
    cards = query(suite, [{"property": "Test Case ID", "number": {"equals": test_case_id}}])
    if len(cards) != 1:
        raise SystemExit(f"expected 1 card with Test Case ID {test_case_id} in suite {suite}, found {len(cards)}")
    return cards[0]


def summarize(card):
    props = card["properties"]
    return {
        "testCaseId": props["Test Case ID"]["number"],
        "pageId": card["id"],
        "url": card["url"],
        "title": title_of(props),
        "summary": plain(props["Summary"]["rich_text"]),
        "automation": select_of(props, "Automation"),
        "automationNotes": plain(props["Automation Notes"]["rich_text"]),
        "areas": [o["name"] for o in props["Areas"]["multi_select"]],
        "priority": select_of(props, "Priority") if props["Priority"]["type"] == "select" else None,
    }


def test_steps(page_id):
    """The to_do blocks of the card body, in order, with any nested children flattened."""
    steps, cursor = [], None
    while True:
        path = f"blocks/{page_id}/children?page_size=100" + (f"&start_cursor={cursor}" if cursor else "")
        page = api("GET", path)
        for block in page["results"]:
            kind = block["type"]
            text = plain(block[kind].get("rich_text", [])) if isinstance(block[kind], dict) else ""
            steps.append({"type": kind, "text": text, "checked": block[kind].get("checked") if kind == "to_do" else None})
            if block.get("has_children"):
                steps += test_steps(block["id"])
        if not page["has_more"]:
            return steps
        cursor = page["next_cursor"]


def update(page_id, status=None, note=None):
    props = {}
    if status is not None:
        if status not in STATUSES:
            raise SystemExit(f"status must be one of {STATUSES}")
        props["Automation"] = {"select": {"name": status}}
    if note is not None:
        props["Automation Notes"] = {"rich_text": [{"type": "text", "text": {"content": note[:2000]}}]}
    return api("PATCH", f"pages/{page_id}", {"properties": props})


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=["list-planned", "show", "claim", "set", "note", "brief"])
    parser.add_argument("args", nargs="*")
    parser.add_argument("--suite", default=DEFAULT_SUITE)
    parser.add_argument("--note")
    parser.add_argument("--out")
    parser.add_argument("--unattended", action="store_true")
    a = parser.parse_args()

    if a.command == "list-planned":
        cards = query(a.suite, [{"property": "Automation", "select": {"equals": "Planned"}}])
        rows = sorted((summarize(c) for c in cards), key=lambda r: r["testCaseId"])
        print(json.dumps(rows, indent=1))
        return

    if not a.args:
        raise SystemExit(f"{a.command} needs a Test Case ID, for example: {a.command} 349")
    try:
        test_case_id = int(a.args[0])
    except ValueError:
        raise SystemExit(f"the Test Case ID must be a number, not {a.args[0]!r}")
    card = find_card(test_case_id, a.suite)

    if a.command == "show":
        out = summarize(card)
        out["testSteps"] = test_steps(card["id"])
        print(json.dumps(out, indent=1))
    elif a.command == "claim":
        current = select_of(card["properties"], "Automation")
        if current != "Planned":
            print(json.dumps({"claimed": False, "automation": current}))
            sys.exit(3)
        update(card["id"], status="Building")
        print(json.dumps({"claimed": True, "url": card["url"]}))
    elif a.command == "set":
        if len(a.args) < 2:
            raise SystemExit(f"set needs a status; one of {STATUSES}")
        update(card["id"], status=a.args[1], note=a.note)
        print(json.dumps(summarize(find_card(test_case_id, a.suite))))
    elif a.command == "note":
        if len(a.args) < 2:
            raise SystemExit('note needs the text, for example: note 349 "..."')
        update(card["id"], note=a.args[1])
        print(json.dumps(summarize(find_card(test_case_id, a.suite))))
    elif a.command == "brief":
        if not a.out:
            raise SystemExit("brief needs --out <file>")
        info = summarize(card)
        skill_dir = pathlib.Path(__file__).resolve().parent.as_posix()
        template = (pathlib.Path(__file__).resolve().parent / "worker-brief.md").read_text(encoding="utf-8")
        text = (
            template.replace("{{TEST_CASE_ID}}", str(info["testCaseId"]))
            .replace("{{CARD_URL}}", info["url"])
            .replace("{{CARD_TITLE}}", info["title"])
            .replace("{{SKILL_DIR}}", skill_dir)
            .replace("{{TODAY}}", datetime.date.today().isoformat())
            .replace("{{QUESTIONS_RULE}}", UNATTENDED_QUESTIONS_RULE if a.unattended else ATTENDED_QUESTIONS_RULE)
            .replace("{{UNDECIDED_RULE}}", UNATTENDED_UNDECIDED_RULE if a.unattended else ATTENDED_UNDECIDED_RULE)
        )
        if "{{" in text:
            raise SystemExit("a placeholder in worker-brief.md was not filled: " + text[text.index("{{"):][:40])
        out = pathlib.Path(a.out)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(text, encoding="utf-8")
        print(json.dumps({"out": a.out, "testCaseId": info["testCaseId"], "title": info["title"]}))


if __name__ == "__main__":
    main()
