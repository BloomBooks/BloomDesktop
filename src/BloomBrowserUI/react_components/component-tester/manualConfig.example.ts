import { getComponentRequestByName } from "./component-registry";

const manualComponentName = "RegistrationContents";

const request = getComponentRequestByName(manualComponentName);

if (!request) {
    throw new Error(
        `Component "${manualComponentName}" was not found in the component registry.`,
    );
}

/**
 * Example manual component configuration.
 *
 * Copy this file to manualConfig.ts and swap out manualComponentName for the
 * component you want to render with `yarn dev`.
 */
export const manualComponent = request;
