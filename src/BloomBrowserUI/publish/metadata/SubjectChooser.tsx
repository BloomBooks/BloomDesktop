import * as React from "react";
import SubjectTreeNode from "./SubjectTreeNode";
import DropdownTreeSelect from "react-dropdown-tree-select";
import { themaSubjectData } from "./SubjectTreeNode";
import "./SubjectChooser.less";

export interface IProps {
    // We don't know or care what the top level elements are to this. We will show a row for each
    // of the top level entries that we find.
    // However the "value" of each entry must itself be an object of type {type:___, value:___}.
    // I don't know if it is possible to express that in Typescript and it doesn't seem worth a lot of effort.
    subjects: any;
}

export class SubjectChooser extends React.Component<IProps> {
    public componentDidMount() {
        this.loadMetadata(this.props.subjects.value);
    }

    private loadMetadata(subjects: any[]) {
        let subjectList: SubjectTreeNode[] = [];
        if (subjects) {
            subjects.forEach(subject => {
                let newSubj = new SubjectTreeNode(
                    subject["value"],
                    subject["label"],
                    "",
                    true
                );
                subjectList.push(newSubj);
            });
        }
        this.props.subjects.value = subjectList;
    }

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
    // Public for testing.
    public handleSubjectChange(
        currentNode: SubjectTreeNode,
        selectedNodes: SubjectTreeNode[] // not useful for our purposes: branches (parents) only
    ) {
        let metadataSubjects: SubjectTreeNode[] = this.props.subjects.value;
        if (!metadataSubjects) metadataSubjects = [];
        if (currentNode.checked) {
            metadataSubjects.push(currentNode); // appends subject to the end of the array
        } else {
            this.remove(metadataSubjects, currentNode);
        }
        metadataSubjects.sort((a, b) => {
            if (a.value < b.value) {
                return -1;
            }
            if (a.value > b.value) {
                return 1;
            }
            return 0;
        });
        this.props.subjects.value = metadataSubjects;
    }

    private remove(
        subjects: SubjectTreeNode[],
        subjectToRemove: SubjectTreeNode
    ) {
        let matches = subjects.filter(subj => {
            return subj.value === subjectToRemove.value;
        });
        if (matches.length !== 1) {
            // something went wrong, dump out and don't remove anything!
            return;
        }
        const index = subjects.indexOf(matches[0]);
        if (index !== -1) {
            subjects.splice(index, 1);
        }
    }
}

export default SubjectChooser;
