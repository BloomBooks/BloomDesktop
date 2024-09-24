/** @jsx jsx **/
import { jsx, css } from "@emotion/react";

import * as React from "react";
import { useState } from "react";
import * as ReactDOM from "react-dom";

import { get, postBoolean, postString } from "../../utils/bloomApi";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogTitle,
    DialogMiddle
} from "../../react_components/BloomDialog/BloomDialog";
import {
    IBloomDialogEnvironmentParams,
    Mode,
    useSetupBloomDialog
} from "../../react_components/BloomDialog/BloomDialogPlumbing";
import { DialogCloseButton } from "../../react_components/BloomDialog/commonDialogComponents";
import { useL10n } from "../../react_components/l10nHooks";
import {
    FormControl,
    FormControlLabel,
    Radio,
    RadioGroup,
    Typography
} from "@mui/material";

interface ITopicChoice {
    englishKey: string;
    translated?: string;
}

interface ITopicChooserDialogProps {
    dialogEnvironment?: IBloomDialogEnvironmentParams;
    currentTopic?: string;
    availableTopics?: ITopicChoice[];
}

export const TopicChooserDialog: React.FunctionComponent<ITopicChooserDialogProps> = props => {
    const {
        showDialog,
        closeDialog,
        propsForBloomDialog
    } = useSetupBloomDialog(props.dialogEnvironment);

    // Configure the local function (`show`) for showing the dialog to be the one derived from useSetupBloomDialog (`showDialog`)
    // which allows js launchers of the dialog to make it visible (by calling showCopyrightAndLicenseInfoOrDialog)
    show = showDialog;

    const [currentTopic, setCurrentTopic] = useState<string | undefined>(
        props.currentTopic
    );

    const dialogTitle = useL10n("Choose Topic", "TopicChooser.Title");

    // Tell edit tab to disable everything when the dialog is up.
    // (Without this, the page list is not disabled since the modal
    // div only exists in the book pane. Once the whole edit tab is inside
    // one browser, this would not be necessary.)
    React.useEffect(() => {
        if (propsForBloomDialog.open === undefined) return;

        if (props.dialogEnvironment?.mode === Mode.Edit)
            postBoolean("editView/setModalState", propsForBloomDialog.open);
    }, [propsForBloomDialog.open, props.dialogEnvironment?.mode]);

    const onRadioSelectionChanged = (newTopicKey: string) => {
        setCurrentTopic(newTopicKey === "No Topic" ? undefined : newTopicKey);
    };

    const handleClose = () => {
        const topicKey = currentTopic ? currentTopic : "<NONE>";
        if (props.dialogEnvironment?.mode === Mode.Edit) {
            postString("editView/setTopic", topicKey);
        } else if (props.dialogEnvironment?.mode === Mode.Publish)
            postString("libraryPublish/topic", topicKey);
        closeDialog();
    };

    const isTopicChecked = (choice: ITopicChoice): boolean => {
        if (!currentTopic && choice.englishKey === "No Topic") {
            return true;
        }
        return choice.englishKey === currentTopic;
    };

    const fontWeightForChoice = (choice: ITopicChoice): "bold" | "normal" => {
        return isTopicChecked(choice) ? "bold" : "normal";
    };

    const topicRadioButtonList = (): JSX.Element[] => {
        if (!props.availableTopics) {
            return [];
        }
        return props.availableTopics.map((choice, index) => (
            <FormControlLabel
                css={css`
                    display: flex !important; // override MUI inline-flex
                `}
                key={index}
                value={choice.englishKey}
                label={
                    <Typography
                        style={{
                            fontWeight: fontWeightForChoice(choice),
                            fontSize: 16,
                            marginLeft: 6,
                            marginBlockEnd: 0
                        }}
                    >
                        {choice.translated
                            ? choice.translated
                            : choice.englishKey}
                    </Typography>
                }
                control={
                    <Radio
                        css={css`
                            // Need a slightly larger radio button, but more limited vertical padding.
                            padding: 2px 9px !important;
                            svg {
                                color: ${kBloomBlue};
                                height: 1.7rem;
                                width: 1.7rem;
                            }
                        `}
                        size="medium"
                        color="primary"
                        checked={isTopicChecked(choice)}
                        onChange={event =>
                            onRadioSelectionChanged(event.target.value)
                        }
                    />
                }
            />
        ));
    };

    return (
        <BloomDialog
            {...propsForBloomDialog}
            css={css`
                padding-left: 18px;
                .MuiDialog-paperWidthSm {
                    max-width: 720px;
                }
            `}
        >
            <DialogTitle title={dialogTitle} />
            <DialogMiddle
                css={css`
                    // the width will grow automatically as needed for localizations
                    height: 315px;
                `}
            >
                <FormControl>
                    <RadioGroup
                        name="topic-radio-group"
                        css={css`
                            display: block !important; // override MUI inline-flex. With Block, we can get columns
                            column-count: 2;
                        `}
                    >
                        {topicRadioButtonList()}
                    </RadioGroup>
                </FormControl>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogCloseButton
                    onClick={handleClose}
                    css={css`
                        font-size: 16px !important; // override MUI default
                        width: 150px;
                    `}
                />
            </DialogBottomButtons>
        </BloomDialog>
    );
};

let show: () => void = () => {
    window.alert("TopicChooserDialog is not set up yet.");
};

export function showTopicChooserDialog(mode: Mode = Mode.Edit) {
    try {
        get("editView/topics", result => {
            const topicsAndCurrent = result.data;
            const topics = (topicsAndCurrent.Topics as string[]).map(t =>
                JSON.parse(t)
            );
            const currentTopic =
                topicsAndCurrent.Current === "No Topic"
                    ? undefined
                    : topicsAndCurrent.Current;

            // Here, topics will be an array with an entry for each known topic. Each topic is an
            // englishKey/translated pair.
            if (topics) {
                ReactDOM.render(
                    <TopicChooserDialog
                        currentTopic={currentTopic}
                        availableTopics={topics}
                        dialogEnvironment={{
                            dialogFrameProvidedExternally: false,
                            initiallyOpen: false,
                            mode
                        }}
                    />,
                    getModalContainer()
                );
                show();
            }
        });
    } catch (error) {
        console.error(error);
    }
}

// It would be simpler to just use getEditTabBundleExports().getModalDialogContainer()
// but we were getting strange interactions between this component and others which use that container.
// We were also having trouble rendering this component more than once for two different book pages.
// So we just always use our own, new, unique container.
function getModalContainer(): HTMLElement {
    let modalDialogContainer = document.getElementById(
        "TopicChooserDialogContainer"
    );
    if (modalDialogContainer) {
        modalDialogContainer.remove();
    }
    modalDialogContainer = document.createElement("div");
    modalDialogContainer.id = "TopicChooserDialogContainer";
    document.body.appendChild(modalDialogContainer);
    return modalDialogContainer;
}
