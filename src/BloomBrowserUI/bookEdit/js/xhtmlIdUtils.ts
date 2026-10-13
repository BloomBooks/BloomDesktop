// taken out of audioRecording.ts to avoid the need for other
// files to import that big file just to use a little bit of
// code.

import { EditableDivUtils } from "./editableDivUtils";

export function createValidXhtmlUniqueId(): string {
    let newId = EditableDivUtils.createUuid();
    if (/^\d/.test(newId)) {
        newId = "i" + newId; // valid ID in XHTML can't start with digit
    }

    return newId;
}
