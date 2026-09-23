import { Page } from "../../component-tester/playwrightTest";
import { setTestComponent } from "../../component-tester/setTestComponent";
import { IProgressBoxProps } from "../progressBox";

export async function setupProgressBox(
    page: Page,
    props: IProgressBoxProps,
): Promise<void> {
    await setTestComponent<IProgressBoxProps>(
        page,
        "../Progress/progressBox",
        "TestableProgressBox",
        props,
    );
}

export function getProgressLog(page: Page) {
    return page.getByTestId("progress-box-log");
}
