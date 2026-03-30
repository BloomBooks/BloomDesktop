import { describe, expect, it } from "vitest";
import { SignLanguageTool } from "./signLanguageTool";

describe("SignLanguageTool", () => {
    it("allows detachFromPage before controls are mounted", () => {
        const tool = new SignLanguageTool();

        expect(() => tool.detachFromPage()).not.toThrow();
    });

    it("allows newPageReady before controls are mounted", () => {
        const tool = new SignLanguageTool();

        expect(() => tool.newPageReady()).not.toThrow();
    });
});
