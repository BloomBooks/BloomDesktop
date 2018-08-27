// The JSON in ThemaData.json is adapted from https://www.editeur.org/files/Thema/1.3/Thema_v1.3.0_en.json
// with the tree hierarchy made explicit and the version information omitted.
// (Subjects from Thema are organized as a hierarchical tree but their JSON file is a flat list).
// Qualifier subjects were removed (all those with codes starting with a number)
// The Children related subjects were moved to the top of the list.
export const themaSubjectData: SubjectTreeNode[] = require("./ThemaData.json");

// A SubjectTreeNode represents the data from one node of the Thema based subject tree
// in a form usable by the react-dropdown-tree-select component.
// We store an array of these nodes to represent the top of the tree. (tops of the forest?)
export class SubjectTreeNode {
    // Made some parts optional to handle creating them from stored metadata
    constructor(
        public value: string,
        public label: string,
        public notes?: string,
        public checked?: boolean,
        public children?: SubjectTreeNode[]
    ) {}

    // Mark only the specifically selected nodes of the tree (forest?) as checked.
    // This is part of how we get around the react-dropdown-tree-select behavior of
    // checking all subnodes in the tree when the user checks a branch node instead
    // of a leaf node.
    public static markSelectedSubjectNodes(
        list: SubjectTreeNode[],
        currentSubjects: SubjectTreeNode[]
    ) {
        list.map(element => {
            if (
                SubjectTreeNode.matchCurrentSubject(
                    element.value,
                    currentSubjects
                )
            ) {
                element.checked = true;
            } else {
                element.checked = false;
            }
            this.markSelectedSubjectNodes(element.children, currentSubjects);
        });
    }

    // Check whether given code is in the current set of subject codes.
    private static matchCurrentSubject(
        codeValue: string,
        currentSubjects: SubjectTreeNode[]
    ): boolean {
        if (!currentSubjects) {
            return false;
        }
        return currentSubjects.some(subject => {
            return subject.value === codeValue;
        });
    }

    // Takes an array of Subjects each containing a code and a description.
    // Returns a space delimited string of codes.
    // TODO: This function is currently only used to:
    //   1) Display codes in the dialog for testing
    //   2) Run a test to verify this function's correct output.
    // Therefore, when we do away with (1), we can eliminate this function.
    public static getCodeList(currentSubjects: SubjectTreeNode[]): string {
        if (currentSubjects == null) return "";
        return currentSubjects
            .map(subject => {
                return subject.value;
            })
            .join(" ");
    }
}

export default SubjectTreeNode;
