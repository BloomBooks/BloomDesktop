// Open a book by title in the Edit tab of the running Bloom: switch to the Collections tab, click
// the book, switch to Edit. Usage: node gotoBook.mjs "A house for mouse"
import { execFileSync } from "node:child_process";
import path from "node:path";
import { connect, mainPage, ports, repoRoot } from "./cdp.mjs";
const title = process.argv[2];
if (!title) throw new Error("usage: node gotoBook.mjs <book title>");
const { httpPort } = ports();
const sw = path.join(
    repoRoot,
    ".github/skills/bloom-automation/switchWorkspaceTab.mjs",
);
const tab = (name) =>
    execFileSync(
        "node",
        [sw, "--http-port", String(httpPort), "--tab", name, "--json"],
        { stdio: "ignore" },
    );
tab("collection");
await new Promise((r) => setTimeout(r, 2500));
{
    const { browser, pages } = await connect();
    const p = mainPage(pages);
    const loc = p.getByText(title, { exact: true });
    const count = await loc.count();
    if (count === 0)
        throw new Error(
            `no book titled '${title}' visible in the collection tab`,
        );
    await loc.first().click();
    await p.waitForTimeout(1500);
    await browser.close();
}
tab("edit");
await new Promise((r) => setTimeout(r, 4000));
console.log("opened", title, "in Edit");
