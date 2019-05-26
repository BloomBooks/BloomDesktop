import * as React from "react";
import ReactTable from "react-table";
import * as mobxReact from "mobx-react";
import { StringListCheckbox } from "../../react_components/stringListCheckbox";
import { Label } from "../../react_components/l10nComponents";
import { Link } from "../../react_components/link";
import "./BookMetadataTable.less";
import SubjectChooser from "./SubjectChooser";
import A11yLevelChooser from "./A11yLevelChooser";

interface IProps {
    // We don't know or care what the top level elements are to this. We will show a row for each
    // of the top level entries that we find.
    // However the "value" of each entry must itself be an object of type {type:___, value:___}.
    // I don't know if it is possible to express that in Typescript and it doesn't seem worth a lot of effort.
    metadata: any;
    translatedControlStrings: any;
}

// The BookMetadataTable shows some elements of https://docs.google.com/document/d/e/2PACX-1vREQ7fUXgSE7lGMl9OJkneddkWffO4sDnMG5Vn-IleK35fJSFqnC-6ulK1Ss3eoETCHeLn0wPvcxJOf/pub

// @observer means mobx will automatically track which observables this component uses
// in its render() function, and then re-render when they change. The "observable" here is the
// metadata prop, and it's observable because it is marked as such back where it is created in our parent component.
@mobxReact.observer
export default class BookMetadataTable extends React.Component<IProps> {
    constructor(props) {
        super(props);
    }
    public componentDidMount() {}
    public render() {
        return (
            <div>
                <ReactTable
                    className="bookMetadataTable"
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
                            type: this.props.metadata[key].type,
                            translatedLabel: this.props.metadata[key]
                                .translatedLabel,
                            helpurl: this.props.metadata[key].helpurl
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
                                return (
                                    <>
                                        <Label
                                            l10nKey={
                                                "BookMetadata." + cellInfo.value
                                            }
                                            alreadyLocalized={true}
                                        >
                                            {cellInfo.original.translatedLabel}
                                        </Label>
                                        {cellInfo.original.helpurl &&
                                        cellInfo.original.helpurl.length > 0 ? (
                                            <Link
                                                className="whatsthis"
                                                l10nKey="BookMetadata.WhatsThis"
                                                href={cellInfo.original.helpurl}
                                            >
                                                What's this?
                                            </Link>
                                        ) : (
                                            ""
                                        )}
                                    </>
                                );
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
                                                defaultValue={f.value}
                                            />
                                        );

                                    case "subjects":
                                        return (
                                            <SubjectChooser
                                                subjects={
                                                    this.props.metadata.subjects
                                                }
                                            />
                                        );
                                    case "hazards":
                                        return this.makeHazardControls();
                                    case "a11yLevel":
                                        return (
                                            <A11yLevelChooser
                                                a11yLevel={
                                                    this.props.metadata
                                                        .a11yLevel
                                                }
                                            />
                                        );
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
                {/* "Sound Hazard" is too hard to explain (BL-6947) */}
                {["flashingHazard", "motionSimulationHazard"].map(
                    hazardName => {
                        return (
                            <StringListCheckbox
                                key={hazardName}
                                l10nKey={"BookMetadata." + hazardName}
                                alreadyLocalized={true}
                                list={this.props.metadata.hazards.value}
                                itemName={hazardName}
                                tristateItemOffName={
                                    "no" + this.capitalizeFirstChar(hazardName)
                                }
                                onChange={list =>
                                    (this.props.metadata.hazards.value = list)
                                }
                            >
                                {
                                    this.props.translatedControlStrings[
                                        hazardName
                                    ]
                                }
                            </StringListCheckbox>
                        );
                    }
                )}
            </div>
        );
    }
    private capitalizeFirstChar(hazardName: string): string {
        let uc = hazardName[0].toUpperCase();
        return uc + hazardName.substr(1, hazardName.length - 1);
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
                            alreadyLocalized={true}
                            list={this.props.metadata.a11yFeatures.value}
                            itemName={featureName}
                            onChange={list =>
                                (this.props.metadata.a11yFeatures.value = list)
                            }
                        >
                            {this.props.translatedControlStrings[featureName]}
                        </StringListCheckbox>
                    );
                })}
            </div>
        );
    }
}
