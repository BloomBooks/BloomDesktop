// For the Talking Book unit tests: utils/bloomApi with the two calls the recording code makes
// as it starts up answered locally. Unit tests must not reach the network (see
// vitest.setup.ts). Use it from a spec as
//
//   vi.mock("../../../utils/bloomApi", async (importOriginal) =>
//       (await import("./talkingBookTestBloomApi")).withTalkingBookReplies(await importOriginal()),
//   );
//
// Everything else stays the real bloomApi.
import type { AxiosResponse } from "axios";
import type * as BloomApi from "../../../utils/bloomApi";

/**
 * Return `realBloomApi` with get("audio/devices") and
 * getWithPromise("talkingBook/defaultAudioRecordingMode") answered without a request. No
 * devices leaves the device menu alone, and "Sentence" is the mode the code falls back to when
 * it cannot ask, which is what these tests have always run with.
 */
export function withTalkingBookReplies(
    realBloomApi: typeof BloomApi,
): typeof BloomApi {
    return {
        ...realBloomApi,
        get: (urlSuffix, successCallback, errorCallback) => {
            if (urlSuffix === "audio/devices") {
                // Like the real call, answer on a later turn of the event loop.
                window.setTimeout(
                    () =>
                        successCallback({
                            data: {
                                devices: [],
                                productName: "",
                                genericName: "",
                            },
                        } as AxiosResponse),
                    0,
                );
                return;
            }
            realBloomApi.get(urlSuffix, successCallback, errorCallback);
        },
        getWithPromise: (urlSuffix) => {
            if (urlSuffix === "talkingBook/defaultAudioRecordingMode") {
                return Promise.resolve({ data: "Sentence" } as AxiosResponse);
            }
            return realBloomApi.getWithPromise(urlSuffix);
        },
    };
}
