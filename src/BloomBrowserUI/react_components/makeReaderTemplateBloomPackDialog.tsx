import { css } from "@emotion/react";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle
} from "./BloomDialog/BloomDialog";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog
} from "./BloomDialog/BloomDialogPlumbing";
import {
    DialogCloseButton,
    DialogHelpButton
} from "./BloomDialog/commonDialogComponents";
import { useL10n } from "./l10nHooks";
import { Div } from "./l10nComponents";
import { get, post, useWatchApiData } from "../utils/bloomApi";
import * as React from "react";
import { useEffect, useState } from "react";
import LazyLoad from "react-lazyload";
import { Grid } from "@mui/material";
import { IBookInfo } from "../collectionsTab/BooksOfCollection";
import BloomButton from "./bloomButton";
import { Checkbox } from "./checkbox";

export const MakeReaderTemplateBloomPackDialog: React.FunctionComponent<{
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);
    showMakeReaderTemplateBloomPackDialog = showDialog;
    return (
        <BloomDialog {...propsForBloomDialog}>
            {propsForBloomDialog.open && (
                <InnerMakeReaderTemplateBloomPackDialog
                    closeDialog={closeDialog}
                    propsForBloomDialog={propsForBloomDialog}
                />
            )}
        </BloomDialog>
    );
};

export const InnerMakeReaderTemplateBloomPackDialog: React.FunctionComponent<{
    closeDialog: () => void;
    propsForBloomDialog;
}> = props => {
    const [collectionLanguage, setCollectionLanguage] = useState("");
    useEffect(() => {
        get("settings/languageData", langData => {
            if (langData?.data) {
                setCollectionLanguage(langData.data.languageName);
            }
        });
    }, []);

    const unfilteredBooks = useWatchApiData<Array<IBookInfo>>(
        `collections/books`,
        [],
        "editableCollectionList",
        "unused" // we don't care about updates, so maybe we don't care about this?
    );

    const [saveConfirmationChecked, setConfirmationChecked] = useState(false);

    return (
        <BloomDialog {...props.propsForBloomDialog}>
            <DialogTitle
                title={useL10n(
                    "Make Reader Template Bloom Pack",
                    "ReaderTemplateBloomPackDialog.WindowTitle"
                )}
                preventCloseButton={false}
            />

            <DialogMiddle
                css={css`
                    width: 350px;
                `}
            >
                <Div
                    l10nKey={"ReaderTemplateBloomPackDialog.IntroLabel"}
                    css={css`
                        padding-bottom: 5px;
                    `}
                >
                    The following books will be made into templates:
                </Div>
                <div
                    tabIndex={0}
                    css={css`
                        flex-direction: column;
                        overflow-y: auto;
                        height: 150px;
                        border: solid;
                        border-width: thin;
                        padding: 5px;
                        white-space: nowrap;
                    `}
                >
                    <Grid
                        container={true}
                        spacing={0}
                        direction="column"
                        justifyContent="flex-start"
                        alignItems="flex-start"
                    >
                        {unfilteredBooks?.map(book => {
                            return (
                                <Grid
                                    item={true}
                                    className="booklist"
                                    key={book.id}
                                >
                                    <LazyLoad
                                        // Tells lazy loader to look for the parent element that has overflowY set to scroll or
                                        // auto. This requires a patch to react-lazyload (as of 3.2.0) because currently it looks for
                                        // a parent that has overflow:scroll or auto in BOTH directions, which is not what we're getting
                                        // from our splitter.
                                        // Note: using this is better than using splitContainer, because that has multiple bugs
                                        // that are not as easy to patch. See https://github.com/twobin/react-lazyload/issues/371.
                                        overflow={true}
                                        resize={true} // expand lazy elements as needed when container resizes
                                    >
                                        {book.title}
                                    </LazyLoad>
                                </Grid>
                            );
                        })}
                    </Grid>
                </div>
                <Div
                    l10nKey={
                        "ReaderTemplateBloomPackDialog.ExplanationParagraph"
                    }
                    l10nParam0={collectionLanguage}
                    css={css`
                        padding-top: 5px;
                    `}
                >
                    In addition, this Bloom Pack will carry your latest
                    decodable and leveled reader settings for the "{0}"
                    language. Anyone opening this Bloom Pack, who then opens a "
                    {0}" collection, will have their current decodable and
                    leveled reader settings replaced by the settings in this
                    Bloom Pack. They will also get the current set of words for
                    use in decodable readers.
                </Div>
                <Checkbox
                    id="saveConfirmation"
                    className="enable-checkbox"
                    l10nKey="ReaderTemplateBloomPackDialog.IUnderstandCheckboxLabel"
                    checked={saveConfirmationChecked}
                    css={css`
                        padding-top: 10px;
                    `}
                    onCheckChanged={() => {
                        setConfirmationChecked(!saveConfirmationChecked);
                    }}
                >
                    I understand what a template is and this is really what I
                    want to do
                </Checkbox>
            </DialogMiddle>
            <DialogBottomButtons>
                <BloomButton
                    className="saveButton"
                    id="saveButton"
                    l10nKey="ReaderTemplateBloomPackDialog.SaveBloomPackButton"
                    hasText={true}
                    enabled={saveConfirmationChecked}
                    variant={saveConfirmationChecked ? "contained" : "outlined"}
                    onClick={() => {
                        post("collections/makeBloompack");
                        props.closeDialog();
                    }}
                >
                    Save Bloom Pack
                </BloomButton>
                <DialogCloseButton
                    default={!saveConfirmationChecked}
                    onClick={props.closeDialog}
                />
                <DialogHelpButton helpId="Concepts/Bloom_Pack.htm" />
            </DialogBottomButtons>
        </BloomDialog>
    );
};

export let showMakeReaderTemplateBloomPackDialog: () => void = () => {
    window.alert("LanguageChooserDialog is not set up yet.");
};
