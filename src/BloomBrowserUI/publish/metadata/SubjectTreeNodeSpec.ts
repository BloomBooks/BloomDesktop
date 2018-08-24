import { SubjectTreeNode, JsSubject } from "./SubjectTreeNode";

describe("SubjectTreeNode tests", function() {
    it("getCodeList produces a space-delimited string of codes from an array of JsSubject", function() {
        let list: Array<JsSubject> = [];
        var sub1: JsSubject = {
            code: "MNP",
            description: "Plastic & reconstructive surgery"
        };
        var sub2: JsSubject = {
            code: "THV",
            description: "Alternative & renewable energy sources & technology"
        };
        var sub3: JsSubject = {
            code: "Y",
            description: "Children’s, Teenage & Educational"
        };
        list.push(sub1);
        list.push(sub2);
        list.push(sub3);

        // SUT
        let result = SubjectTreeNode.getCodeList(list);

        // verify
        expect(result).toBe("MNP THV Y");
    });
});
