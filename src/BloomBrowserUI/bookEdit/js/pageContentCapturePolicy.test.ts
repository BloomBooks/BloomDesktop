import { describe, expect, test } from "vitest";
import { kBackgroundConversionDelayId } from "../toolbox/canvas/canvasElementConstants";
import {
    externalCaptureErrorForPendingWork,
    kExternalCaptureMaxWaitMs,
} from "./pageContentCapturePolicy";

describe("externalCaptureErrorForPendingWork (BL-16870)", () => {
    test("refuses to capture while a background image conversion is still pending", () => {
        const error = externalCaptureErrorForPendingWork([
            "adjustBackgroundImageSize",
            kBackgroundConversionDelayId,
        ]);
        expect(error).toBeDefined();
        // BookProcessor.ProcessOnePage recognises a failed capture by this prefix.
        expect(error!.startsWith("ERROR:")).toBe(true);
        expect(error).toContain(String(kExternalCaptureMaxWaitMs));
        expect(error).toContain(kBackgroundConversionDelayId);
    });

    test("lets the capture proceed when only other kinds of work are still pending", () => {
        expect(
            externalCaptureErrorForPendingWork([
                "adjustBackgroundImageSize",
                "imageTooltip",
            ]),
        ).toBeUndefined();
    });

    test("lets the capture proceed when nothing is pending", () => {
        expect(externalCaptureErrorForPendingWork([])).toBeUndefined();
    });

    test("waits far longer than the live editor's 4 seconds but stays under the C# poll timeout", () => {
        // The live editor's cap is 4000 ms (bloomEditing.ts kMaxWaitTimeMs); BookProcessor polls for
        // our answer for 60000 ms. A regression in either direction would either give a slow image
        // no more grace than the live save does, or have C# time out before the browser answers.
        expect(kExternalCaptureMaxWaitMs).toBeGreaterThan(4000);
        expect(kExternalCaptureMaxWaitMs).toBeLessThan(60000);
    });
});
