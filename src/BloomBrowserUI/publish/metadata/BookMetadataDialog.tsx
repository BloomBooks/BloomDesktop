import * as React from "react";
import * as ReactModal from "react-modal";
import "./BookMetadataDialog.less";
import CloseOnEscape from "react-close-on-escape";

// tslint:disable-next-line:no-empty-interface
interface IProps {}
interface IState {
    isOpen: boolean;
}
export default class BookMetadataDialog extends React.Component<
    IProps,
    IState
> {
    private static singleton: BookMetadataDialog;
    public readonly state = { isOpen: false };
    constructor(props: IProps) {
        super(props);
        BookMetadataDialog.singleton = this;
    }
    public componentDidMount() {
        //todo: query the metadata json via some api
    }
    private handleCloseModal(doSave: boolean) {
        if (doSave) {
            //post the edit json back via some api
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
                    className="bookMetadataDialog"
                    isOpen={this.state.isOpen}
                    shouldCloseOnOverlayClick={true}
                >
                    <div className={"dialogTitle"}>Book Metadata</div>
                    <div className="dialogContent">
                        <div className="foobar">hello</div>
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
