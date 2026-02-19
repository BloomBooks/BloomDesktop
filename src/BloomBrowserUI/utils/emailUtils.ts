//Note: the "isemail" package was not compatible with geckofx 45, so I'm just going with regex
// from https://stackoverflow.com/a/46181/723299
// NB: should handle emails like 用户@例子.广告
const emailPattern =
    /^(([^<>()\[\]\.,;:\s@\"]+(\.[^<>()\[\]\.,;:\s@\"]+)*)|(\".+\"))@(([^<>()[\]\.,;:\s@\"]+\.)+[^<>()[\]\.,;:\s@\"]{2,})$/i;

export function isValidEmail(email: string): boolean {
    return emailPattern.test(email);
}
