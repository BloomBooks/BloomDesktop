import { Page } from "playwright/test";
import { setTestComponent } from "../../component-tester/setTestComponent";
import { IRegistrationContentsProps } from "../registrationContents";

export async function setupRegistrationComponent(
    page: Page,
    props: IRegistrationContentsProps,
) {
    await setTestComponent<IRegistrationContentsProps>(
        page,
        "RegistrationContents",
        {
            onSubmit: (updated) => console.log("Submitted:", updated),
            ...props,
        },
    );
}
