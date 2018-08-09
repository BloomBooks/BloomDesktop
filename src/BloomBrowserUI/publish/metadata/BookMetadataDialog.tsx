import * as React from "react";
import * as ReactModal from "react-modal";
import "./BookMetadataDialog.less";
import CloseOnEscape from "react-close-on-escape";
import BookMetadataTable from "./BookMetadataTable";
import { BloomApi } from "../../utils/bloomApi";

// tslint:disable-next-line:no-empty-interface
interface IProps {}
export default class BookMetadataDialog extends React.Component<IProps> {
    private static singleton: BookMetadataDialog;
    public readonly state = { isOpen: false, data: Object };
    constructor(props: IProps) {
        super(props);
        BookMetadataDialog.singleton = this;
    }
    public componentDidMount() {
        BloomApi.get("book/metadata", result => {
            this.setState({ data: result.data });
        });
    }
    private handleCloseModal(doSave: boolean) {
        if (doSave) {
            BloomApi.postData("book/metadata", this.state.data);
        }
        this.setState({ isOpen: false });
    }

    public static show() {
        BookMetadataDialog.singleton.setState({
            isOpen: true
        });
    }
    public render() {
        return (
            <CloseOnEscape
                onEscape={() => {
                    this.handleCloseModal(false);
                }}
            >
                <ReactModal
                    ariaHideApp={false} //we're not trying to make Bloom work with screen readers
                    className="bookMetadataDialog"
                    isOpen={this.state.isOpen}
                    shouldCloseOnOverlayClick={true}
                    onRequestClose={() => this.handleCloseModal(false)}
                >
                    <div className={"dialogTitle"}>Book Metadata</div>
                    <div className="dialogContent">
                        <BookMetadataTable data={this.state.data} />
                        <div className={"bottomButtonRow"}>
                            <button id="helpButton" disabled={true}>
                                Help
                            </button>
                            <button
                                id="okButton"
                                onClick={() => this.handleCloseModal(true)}
                            >
                                OK
                            </button>
                            <button
                                onClick={() => this.handleCloseModal(false)}
                            >
                                Cancel
                            </button>
                        </div>
                    </div>
                </ReactModal>
            </CloseOnEscape>
        );
    }
}
