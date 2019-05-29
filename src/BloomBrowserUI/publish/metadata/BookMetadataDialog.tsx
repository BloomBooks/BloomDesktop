import * as React from "react";
import * as ReactModal from "react-modal";
import "./BookMetadataDialog.less";
import CloseOnEscape from "react-close-on-escape";
import BookMetadataTable from "./BookMetadataTable";
import { BloomApi } from "../../utils/bloomApi";
import * as mobx from "mobx";
import * as mobxReact from "mobx-react";
import BloomButton from "../../react_components/bloomButton";
import { Div } from "../../react_components/l10nComponents";

// tslint:disable-next-line:no-empty-interface
interface IState {
    isOpen: boolean;
}

// @observer means mobx will automatically track which observables this component uses
// in its render() function, and then re-render when they change.
@mobxReact.observer
export default class BookMetadataDialog extends React.Component<{}, IState> {
    private static singleton: BookMetadataDialog;
    public readonly state: IState = { isOpen: false };

    // We want mobx to watch this, because we will pass it to the BookMetadataTable, which can change it.
    @mobx.observable
    private metadata: any;

    // We will also pass this to the BookMetadataTable, but mobx doesn't need to watch it, since it won't change.
    private translatedControlStrings: any;

    constructor(props) {
        super(props);
        BookMetadataDialog.singleton = this;
    }

    private handleCloseModal(doSave: boolean) {
        if (doSave) {
            BloomApi.postData("book/metadata", this.metadata);
            BloomApi.post("publish/epub/updatePreview");
        }
        this.setState({ isOpen: false });
    }

    public static show() {
        BloomApi.get("book/metadata", result => {
            BookMetadataDialog.singleton.metadata = result.data.metadata;
            BookMetadataDialog.singleton.translatedControlStrings =
                result.data.translatedStringPairs;

            BookMetadataDialog.singleton.setState({
                isOpen: true
            });
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
                    shouldCloseOnOverlayClick={false}
                    onRequestClose={() => this.handleCloseModal(false)}
                >
                    <Div
                        className={"dialogTitle"}
                        l10nKey="PublishTab.BookMetadata"
                    >
                        Book Metadata
                    </Div>
                    <div className="dialogContent">
                        <BookMetadataTable
                            metadata={this.metadata}
                            translatedControlStrings={
                                this.translatedControlStrings
                            }
                        />
                        <div className={"bottomButtonRow"}>
                            <BloomButton
                                id="helpButton"
                                enabled={true}
                                l10nKey="Common.Help"
                                clickApiEndpoint="help/User_Interface/Dialog_boxes/Book_Metadata_dialog_box.htm"
                                hasText={true}
                            >
                                Help
                            </BloomButton>
                            <BloomButton
                                id="okButton"
                                enabled={true}
                                l10nKey="Common.OK"
                                hasText={true}
                                onClick={() => this.handleCloseModal(true)}
                            >
                                OK
                            </BloomButton>
                            <BloomButton
                                enabled={true}
                                l10nKey="Common.Cancel"
                                hasText={true}
                                onClick={() => this.handleCloseModal(false)}
                            >
                                Cancel
                            </BloomButton>
                        </div>
                    </div>
                </ReactModal>
            </CloseOnEscape>
        );
    }
}
