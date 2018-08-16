import * as React from "react";
import ReactTable from "react-table";
import * as mobxReact from "mobx-react";
import { StringListCheckbox } from "../../react_components/stringListCheckbox";
import SubjectTreeNode from "./SubjectTreeNode";
import { themaSubjectData } from "./SubjectTreeNode";
import DropdownTreeSelect from "react-dropdown-tree-select";
import "./BookMetadataTable.less";
interface IProps {
    // We don't know or care what the top level elements are to this. We will show a row for each
    // of the top level entries that we find.
    // However the "value" of each entry must itself be an object of type {type:___, value:___}.
    // I don't know if it is possible to express that in Typescript and it doesn't seem worth a lot of effort.
    metadata: any;
}

// The BookMetadataTable shows some elements of https://docs.google.com/document/d/e/2PACX-1vREQ7fUXgSE7lGMl9OJkneddkWffO4sDnMG5Vn-IleK35fJSFqnC-6ulK1Ss3eoETCHeLn0wPvcxJOf/pub

// @observer means mobx will automatically track which observables this component uses
// in its render() function, and then re-render when they change. The "observable" here is the
// metadata prop, and it's observable because it is marked as such back where it is created in our parent component.
@mobxReact.observer
export default class BookMetadataTable extends React.Component<IProps> {
    constructor(props) {
        super(props);
        this.handleSubjectChange = this.handleSubjectChange.bind(this);
    }
    public componentDidMount() {}
    public render() {
        //console.log("rendering table");
        const metadata = this.props.metadata as any;
        return (
            <div>
                <ReactTable
                    loading={false}
                    NoDataComponent={() => (
                        <div className="loading">Loading...</div>
                    )}
                    showPagination={false}
                    minRows={1} //don't add extra blank rows
                    data={Object.keys(this.props.metadata).map(key => {
                        return {
                            key,
                            value: this.props.metadata[key].value,
                            type: this.props.metadata[key].type
                        };
                    })}
                    columns={[
                        {
                            // there is no automatic way to compute this (https://github.com/react-tools/react-table/issues/94);
                            // need to keep it large enough for localization
                            width: 150,
                            accessor: "key",
                            className: "label",
                            Cell: (cellInfo: any) => {
                                return <div>{cellInfo.value}</div>;
                            }
                        },
                        {
                            className: "value",
                            Cell: (cellInfo: any) => {
                                const f = cellInfo.original;
                                //console.log(JSON.stringify(f));
                                switch (f.type) {
                                    case "image":
                                        return <img src={f.value} />;
                                    case "readOnlyText":
                                        // We need to wrap in a div (or something) so we can put in a margin to replace the removed padding of rt-dt
                                        // See stylesheet for more info.
                                        return <div>{f.value}</div>;

                                    case "editableText":
                                        return (
                                            <textarea
                                                onBlur={(
                                                    event: React.FocusEvent<
                                                        HTMLTextAreaElement
                                                    >
                                                ) => {
                                                    this.props.metadata[
                                                        f.key
                                                    ].value =
                                                        event.currentTarget.value;
                                                }}
                                            >
                                                {f.value}
                                            </textarea>
                                        );

                                    case "subjects":
                                        return this.makeSubjectChooser();
                                    case "hazards":
                                        return this.makeHazardControls();
                                    case "a11yFeatures":
                                        return this.makeA11yFeaturesControls();
                                    default:
                                        return "??" + f.type;
                                }
                            }
                        }
                    ]}
                />
            </div>
        );
    }

    private makeHazardControls() {
        return (
            <div>
                {/* from https://www.w3.org/wiki/WebSchemas/Accessibility*/}
                {[
                    "flashingHazard",
                    "motionSimulationHazard",
                    "soundHazard"
                ].map(hazardName => {
                    return (
                        <StringListCheckbox
                            key={hazardName}
                            l10nKey={"BookMetadata." + hazardName}
                            list={this.props.metadata.hazards.value}
                            itemName={hazardName}
                            tristateItemOffName={"no" + hazardName}
                            onChange={list =>
                                (this.props.metadata.hazards.value = list)
                            }
                        >
                            {/* TODO in BL-6336, separate the key from what we show as a label */}
                            {hazardName}
                        </StringListCheckbox>
                    );
                })}
                {/* TODO: this is really helpful for testing the checkboxes, but we won't ship with it.*/}
                for testing only:
                <br />
                {this.props.metadata.hazards &&
                this.props.metadata.hazards.value
                    ? this.props.metadata.hazards.value
                    : "(none)"}
            </div>
        );
    }
    private makeA11yFeaturesControls() {
        return (
            <div>
                {/* from https://www.w3.org/wiki/WebSchemas/Accessibility*/}
                {["alternativeText", "signLanguage"].map(featureName => {
                    return (
                        <StringListCheckbox
                            key={featureName}
                            l10nKey={"BookMetadata." + featureName}
                            list={this.props.metadata.a11yFeatures.value}
                            itemName={featureName}
                            onChange={list =>
                                (this.props.metadata.a11yFeatures.value = list)
                            }
                        >
                            {/* TODO in BL-6336, separate the key from what we show as a label */}
                            {featureName}
                        </StringListCheckbox>
                    );
                })}
                {/* TODO: this is really helpful for testing the checkboxes, but we won't ship with it.*/}
                for testing only:
                <br />
                {this.props.metadata.a11yFeatures &&
                this.props.metadata.a11yFeatures.value
                    ? this.props.metadata.a11yFeatures.value
                    : "(none)"}
            </div>
        );
    }

    private makeSubjectChooser() {
        SubjectTreeNode.markSelectedSubjectNodes(
            themaSubjectData,
            " " + this.props.metadata.subjects.value + " "
        );
        const chooseLabel = "Choose...";
        // The current react-dropdown-tree-select "button" shows the label of only
        // a parent node if checked, and none of the labels of its children even if
        // one or more of them are checked.  It may be nontrivial to change that
        // behavior.  The code in this class ensures that only the desired nodes in
        // the tree are actually checked, and that the list returned to Bloom matches
        // exactly what has been checked by the user.
        return (
            <div>
                {/* REVIEW: Showing the codes is helpful for testing, but I don't know if we want to ship it.*/}
                Thema Codes: {this.props.metadata.subjects.value}
                <br />
                <DropdownTreeSelect
                    data={themaSubjectData}
                    onChange={this.handleSubjectChange}
                    placeholderText={chooseLabel}
                />
            </div>
        );
    }
    // Update the list of subject codes based on the current node that has just
    // changed, either to being checked or unchecked.  This is part of how we get
    // around the react-dropdown-tree-select behavior of checking all subnodes in
    // the tree when the user checks a branch node instead of a leaf node.
    private handleSubjectChange(
        currentNode: SubjectTreeNode,
        selectedNodes: SubjectTreeNode[] // not useful for our purposes: branches (parents) only
    ) {
        let subjects = " " + this.props.metadata.subjects.value + " ";
        if (currentNode.checked) {
            subjects = subjects + currentNode.value;
        } else {
            subjects = subjects.replace(" " + currentNode.value + " ", " ");
        }
        subjects = subjects
            .trim()
            .split(" ")
            .sort()
            .join(" ");
        this.props.metadata.subjects.value = subjects;
    }
}
