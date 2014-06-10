describe("SynphonyApi tests", function() {
    it("loads a file with a specified name", function() {
        // Todo PhilH: this test should load a data file and check that the expected stages are present
        expect(true).toBe(true);
    });
    it("initially has empty list of stages", function() {
        var api = new SynphonyApi();
        expect(api.getStages()).toEqual([]);
    });
    it("remembers added stages", function() {
        var api = new SynphonyApi();
        var stage1 = new Stage("1");
        api.AddStage(stage1);
        expect(api.getStages()[0]).toBe(stage1);

        var stage2 = new Stage("2");
        api.AddStage(stage2);
        expect(api.getStages()[0]).toBe(stage1);
        expect(api.getStages()[1]).toBe(stage2);
    });
});

describe("Stage tests", function() {
    it("remembers its name", function() {
        var stage = new Stage("X");
        expect(stage.getName()).toBe("X");
    });
    it("parses a string into frequency counts", function() {
        var stage = new Stage("1");
        stage.addWords("the cat sat on the mat");
        var words = stage.getWords();
        expect(words.length).toEqual(5);
        expect(words).toContain("the");
        expect(words).toContain("sat");
        expect(stage.getFrequency("the")).toBe(2);
        expect(stage.getFrequency("cat")).toBe(1);
    });
});