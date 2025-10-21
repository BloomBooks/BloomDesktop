import { Page } from "playwright/test";
import { setTestComponent } from "../../component-tester/setTestComponent";
import { IRegistrationContentsProps } from "../registrationContents";

export async function setupRegistrationComponent(
    page: Page,
    props: IRegistrationContentsProps,
) {
    await setTestComponent<IRegistrationContentsProps>(
        page,
        "../registration/registrationContents",
        "RegistrationContents",
        props,
    );
}
