/** @jsx jsx **/
import { jsx, css } from "@emotion/core";

import * as React from "react";
import { useState } from "react";
import * as ReactDOM from "react-dom";

import { BloomApi } from "../../utils/bloomApi";
import { kBloomBlue } from "../../bloomMaterialUITheme";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogTitle,
    DialogMiddle
} from "../../react_components/BloomDialog/BloomDialog";
import {
    IBloomDialogEnvironmentParams,
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
} from "@material-ui/core";

interface ITopicChoice {
    englishKey: string;
    translated?: string;
}

interface ITopicChooserdialogProps {
    dialogEnvironment?: IBloomDialogEnvironmentParams;
    currentTopic: string;
    availableTopics?: ITopicChoice[];
}

export const TopicChooserDialog: React.FunctionComponent<ITopicChooserdialogProps> = props => {
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

        BloomApi.postBoolean(
            "editView/setModalState",
            propsForBloomDialog.open
        );
    }, [propsForBloomDialog.open]);

    const onRadioSelectionChanged = (newTopicKey: string) => {
        setCurrentTopic(newTopicKey === "No Topic" ? undefined : newTopicKey);
    };

    const handleOk = () => {
        BloomApi.postString(
            "editView/setTopic",
            currentTopic ? currentTopic : "<NONE>"
        );
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
                    span:first-child {
                        // only the radio button should be blue
                        color: ${kBloomBlue};
                    }
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

    // If this is false, we get a react render loop and error.
    // Our theory is some kind of focus war based on enabling/disabling the buttons.
    const disableDragging = true;

    return (
        <BloomDialog
            {...propsForBloomDialog}
            disableDragging={disableDragging}
            css={css`
                padding-left: 18px;
            `}
        >
            <DialogTitle
                title={dialogTitle}
                disableDragging={disableDragging}
                color="white"
                backgroundColor={kBloomBlue}
                fontSize="28px"
            />
            <DialogMiddle
                css={css`
                    width: 280px; // Surprisingly this width controls the size of the entire dialog.
                    height: 575px;
                `}
            >
                <FormControl>
                    <RadioGroup name="topic-radio-group">
                        {topicRadioButtonList()}
                    </RadioGroup>
                </FormControl>
            </DialogMiddle>
            <DialogBottomButtons>
                <DialogCloseButton
                    onClick={handleOk}
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

export function showTopicChooserDialog() {
    try {
        BloomApi.get("editView/topics", result => {
            const topics = result.data;
            // Here, topics will be an object with a property for each known topic. Each property is a key:value pair
            // where the key is the English, and the value is the topic in the UI Language
            if (topics) {
                BloomApi.get("editView/currentTopic", topicResult => {
                    ReactDOM.render(
                        <TopicChooserDialog
                            currentTopic={topicResult.data}
                            availableTopics={topics}
                        />,
                        getModalContainer()
                    );
                    show();
                });
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
