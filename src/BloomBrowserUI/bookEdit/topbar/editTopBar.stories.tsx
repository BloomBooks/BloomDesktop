import { useState } from "react";
import { BloomCheckbox } from "../../react_components/BloomCheckBox";
import { Meta, StoryObj } from "@storybook/react-vite";
import { css } from "@emotion/react";
import {
    ContentLanguagesDropdown,
    CopyButton,
    CutButton,
    LayoutChoicesDropdown,
    PasteButton,
    UndoButton,
} from "./editTopBarControls";

const meta: Meta = {
    title: "Edit Top Bar Controls",
};

export default meta;

type Story = StoryObj;

export const Copy: Story = {
    name: "CopyButton",
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
                    label="Check to Enable the Copy Button"
                    l10nKey=""
                    checked={enabled}
                    onCheckChanged={() => setEnabled(!enabled)}
                />
                <CopyButton enabled={enabled} />
            </div>
        );
    },
};

export const Cut: Story = {
    name: "CutButton",
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
                    label="Check to Enable the Cut Button"
                    l10nKey=""
                    checked={enabled}
                    onCheckChanged={() => setEnabled(!enabled)}
                />
                <CutButton enabled={enabled} />
            </div>
        );
    },
};

export const Paste: Story = {
    name: "PasteButton",
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
                    label="Check to Enable the Paste Button"
                    l10nKey=""
                    checked={enabled}
                    onCheckChanged={() => setEnabled(!enabled)}
                />
                <PasteButton enabled={enabled} />
            </div>
        );
    },
};

export const Undo: Story = {
    name: "UndoButton",
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
                    label="Check to Enable the Undo Button"
                    l10nKey=""
                    checked={enabled}
                    onCheckChanged={() => setEnabled(!enabled)}
                />
                <UndoButton enabled={enabled} />
            </div>
        );
    },
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
                <ContentLanguagesDropdown enabled={enabled} number={1} />
            </div>
        );
    },
};

export const ChooseLayout: Story = {
    name: "ChooseLayout",
    render: () => {
        return (
            <div>
                <div>Always Enabled</div>
                <LayoutChoicesDropdown localizedText="A5 Portrait" />
            </div>
        );
    },
};
