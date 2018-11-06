import ReadersSynphonyWrapper from "./ReadersSynphonyWrapper";
import { ReaderStage } from "./ReaderSettings";

describe("ReadersSynphonyWrapper tests", () => {
    it("loads a file with a specified name", () => {
        // Todo PhilH: this test should load a data file and check that the expected stages are present
        expect(true).toBe(true);
    });
    it("initially has empty list of stages", () => {
        var api = new ReadersSynphonyWrapper();
        expect(api.getStages()).toEqual([]);
    });
    it("remembers added stages", () => {
        var api = new ReadersSynphonyWrapper();
        var stage1 = new ReaderStage("1");
        api.AddStage(stage1);
        expect(api.getStages()[0]).toBe(stage1);

        var stage2 = new ReaderStage("2");
        api.AddStage(stage2);
        expect(api.getStages()[0]).toBe(stage1);
        expect(api.getStages()[1]).toBe(stage2);
    });
});

describe("Stage tests", () => {
    it("remembers its name", () => {
        var stage = new ReaderStage("X");
        expect(stage.getName()).toBe("X");
    });
});
