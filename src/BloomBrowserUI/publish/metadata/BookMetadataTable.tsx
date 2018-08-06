import * as React from "react";
import ReactTable from "react-table";
import { BloomApi } from "../../utils/bloomApi";

export default class BookMetadataTable extends React.Component {
    public readonly state = { data: [] };

    public componentDidMount() {
        BloomApi.get("book/metadata", result => {
            this.setState({ data: result.data });
        });
    }
    public render() {
        return (
            <ReactTable
                loading={false}
                NoDataComponent={() => (
                    <div className="loading">Loading...</div>
                )}
                showPagination={false}
                minRows={1} //don't add extra blank rows
                data={this.state.data}
                columns={[
                    {
                        // there is no automatic way to compute this (https://github.com/react-tools/react-table/issues/94);
                        // need to keep it large enough for localization
                        width: 150,
                        accessor: "key"
                    },
                    {
                        Cell: (cellInfo: any) => {
                            const f = cellInfo.original;
                            switch (f.type) {
                                case "image":
                                    return <img src={f.value} />;
                                case "readOnlyText":
                                    return f.value;
                                /* future
                                case "editableText":
                                break;
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
