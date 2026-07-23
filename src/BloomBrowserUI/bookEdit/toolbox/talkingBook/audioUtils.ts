// taken out of audioRecording.ts to avoid the need for other
// files to import that big file just to use a little bit of
// code.

import axios, { AxiosResponse } from "axios";

export const kAnyRecordingApiUrl = "/bloom/api/audio/checkForAnyRecording?ids=";

export async function audioExistsForIdsAsync(ids: string[]): Promise<boolean> {
    try {
        const response: AxiosResponse<any> = await axios.get(
            `${kAnyRecordingApiUrl}${ids}`,
        );
        return doesNarrationExist(response);
    } catch {
        return false;
    }
}

// Given a response from kAnyRecordingApiUrl, determines whether narration audio exists
// for any of the specified IDs.
export function doesNarrationExist(response: AxiosResponse<any>): boolean {
    return response && response.data === true;
}
