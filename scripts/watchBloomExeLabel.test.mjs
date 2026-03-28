import assert from "node:assert/strict";
import test from "node:test";

import { formatStartupLabel } from "./watchBloomExeLabel.mjs";

test("formatStartupLabel uses repo label when branch is unavailable", () => {
    assert.equal(
        formatStartupLabel("BloomDesktop", undefined, false),
        "/BloomDesktop/",
    );
});

test("formatStartupLabel prefers branch for non-worktree repos", () => {
    assert.equal(
        formatStartupLabel("BloomDesktop", "testbranch", false),
        "/testbranch/",
    );
});

test("formatStartupLabel avoids repeating matching worktree branch names", () => {
    assert.equal(
        formatStartupLabel(
            "BL-16014-MultipleDevExes",
            "BL-16014-MultipleDevExes",
            true,
        ),
        "/BL-16014-MultipleDevExes/",
    );
});

test("formatStartupLabel shows repo and branch when helpful for worktrees", () => {
    assert.equal(
        formatStartupLabel("BL-16014-MultipleDevExes", "testbranch", true),
        "/BL-16014-MultipleDevExes (testbranch)/",
    );
});
