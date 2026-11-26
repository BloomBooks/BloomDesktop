// A person who has registered with Bloom does not have to say who they are again when they report
// a problem: Help > Report a Problem opens with their registered email address in its Email box,
// and the report that Bloom would send names them.
//
// The dialog has no box for the name. The name goes into the diagnostic information that Bloom
// sends with the report, which the dialog shows behind its Learn More button, so that is where the
// test reads it. Nothing is submitted.

import { expect, test } from "../fixtures/bloomTest";
import {
    expectReporterEmail,
    showReportDiagnosticInfo,
    withReportAProblemDialog,
} from "../helpers/problemReport";
import {
    getRegistration,
    withRegistration,
    type IRegistration,
} from "../helpers/registration";

test.use({
    collectionSpec: { name: "report-reporter-details", languages: ["en"] },
});

// Names and an address that nobody registers with, so finding them in the dialog shows that it
// read the registration this test made.
const kReporter: IRegistration = {
    firstName: "Prefilla",
    surname: "Reportson",
    email: "prefilla.reportson@example.com",
    organization: "Bloom E2E",
    usingFor: "Testing",
};

test("Report a Problem opens with the registered email and name filled in [Test Case ID 43]", async ({
    page,
    problemDialogWatcher,
    step,
}) => {
    const before = await getRegistration(page);

    await withRegistration(page, kReporter, async () => {
        await withReportAProblemDialog(
            page,
            problemDialogWatcher,
            async (dialog) => {
                await step(
                    "Check that the Email box holds the registered address",
                    () => expectReporterEmail(dialog, kReporter.email),
                );
                await step(
                    "Check that the report names the registered person",
                    async () => {
                        const report = await showReportDiagnosticInfo(dialog);
                        expect(report).toContain(kReporter.firstName);
                        expect(report).toContain(kReporter.surname);
                    },
                );
            },
        );
    });

    expect(
        await getRegistration(page),
        "the registration that the machine had before the test",
    ).toEqual(before);
});
