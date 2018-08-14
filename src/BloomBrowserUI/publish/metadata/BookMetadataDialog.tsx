import * as React from "react";
import * as ReactModal from "react-modal";
import "./BookMetadataDialog.less";
import CloseOnEscape from "react-close-on-escape";
import BookMetadataTable from "./BookMetadataTable";
import { BloomApi } from "../../utils/bloomApi";
import * as mobx from "mobx";
import * as mobxReact from "mobx-react";

// tslint:disable-next-line:no-empty-interface
interface IProps {}

// @observer means mobx will automatically track which observables this component uses
// in its render() function, and then re-render when they change.
@mobxReact.observer
export default class BookMetadataDialog extends React.Component<IProps> {
    private static singleton: BookMetadataDialog;
    public readonly state = { isOpen: false };

    // we want mobx to watch this, because we will pass it to the BookMetadataTable, which can change it.
    @mobx.observable
    private metadata: any = { test: { type: "readOnlyText", value: "test" } };

    constructor(props: IProps) {
        super(props);
        BookMetadataDialog.singleton = this;
    }
    public componentDidMount() {
        BloomApi.get("book/metadata", result => {
            this.metadata = result.data;
        });
    }
    private handleCloseModal(doSave: boolean) {
        if (doSave) {
            BloomApi.postData("book/metadata", this.metadata);
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
                        <BookMetadataTable metadata={this.metadata} />
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
