// The JSON in ThemaData.json is adapted from https://www.editeur.org/files/Thema/1.3/Thema_v1.3.0_en.json
// with the tree hierarchy made explicit and the version information omitted.
// (Subjects from Thema are organized as a hierarchical tree but their JSON file is a flat list).
export const themaSubjectData: SubjectTreeNode[] = require("./ThemaData.json");

// A SubjectTreeNode represents the data from one node of the Thema based subject tree
// in a form usable by the react-dropdown-tree-select component.
// We store an array of these nodes to represent the top of the tree. (tops of the forest?)
export default class SubjectTreeNode {
    constructor(
        public value: string,
        public label: string,
        public notes: string,
        public checked: boolean,
        public children: SubjectTreeNode[]
    ) {}

    // Mark only the specifically selected nodes of the tree (forest?) as checked.
    // This is part of how we get around the react-dropdown-tree-select behavior of
    // checking all subnodes in the tree when the user checks a branch node instead
    // of a leaf node.
    public static markSelectedSubjectNodes(
        list: SubjectTreeNode[],
        currentSubjects: string // space separated list of codes, with leading and trailing spaces
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
    // (This implementation uses simple string search.)
    private static matchCurrentSubject(
        codeValue: string,
        currentSubjects: string // space separated list of codes, with leading and trailing spaces
    ): boolean {
        return currentSubjects.indexOf(" " + codeValue + " ") >= 0;
    }
}
