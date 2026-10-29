import { isValidEmail } from "../utils/emailUtils";

/**
 * Whether a list of administrators, separated by commas or spaces, is acceptable: at least one,
 * and every one a valid email address. CollectionSettings.ValidateAdministrators in C# has the
 * same rule.
 */
export function administratorsAreValid(administrators: string): boolean {
    const list = administrators.split(/[, ]/).filter((a) => a !== "");
    return list.length > 0 && list.every(isValidEmail);
}
