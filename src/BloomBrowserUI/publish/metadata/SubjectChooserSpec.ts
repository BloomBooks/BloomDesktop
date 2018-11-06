import { SubjectChooser, IProps } from "./SubjectChooser";
import { SubjectTreeNode } from "./SubjectTreeNode";

describe("SubjectChooser tests", () => {
    it("handleSubjectChange deletes Subjects correctly", () => {
        let list: Array<SubjectTreeNode> = [];
        var sub1: SubjectTreeNode = {
            value: "MNP",
            label: "Plastic & reconstructive surgery",
            notes: "some notes",
            checked: true,
            children: []
        };
        var sub2: SubjectTreeNode = {
            value: "Y",
            label: "Children’s, Teenage & Educational",
            notes: "some notes",
            checked: true,
            children: []
        };
        var sub3: SubjectTreeNode = {
            value: "THV",
            label: "Alternative & renewable energy sources & technology",
            notes: "some notes",
            checked: true,
            children: []
        };
        list.push(sub1);
        list.push(sub2);
        list.push(sub3);
        let props: IProps = { subjects: { value: list } };
        let chooser = new SubjectChooser(props);
        let currentNode: SubjectTreeNode = {
            value: "MNP",
            label: "Plastic & reconstructive surgery",
            notes: "some notes",
            checked: false,
            children: []
        };

        // SUT
        chooser.handleSubjectChange(currentNode, null);

        // verify unchecked subject is deleted from props value
        let result = props.subjects.value;
        expect(result.length).toBe(2);
        expect(result[0].value).toBe("THV"); // verifies sorting too
        expect(result[1].value).toBe("Y");
    });

    it("handleSubjectChange adds a new Subject and sorts correctly", () => {
        let list: Array<SubjectTreeNode> = [];
        var sub1: SubjectTreeNode = {
            value: "MNP",
            label: "Plastic & reconstructive surgery",
            notes: "some notes",
            checked: true,
            children: []
        };
        var sub2: SubjectTreeNode = {
            value: "Y",
            label: "Children’s, Teenage & Educational",
            notes: "some notes",
            checked: true,
            children: []
        };
        list.push(sub1);
        list.push(sub2);
        let props: IProps = { subjects: { value: list } };
        let chooser = new SubjectChooser(props);
        let currentNode: SubjectTreeNode = {
            value: "THV",
            label: "Alternative & renewable energy sources & technology",
            notes: "some notes",
            checked: true,
            children: []
        };

        // SUT
        chooser.handleSubjectChange(currentNode, null);

        // verify newly checked subject is added to props value
        let result = props.subjects.value;
        expect(result.length).toBe(3);
        expect(result[0].value).toBe("MNP");
        expect(result[1].value).toBe("THV"); // verifies correct sorting too
        expect(result[2].value).toBe("Y");
    });
});
