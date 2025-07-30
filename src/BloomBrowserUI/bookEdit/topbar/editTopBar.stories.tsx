import { useState } from "react";
import { BloomCheckbox } from "../../react_components/BloomCheckBox";
import { Meta, StoryObj } from "@storybook/react";
import { css } from "@emotion/react";
import {
    ContentLanguagesDropdown,
    CopyButton,
    CutButton,
    LayoutChoicesDropdown,
    PasteButton,
    UndoButton
} from "./editTopBarControls";

const meta: Meta = {
    title: "Edit Top Bar Controls"
};

export default meta;

type Story = StoryObj;

export const Copy: Story = {
    name: "CopyButton",
    render: () => {
        const [enabled, setEnabled] = useState(false);
        const l10nSet = {
            key: "EditTab.CopyButton",
            english: "Copy",
            comment: "Button to copy what is selected",
            localizedTip: "Copy (Ctrl+C)"
        };

        return (
            <div
                css={css`
                    display: grid;
                    grid-template-columns: auto;
                `}
            >
                <BloomCheckbox
                    label="Check to Enable the Copy Button"
                    l10nKey=""
                    checked={enabled}
                    onCheckChanged={() => setEnabled(!enabled)}
                />
                <CopyButton enabled={enabled} l10nSet={l10nSet} />
            </div>
        );
    }
};

export const Cut: Story = {
    name: "CutButton",
    render: () => {
        const [enabled, setEnabled] = useState(false);
        const l10nSet = {
            key: "EditTab.CutButton",
            english: "Cut",
            comment: "",
            localizedTip: "Cut (Ctrl+X)"
        };

        return (
            <div
                css={css`
                    display: grid;
                    grid-template-columns: auto;
                `}
            >
                <BloomCheckbox
                    label="Check to Enable the Cut Button"
                    l10nKey=""
                    checked={enabled}
                    onCheckChanged={() => setEnabled(!enabled)}
                />
                <CutButton enabled={enabled} l10nSet={l10nSet} />
            </div>
        );
    }
};

export const Paste: Story = {
    name: "PasteButton",
    render: () => {
        const [enabled, setEnabled] = useState(false);
        const l10nSet = {
            key: "EditTab.PasteButton",
            english: "Paste",
            comment: "Button to paste what is on the Clipboard.",
            localizedTip: "Paste (Ctrl+V)"
        };

        return (
            <div
                css={css`
                    display: grid;
                    grid-template-columns: auto;
                `}
            >
                <BloomCheckbox
                    label="Check to Enable the Paste Button"
                    l10nKey=""
                    checked={enabled}
                    onCheckChanged={() => setEnabled(!enabled)}
                />
                <PasteButton enabled={enabled} l10nSet={l10nSet} />
            </div>
        );
    }
};

export const Undo: Story = {
    name: "UndoButton",
    render: () => {
        const [enabled, setEnabled] = useState(false);
        const l10nSet = {
            key: "EditTab.UndoButton",
            english: "Undo",
            comment: "Button to undo last action",
            localizedTip: "Undo (Ctrl+Z)"
        };

        return (
            <div
                css={css`
                    display: grid;
                    grid-template-columns: auto;
                `}
            >
                <BloomCheckbox
                    label="Check to Enable the Undo Button"
                    l10nKey=""
                    checked={enabled}
                    onCheckChanged={() => setEnabled(!enabled)}
                />
                <UndoButton enabled={enabled} l10nSet={l10nSet} />
            </div>
        );
    }
};

export const ChooseLanguage: Story = {
    name: "ChooseLanguage",
    render: () => {
        const [enabled, setEnabled] = useState(false);

        return (
            <div
                css={css`
                    display: grid;
                    grid-template-columns: auto;
                `}
            >
                <BloomCheckbox
                    label="Check to Enable the Language Dropdown"
                    l10nKey=""
                    checked={enabled}
                    onCheckChanged={() => setEnabled(!enabled)}
                />
                <ContentLanguagesDropdown
                    enabled={enabled}
                    l10nSet={{
                        key: "EditTab.Monolingual",
                        english: "One Language",
                        comment:
                            "Shown in edit tab multilingualism chooser, for monolingual mode, one language per page",
                        localizedTip:
                            "Choose language to make this a bilingual or trilingual book"
                    }}
                />
            </div>
        );
    }
};

export const ChooseLayout: Story = {
    name: "ChooseLayout",
    render: () => {
        return (
            <div>
                <div>Always Enabled</div>
                <LayoutChoicesDropdown
                    localizedText="A5 Portrait"
                    localizedTooltip="Choose a page size and orientation"
                />
            </div>
        );
    }
};
