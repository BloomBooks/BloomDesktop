import * as React from "react";
import Dialog from "@material-ui/core/Dialog";
import "./BookMetadataDialog.less";
import CloseOnEscape from "react-close-on-escape";
import BookMetadataTable from "./BookMetadataTable";
import { BloomApi } from "../../utils/bloomApi";
import * as mobx from "mobx";
import * as mobxReact from "mobx-react";
import BloomButton from "../../react_components/bloomButton";
import { Div } from "../../react_components/l10nComponents";
import { useL10n } from "../../react_components/l10nHooks";
import { DialogTitle, DialogActions, DialogContent } from "@material-ui/core";
import { String } from "../../react_components/l10nComponents";
// tslint:disable-next-line:no-empty-interface
interface IState {
    isOpen: boolean;
}

// @observer means mobx will automatically track which observables this component uses
// in its render() function, and then re-render when they change.
@mobxReact.observer
export default class BookMetadataDialog extends React.Component<
    { startOpen?: boolean },
    IState
> {
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
    public componentDidMount() {
        if (this.props.startOpen) {
            BookMetadataDialog.show();
        }
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
                <Dialog className="bookMetadataDialog" open={this.state.isOpen}>
                    <DialogTitle>
                        <String
                            l10nKey="PublishTab.BookMetadata"
                            l10nComment="title of metadata dialog box"
                        >
                            Book Metadata
                        </String>
                    </DialogTitle>
                    <DialogContent>
                        <BookMetadataTable
                            metadata={this.metadata}
                            translatedControlStrings={
                                this.translatedControlStrings
                            }
                        />
                    </DialogContent>
                    <DialogActions>
                        <BloomButton
                            id="helpButton"
                            variant="outlined"
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
                            variant="outlined"
                            l10nKey="Common.Cancel"
                            hasText={true}
                            onClick={() => this.handleCloseModal(false)}
                        >
                            Cancel
                        </BloomButton>
                    </DialogActions>
                </Dialog>
            </CloseOnEscape>
        );
    }
}
