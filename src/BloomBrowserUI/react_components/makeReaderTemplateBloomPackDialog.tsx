import { css } from "@emotion/react";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogBottomLeftButtons,
    DialogMiddle,
    DialogTitle,
} from "./BloomDialog/BloomDialog";
import {
    IBloomDialogEnvironmentParams,
    useSetupBloomDialog,
} from "./BloomDialog/BloomDialogPlumbing";
import {
    DialogCancelButton,
    DialogHelpButton,
} from "./BloomDialog/commonDialogComponents";
import { useL10n } from "./l10nHooks";
import { Div } from "./l10nComponents";
import { get, post } from "../utils/bloomApi";
import * as React from "react";
import { useEffect, useState } from "react";
import BloomButton from "./bloomButton";
import { BloomCheckbox } from "./BloomCheckBox";
import { CollectionBookList } from "../collection/collectionBookList";
import { IBookInfo } from "../collectionsTab/BooksOfCollection";
import { propsToClassKey } from "@mui/styles";

// We extract the core here so that we can avoid running most of the hook code when this dialog is not visible.
export const MakeReaderTemplateBloomPackDialog: React.FunctionComponent<{
    dialogEnvironment?: IBloomDialogEnvironmentParams;
}> = (props) => {
    const { showDialog, closeDialog, propsForBloomDialog } =
        useSetupBloomDialog(props.dialogEnvironment);
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
}> = (props) => {
    const [collectionLanguage, setCollectionLanguage] = useState("");
    useEffect(() => {
        get("settings/languageData", (langData) => {
            if (langData?.data) {
                setCollectionLanguage(langData.data.languageName);
            }
        });
    }, []);

    const [saveConfirmationChecked, setSaveConfirmationChecked] =
        useState(false);

    const [collectionHasBooks, setCollectionHasBooks] = useState(false);

    return (
        <BloomDialog
            {...props.propsForBloomDialog}
            onCancel={() => {
                props.closeDialog();
            }}
        >
            <DialogTitle
                title={useL10n(
                    "Make Reader Template Bloom Pack",
                    "ReaderTemplateBloomPackDialog.WindowTitle",
                )}
                preventCloseButton={true}
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
                <CollectionBookList
                    onBooksLoaded={(bookCollection: Array<IBookInfo>) => {
                        setCollectionHasBooks(bookCollection?.length > 0);
                    }}
                    css={css`
                        height: 150px;
                    `}
                />
                <Div
                    l10nKey={
                        "ReaderTemplateBloomPackDialog.ExplanationParagraph"
                    }
                    l10nParam0={collectionLanguage}
                    css={css`
                        padding-top: 20px;
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
                <div
                    css={css`
                        height: 54px;
                    `}
                ></div>
                <BloomCheckbox
                    label="I understand what a template is and this is really what I
                    want to do"
                    l10nKey="ReaderTemplateBloomPackDialog.IUnderstandCheckboxLabel"
                    checked={saveConfirmationChecked}
                    onCheckChanged={() => {
                        setSaveConfirmationChecked(!saveConfirmationChecked);
                    }}
                >
                    I understand what a template is and this is really what I
                    want to do
                </BloomCheckbox>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogBottomLeftButtons>
                    <DialogHelpButton helpId="Concepts/Bloom_Pack.htm" />
                </DialogBottomLeftButtons>
                <BloomButton
                    className="saveButton"
                    id="saveButton"
                    l10nKey="ReaderTemplateBloomPackDialog.SaveBloomPackButton"
                    hasText={true}
                    enabled={saveConfirmationChecked && collectionHasBooks}
                    variant={
                        saveConfirmationChecked && collectionHasBooks
                            ? "contained"
                            : "outlined"
                    }
                    onClick={() => {
                        post("collections/makeBloompack");
                        props.closeDialog();
                    }}
                >
                    Save Bloom Pack
                </BloomButton>
                <DialogCancelButton
                    default={!saveConfirmationChecked || !collectionHasBooks}
                />
            </DialogBottomButtons>
        </BloomDialog>
    );
};

export let showMakeReaderTemplateBloomPackDialog: () => void = () => {
    window.alert("MakeReaderTemplateBloomPackDialog is not set up yet.");
};
