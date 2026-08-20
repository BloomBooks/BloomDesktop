import { FunctionComponent, useState } from "react";
import { BloomCheckbox } from "../../../react_components/BloomCheckBox";
import {
    isToolEnabledInToolbox,
    setToolEnabledFromSettings,
    setToolboxSettingsChangeHandler,
} from "../toolbox";
import { css } from "@emotion/react";
import { SubscriptionBadgeWithTooltipAndDialog } from "../../../react_components/requiresSubscription";
import { ThemeProvider } from "@mui/material/styles";
import { toolboxTheme } from "../../../bloomMaterialUITheme";
import { useMountEffect } from "../../../utils/useMountEffect";

const ToolboxCheckbox: FunctionComponent<{
    tool: string;
    l10nKeySuffix: string;
    toolLabel: string;
    shouldCheck: boolean;
    requiresSubscription?: boolean;
}> = (props) => {
    return (
        <div
            css={css`
                display: flex;
                align-items: center;
                justify-content: space-between;
                margin-left: 10px;
                margin-right: 10px;
                margin-bottom: -6px;
                padding-top: 0;
            `}
        >
            <BloomCheckbox
                css={css`
                    color: white;
                    border-radius: 0;
                    padding-right: 0;
                    margin-right: 5px;
                    margin-top: 1px;
                `}
                size="small"
                label={props.toolLabel}
                l10nKey={`EditTab.Toolbox.${props.l10nKeySuffix}`}
                checked={props.shouldCheck}
                onCheckChanged={(checked) => {
                    // Pass true so that, when enabling, the tool opens after a
                    // brief delay letting the user see this checkbox tick before
                    // the "More..." section collapses to reveal the tool. (BL-16501)
                    setToolEnabledFromSettings(props.tool, checked!, true);
                }}
            />
            {props.requiresSubscription && (
                <div
                    css={css`
                        display: inline-flex;
                        padding-top: 10px;
                    `}
                >
                    <SubscriptionBadgeWithTooltipAndDialog
                        featureName={props.tool}
                    />
                </div>
            )}
        </div>
    );
};

export const SettingsToolControls: FunctionComponent = () => {
    const kToolDefs = [
        {
            tool: "canvas",
            l10nKeySuffix: "CanvasTool",
            toolLabel: "Canvas Tool",
            requiresSubscription: true,
        },
        {
            tool: "decodableReader",
            l10nKeySuffix: "DecodableReaderTool",
            toolLabel: "Decodable Reader Tool",
        },
        {
            tool: "imageDescription",
            l10nKeySuffix: "ImageDescriptionTool",
            toolLabel: "Image Description Tool",
        },
        {
            tool: "impairmentVisualizer",
            l10nKeySuffix: "ImpairmentVisualizer",
            toolLabel: "Impairment Visualizer",
        },
        {
            tool: "leveledReader",
            l10nKeySuffix: "LeveledReaderTool",
            toolLabel: "Leveled Reader Tool",
        },
        {
            tool: "motion",
            l10nKeySuffix: "MotionTool",
            toolLabel: "Motion Tool",
            requiresSubscription: true,
        },
        {
            tool: "music",
            l10nKeySuffix: "MusicTool",
            toolLabel: "Music Tool",
            requiresSubscription: true,
        },
        {
            tool: "signLanguage",
            l10nKeySuffix: "SignLanguageTool",
            toolLabel: "Sign Language Tool",
        },
    ];
    const [checkedState, setCheckedState] = useState<Record<string, boolean>>(
        () =>
            Object.fromEntries(
                kToolDefs.map((t) => [t.tool, isToolEnabledInToolbox(t.tool)]),
            ),
    );

    function updateState(which: string, value: boolean): void {
        setCheckedState((previous) => ({ ...previous, [which]: value }));
    }

    useMountEffect(() => {
        setToolboxSettingsChangeHandler((which, value) =>
            updateState(which, value),
        );
        return () => {
            setToolboxSettingsChangeHandler(undefined);
        };
    });
    return (
        <ThemeProvider theme={toolboxTheme}>
            <div
                css={css`
                    margin-top: 6px;
                `}
            >
                {kToolDefs.map((t) => (
                    <ToolboxCheckbox
                        key={t.tool}
                        {...t}
                        shouldCheck={!!checkedState[t.tool]}
                    />
                ))}
            </div>
        </ThemeProvider>
    );
};
