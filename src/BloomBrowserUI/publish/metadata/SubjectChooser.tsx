import * as React from "react";
import SubjectTreeNode from "./SubjectTreeNode";
import { JsSubject } from "./SubjectTreeNode";
import DropdownTreeSelect from "react-dropdown-tree-select";
import { themaSubjectData } from "./SubjectTreeNode";
import "./SubjectChooser.less";

interface IProps {
    // We don't know or care what the top level elements are to this. We will show a row for each
    // of the top level entries that we find.
    // However the "value" of each entry must itself be an object of type {type:___, value:___}.
    // I don't know if it is possible to express that in Typescript and it doesn't seem worth a lot of effort.
    subjects: any;
}

export default class SubjectChooser extends React.Component<IProps> {
    public render() {
        SubjectTreeNode.markSelectedSubjectNodes(
            themaSubjectData,
            this.props.subjects.value
        );
        // The current react-dropdown-tree-select "button" shows the label of only
        // a parent node if checked, and none of the labels of its children even if
        // one or more of them are checked.  It may be nontrivial to change that
        // behavior.  The code in this class ensures that only the desired nodes in
        // the tree are actually checked, and that the list returned to Bloom matches
        // exactly what has been checked by the user.
        return (
            <div className="subjectChooser">
                for testing only:{" "}
                {SubjectTreeNode.getCodeList(this.props.subjects.value)}
                <br />
                <DropdownTreeSelect
                    data={themaSubjectData}
                    onChange={(a, b) => this.handleSubjectChange(a, b)}
                    placeholderText={""}
                />
            </div>
        );
    }

    // Update the array of subjects based on the current node that has just
    // changed, either to being checked or unchecked.  This is part of how we get
    // around the react-dropdown-tree-select behavior of checking all subnodes in
    // the tree when the user checks a branch node instead of a leaf node.
    private handleSubjectChange(
        currentNode: SubjectTreeNode,
        selectedNodes: SubjectTreeNode[] // not useful for our purposes: branches (parents) only
    ) {
        let currentSubject: JsSubject = {
            code: currentNode.value,
            description: currentNode.label
        };
        let metadataSubjects: JsSubject[] = this.props.subjects.value;
        if (!metadataSubjects) metadataSubjects = [];
        if (currentNode.checked) {
            metadataSubjects.push(currentSubject); // appends subject to the end of the array
        } else {
            this.remove(metadataSubjects, currentSubject);
        }
        metadataSubjects.sort((a, b) => {
            if (a.code < b.code) {
                return -1;
            }
            if (a.code > b.code) {
                return 1;
            }
            return 0;
        });
        this.props.subjects.value = metadataSubjects;
    }

    private remove(subjects: JsSubject[], subjectToRemove: JsSubject) {
        const index = subjects.indexOf(subjectToRemove);
        if (index !== -1) {
            subjects.splice(index, 1);
        }
    }
}
