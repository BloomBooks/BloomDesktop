// The JSON in ThemaData.json is adapted from https://www.editeur.org/files/Thema/1.3/Thema_v1.3.0_en.json
// with the tree hierarchy made explicit and the version information omitted.
// (Subjects from Thema are organized as a hierarchical tree but their JSON file is a flat list).
const themaData = require("./ThemaData.json");

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
}
export let themaSubjectData: SubjectTreeNode[] = themaData;
