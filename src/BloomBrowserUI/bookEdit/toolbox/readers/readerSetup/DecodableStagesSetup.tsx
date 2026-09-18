import { css } from "@emotion/react";
import {
    Tab,
    Tabs,
} from "@mui/material";
import * as React from "react";
import {
    useState,
} from "react";
import { useL10n } from "../../../../react_components/l10nHooks";
import {
    ReaderSettings,
} from "../ReaderSettings";
import {
    kBloomBlue,
} from "../../../../utils/colorUtils";
import { LettersTab } from "./LettersTab";
import { SampleWordsTab } from "./SampleWordsTab";
import { StagesTab } from "./StagesTab";

export const DecodableStagesSetup: React.FunctionComponent<{
    settings: ReaderSettings;
    setSettings: React.Dispatch<React.SetStateAction<ReaderSettings>>;
    fontName: string;
    maxAllowedWords: number;
    // The tab lives in the dialog above us because the Help button, which is down in the
    // dialog's button row, has to open the help page for whichever tab is showing.
    curTab: number;
    setCurTab: (value: number) => void;
}> = (props) => {
    const curTab = props.curTab;
    const setCurTab = props.setCurTab;
    const [selectedStageIndex, setSelectedStageIndex] = useState(0);

    const lettersTab = useL10n("Letters", "ReaderSetup.Letters");
    const sampleWordsTab = useL10n("Sample Words", "ReaderSetup.SampleWords");
    const stagesTab = useL10n(
        "Decodable Stages",
        "ReaderSetup.DecodableStages",
    );
    const tabsLabel = useL10n(
        "Set up Decodable Reader Tool",
        "ReaderSetup.SetUpDecodableReaderTool",
    );

    let activeTab: React.ReactNode;
    if (curTab === 0) {
        activeTab = (
            <LettersTab
                settings={props.settings}
                setSettings={props.setSettings}
                fontName={props.fontName}
            />
        );
    } else if (curTab === 1) {
        activeTab = (
            <SampleWordsTab
                settings={props.settings}
                setSettings={props.setSettings}
                fontName={props.fontName}
            />
        );
    } else {
        activeTab = (
            <StagesTab
                settings={props.settings}
                setSettings={props.setSettings}
                setCurTab={setCurTab}
                fontName={props.fontName}
                curStageIndex={selectedStageIndex}
                setCurStageIndex={setSelectedStageIndex}
                maxAllowedWords={props.maxAllowedWords}
            />
        );
    }

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                flex: 1 1 auto;
                height: 100%;
                min-height: 0;
                margin: -20px -24px;
                background: #f4f5f5;
            `}
        >
            <Tabs
                value={curTab}
                // onChange (rather than onClick on each Tab) is what lets MUI's own
                // arrow-key navigation actually change tabs, not just move focus.
                onChange={(_event, newTab: number) => setCurTab(newTab)}
                aria-label={tabsLabel}
                css={css`
                    min-height: 50px;
                    padding: 0 6px;
                    background: white;
                    border-bottom: 1px solid #e5e5e5;
                    .MuiTab-root {
                        min-height: 50px;
                        min-width: 0;
                        padding: 0 22px;
                        font-size: 15px;
                        text-transform: none;
                        font-weight: 600;
                    }
                    .Mui-selected {
                        color: ${kBloomBlue} !important;
                    }
                    .MuiTabs-indicator {
                        background-color: ${kBloomBlue};
                    }
                `}
            >
                <Tab
                    label={lettersTab}
                    data-testid="reader-setup-tab-letters"
                />
                <Tab
                    label={sampleWordsTab}
                    data-testid="reader-setup-tab-sample-words"
                />
                <Tab label={stagesTab} data-testid="reader-setup-tab-stages" />
            </Tabs>
            <div
                css={css`
                    flex: 1 1 auto;
                    min-height: 0;
                    margin: 24px;
                    background: white;
                    border: 1px solid #dddddd;
                    border-radius: 8px;
                    box-shadow: 0 1px 2px rgb(0 0 0 / 8%);
                    overflow: hidden;
                `}
            >
                {activeTab}
            </div>
        </div>
    );
};
