import * as React from "react";
import BloomButton from "../../../react_components/bloomButton";
import {
    ImportIcon,
    UseTimingsFileIcon,
    InsertSegmentMarkerIcon
} from "./TalkingBookToolboxIcons";
import { postJson } from "../../../utils/bloomApi";

export const TalkingBookAdvancedButtons: React.FunctionComponent<{
    hasAudio: boolean;
    wholeTextBoxMode: boolean;
    hasRecordableDivs: boolean;
    handleImportRecordingClick: () => void;
    split: (timingFilePath: string) => Promise<void>;
}> = props => {
    return (
        <React.Fragment>
            <BloomButton
                id="import-recording-button"
                iconBeforeText={React.createElement(ImportIcon)}
                hasText={true}
                variant="outlined"
                size="small"
                enabled={props.wholeTextBoxMode && props.hasRecordableDivs}
                l10nKey="EditTab.Toolbox.TalkingBookTool.ImportRecording"
                onClick={() => props.handleImportRecordingClick()}
            />
            <BloomButton
                id="split-with-timings-file-button"
                iconBeforeText={React.createElement(UseTimingsFileIcon)}
                hasText={true}
                variant="outlined"
                size="small"
                enabled={props.wholeTextBoxMode && props.hasAudio}
                l10nKey="EditTab.Toolbox.TalkingBookTool.UseTimingsFile"
                onClick={async () => {
                    const result = await postJson("fileIO/chooseFile", {
                        title: "Choose Audacity Timing File",
                        fileTypes: [
                            {
                                name: "Audacity Timing File",
                                extensions: ["txt"]
                            }
                        ]
                    });
                    if (!result || !result.data) {
                        return;
                    }
                    props.split(result.data);
                }}
            />
            <BloomButton
                id="insert-segment-marker-button"
                iconBeforeText={React.createElement(InsertSegmentMarkerIcon)}
                hasText={true}
                variant="outlined"
                size="small"
                enabled={props.wholeTextBoxMode && props.hasRecordableDivs}
                l10nKey="EditTab.Toolbox.TalkingBookTool.InsertSegmentMarker"
                onClick={() => {}} // TODO
            />
        </React.Fragment>
    );
};
