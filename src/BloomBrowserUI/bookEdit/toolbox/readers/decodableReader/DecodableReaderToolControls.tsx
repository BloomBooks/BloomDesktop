import { getTheOneReaderToolsModel, MarkupType } from "../readerToolsModel";
import { ReaderToolSwitch } from "../ReaderToolSwitch";

const SetUpStages: React.FunctionComponent = () => {
    return (
        <div id="setupStages">
            <img
                src="/bloom/bookEdit/toolbox/readers/edit-white.png"
                id="decodable-edit"
            ></img>
            <a
                href="javascript:window.toolboxBundle.showSetupDialog('stages');"
                data-i18n="EditTab.Toolbox.DecodableReaderTool.SetUpStages"
            >
                Set Up Stages
            </a>
        </div>
    );
};

const GenLetterWordList: React.FunctionComponent = () => {
    return (
        <div id="make-letter-word-list-div">
            <a
                href="javascript:toolboxBundle.makeLetterWordList();"
                data-i18n="EditTab.Toolbox.DecodableReaderTool.MakeLetterWordReport"
                id="make-letter-word-list"
            >
                Generate a letter and word list report
            </a>
        </div>
    );
};

const CurrentStage: React.FunctionComponent = () => {
    const model = getTheOneReaderToolsModel();
    return (
        <div>
            <p id="stageNofM">St</p>
        </div>
    );
};

const DecReaderToggle: React.FunctionComponent = () => {
    return <ReaderToolSwitch isForLeveled={false} />;
};

export const DecodableReaderToolControls: React.FunctionComponent = () => {
    return (
        <div className="DecodableReaderToolControls">
            <SetUpStages />
            <GenLetterWordList />
            <CurrentStage />
            <DecReaderToggle />
        </div>
    );
};
