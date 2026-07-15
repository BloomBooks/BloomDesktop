import { FunctionComponent, useState } from "react";
import { BloomCheckbox } from "../../../react_components/BloomCheckBox";
import { isToolEnabledInToolbox, setToolEnabledFromSettings } from "../toolbox";
import { css } from "@emotion/react";
import { SubscriptionBadgeWithTooltipAndDialog } from "../../../react_components/requiresSubscription";
import { ThemeProvider } from "@mui/material/styles";
import { toolboxTheme } from "../../../bloomMaterialUITheme";

const ToolboxCheckbox: FunctionComponent<{
    tool: string;
    l10nKeySuffix: string;
    toolLabel: string;
    requiresSubscription?: boolean;
}> = (props) => {
    const [checked, setChecked] = useState<boolean>(
        isToolEnabledInToolbox(props.tool),
    );
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
                checked={checked}
                onCheckChanged={(checked) => {
                    // Pass true so that, when enabling, the tool opens after a
                    // brief delay letting the user see this checkbox tick before
                    // the "More..." section collapses to reveal the tool. (BL-16501)
                    setToolEnabledFromSettings(props.tool, checked!, true);
                    setChecked(checked!);
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
    return (
        <ThemeProvider theme={toolboxTheme}>
            <div
                css={css`
                    margin-top: 6px;
                `}
            >
                <ToolboxCheckbox
                    tool="canvas"
                    l10nKeySuffix="CanvasTool"
                    toolLabel="Canvas Tool"
                    requiresSubscription
                />
                <ToolboxCheckbox
                    tool="decodableReader"
                    l10nKeySuffix="DecodableReaderTool"
                    toolLabel="Decodable Reader Tool"
                />
                <ToolboxCheckbox
                    tool="imageDescription"
                    l10nKeySuffix="ImageDescriptionTool"
                    toolLabel="Image Description Tool"
                />
                <ToolboxCheckbox
                    tool="impairmentVisualizer"
                    l10nKeySuffix="ImpairmentVisualizer"
                    toolLabel="Impairment Visualizer"
                />
                <ToolboxCheckbox
                    tool="leveledReader"
                    l10nKeySuffix="LeveledReaderTool"
                    toolLabel="Leveled Reader Tool"
                />
                <ToolboxCheckbox
                    tool="motion"
                    l10nKeySuffix="MotionTool"
                    toolLabel="Motion Tool"
                    requiresSubscription
                />
                <ToolboxCheckbox
                    tool="music"
                    l10nKeySuffix="MusicTool"
                    toolLabel="Music Tool"
                    requiresSubscription
                />
                <ToolboxCheckbox
                    tool="signLanguage"
                    l10nKeySuffix="SignLanguageTool"
                    toolLabel="Sign Language Tool"
                />
            </div>
        </ThemeProvider>
    );
};
