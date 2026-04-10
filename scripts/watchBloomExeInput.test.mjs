import assert from "node:assert/strict";
import test from "node:test";

import { isManualRestartCommand } from "./watchBloomExeInput.mjs";

test("isManualRestartCommand accepts enter with no text", () => {
    assert.equal(isManualRestartCommand("\n"), true);
});

test("isManualRestartCommand accepts r and restart", () => {
    assert.equal(isManualRestartCommand("r\n"), true);
    assert.equal(isManualRestartCommand("restart\r\n"), true);
});

test("isManualRestartCommand rejects unrelated commands", () => {
    assert.equal(isManualRestartCommand("x\n"), false);
});
