import { describe, it, expect } from "vitest";
import { SubjectTreeNode } from "./SubjectTreeNode";

describe("SubjectTreeNode tests", () => {
    it("getCodeList produces a space-delimited string of codes from an array of SubjectTreeNodes", () => {
        const list: Array<SubjectTreeNode> = [];
        const sub1: SubjectTreeNode = {
            value: "MNP",
            label: "Plastic & reconstructive surgery",
            notes: "some notes",
            checked: false,
            children: [],
        };
        const sub2: SubjectTreeNode = {
            value: "THV",
            label: "Alternative & renewable energy sources & technology",
            notes: "some notes",
            checked: false,
            children: [],
        };
        const sub3: SubjectTreeNode = {
            value: "Y",
            label: "Children’s, Teenage & Educational",
            notes: "some notes",
            checked: false,
            children: [],
        };
        list.push(sub1);
        list.push(sub2);
        list.push(sub3);

        // SUT
        const result = SubjectTreeNode.getCodeList(list);

        // verify
        expect(result).toBe("MNP THV Y");
    });

    it("markSelectedSubjectNodes accomplishes its job selecting the correct nodes", () => {
        const list: Array<SubjectTreeNode> = [];
        const sub1: SubjectTreeNode = {
            value: "MNP",
            label: "Plastic & reconstructive surgery",
            notes: "some notes",
            checked: false,
            children: [],
        };
        const sub2: SubjectTreeNode = {
            value: "THV",
            label: "Alternative & renewable energy sources & technology",
            notes: "some notes",
            checked: false,
            children: [],
        };
        const sub3: SubjectTreeNode = {
            value: "Y",
            label: "Children’s, Teenage & Educational",
            notes: "some notes",
            checked: false,
            children: [],
        };
        list.push(sub1);
        list.push(sub2);
        list.push(sub3);

        const curSub1: SubjectTreeNode = {
            value: "THV",
            label: "Alternative & renewable energy sources & technology",
        };
        const curSub2: SubjectTreeNode = {
            value: "Y",
            label: "Children’s, Teenage & Educational",
        };
        const currentNodes: Array<SubjectTreeNode> = [];
        currentNodes.push(curSub1);
        currentNodes.push(curSub2);

        // SUT
        SubjectTreeNode.markSelectedSubjectNodes(list, currentNodes);

        // verify
        expect(list[0].checked).toBe(false);
        expect(list[1].checked).toBe(true);
        expect(list[2].checked).toBe(true);
    });
});
