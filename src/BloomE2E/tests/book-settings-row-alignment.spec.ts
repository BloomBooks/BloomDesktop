// BL-16957: in Book and Page Settings, a check box should line up with the first line of its label.

import * as fs from "fs";
import * as path from "path";
import { expect, test } from "../fixtures/bloomTest";
import { makeBookFromTemplate } from "../helpers/bookMaking";
import {
    cancelBookSettings,
    openBookSettings,
    showBookSettingsPage,
} from "../helpers/bookSettings";

test.use({
    collectionSpec: {
        name: "row-alignment",
        languages: ["en", "fr"],
    },
});

test.describe.configure({ mode: "serial" });

test.describe("Book Settings row alignment", () => {
    test("makes a book", async ({ page }) => {
        test.setTimeout(300000);
        await makeBookFromTemplate(page, "Basic Book");
    });

    test("check boxes line up with their labels", async ({
        page,
    }, testInfo) => {
        await openBookSettings(page, "Cover");
        const dialog = page.getByRole("dialog");
        const report: object[] = [];
        for (const pageName of ["Cover", "Print Publishing", "BloomPUB"]) {
            await showBookSettingsPage(page, pageName);
            const shot = path.join(
                testInfo.outputDir,
                `${pageName.replace(/ /g, "-")}.png`,
            );
            await dialog.screenshot({ path: shot });
            await testInfo.attach(pageName, {
                path: shot,
                contentType: "image/png",
            });
            const rows = await dialog
                .locator(".MuiListItemButton-root:has(.MuiCheckbox-root)")
                .evaluateAll((els) =>
                    els
                        .filter((el) => (el as HTMLElement).offsetParent)
                        .map((el) => {
                            const text = el.querySelector(
                                ".MuiListItemText-primary",
                            ) as HTMLElement;
                            const box = el.querySelector(
                                ".MuiCheckbox-root svg",
                            ) as HTMLElement;
                            const t = text.getBoundingClientRect();
                            const b = box.getBoundingClientRect();
                            const lineHeight = parseFloat(
                                getComputedStyle(text).lineHeight,
                            );
                            return {
                                label: text.innerText,
                                firstLineCenter: t.top + lineHeight / 2,
                                boxCenter: b.top + b.height / 2,
                                offset: b.top + b.height / 2 - (t.top + lineHeight / 2),
                                html: el.outerHTML.slice(0, 3000),
                                styles: [el, ...Array.from(el.querySelectorAll("*"))]
                                    .slice(0, 12)
                                    .map((e) => {
                                        const s = getComputedStyle(e);
                                        return `${e.tagName}.${e.className}: display=${s.display} align-items=${s.alignItems} padding=${s.padding} margin=${s.margin} line-height=${s.lineHeight} height=${s.height}`;
                                    }),
                            };
                        }),
                );
            report.push({ pageName, rows });
        }
        const reportPath = path.join(testInfo.outputDir, "alignment.json");
        fs.writeFileSync(reportPath, JSON.stringify(report, undefined, 2));
        await testInfo.attach("alignment", {
            path: reportPath,
            contentType: "application/json",
        });
        await cancelBookSettings(page);
        for (const p of report as { rows: { label: string; offset: number }[] }[])
            for (const r of p.rows)
                expect(
                    Math.abs(r.offset),
                    `the check box for "${r.label}" is not level with its label`,
                ).toBeLessThan(3);    });
});
