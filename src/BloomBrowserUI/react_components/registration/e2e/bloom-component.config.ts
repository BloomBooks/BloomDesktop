import type { IBloomComponentConfig } from "../../component-tester/componentTypes";
import type { IRegistrationContentsProps } from "../registrationTypes";

const config: IBloomComponentConfig<IRegistrationContentsProps> = {
    defaultProps: {
        initialInfo: {
            firstName: "",
            surname: "",
            email: "",
            organization: "",
            usingFor: "",
            hadEmailAlready: false,
        },
        mayChangeEmail: true,
        emailRequiredForTeamCollection: false,
    },
    modulePath: "../registration/registrationContents",
    exportName: "RegistrationContents",
};

export default config;
