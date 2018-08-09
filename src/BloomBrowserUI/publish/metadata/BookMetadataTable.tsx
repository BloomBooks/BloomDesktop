import * as React from "react";
import ReactTable from "react-table";
import { BloomApi } from "../../utils/bloomApi";

interface IProps {
    // We don't know or care what the top level elements are to this. We will show a row for each
    // of the top level entries that we find.
    // However the "value" of each entry must itself be an object of type {type:___, value:___}.
    // I don't know if it is possible to express that in Typescript and it doesn't seem worth a lot of effort.
    data: object;
}
export default class BookMetadataTable extends React.Component<IProps> {
    public componentDidMount() {}
    public render() {
        return (
            <ReactTable
                loading={false}
                NoDataComponent={() => (
                    <div className="loading">Loading...</div>
                )}
                showPagination={false}
                minRows={1} //don't add extra blank rows
                data={Object.keys(this.props.data).map(key => {
                    return {
                        key,
                        value: this.props.data[key].value,
                        type: this.props.data[key].type
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
                                                this.props.data[f.key].value =
                                                    event.currentTarget.value;
                                            }}
                                        >
                                            {f.value}
                                        </textarea>
                                    );
                                /* future
                                case "choices":
                                break;*/
                                default:
                                    return "??" + f.type;
                            }
                        }
                    }
                ]}
            />
        );
    }
}
