const kFunctionOnHintClickSuppressionMs = 300;

let suppressFunctionOnHintClickUntil = 0;

export function noteNonTrivialCanvasElementMove(): void {
    suppressFunctionOnHintClickUntil =
        Date.now() + kFunctionOnHintClickSuppressionMs;
}

export function shouldSuppressFunctionOnHintClick(): boolean {
    if (Date.now() <= suppressFunctionOnHintClickUntil) {
        return true;
    }
    return false;
}
