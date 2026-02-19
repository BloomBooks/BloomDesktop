// Our custom all settled function
// From: https://dev.to/gokulkrishh/how-it-works-promise-allsettled-3dle
// Like Promise.allSettled, but that requires:
// 1) Upgrading our lib to es2020
// 2) Upgrading GeckoFx to 71
export function allPromiseSettled(promises) {
    // To store our results
    const results = Array(promises.length);

    // To keep track of how many promises resolved
    let counter = 0;

    // If not iterable throw an error
    if (!isIterable(promises)) {
        throw new Error(`${typeof promises} is not iterable`);
    }

    // Wrapping our iteration with Promise object
    // So that we can resolve and return the results on done.
    return new Promise((resolve) => {
        // Iterate the inputs
        promises.forEach((promise, index) => {
            // Wait for each promise to resolve
            return Promise.resolve(promise)
                .then((result) => {
                    counter++; // Increment counter

                    // Store status and result in same order
                    results[index] = { status: "fulfilled", value: result };

                    // If all inputs are settled, return the results
                    if (counter === promises.length) {
                        resolve(results);
                    }
                })
                .catch((err) => {
                    counter++; // Increment counter

                    // Store status and reason for rejection in same order
                    results[index] = { status: "rejected", reason: err };

                    // If all inputs are settled, return the results
                    if (counter === promises.length) {
                        resolve(results);
                    }
                });
        });
    });
}

function isIterable(value) {
    // If no argument is passed or === null
    if (arguments.length === 0 || value === null) {
        return false;
    }

    return typeof value[Symbol.iterator] === "function";
}

/**
 * Wraps window.setTimeout and returns a promise for when the callback is completely done.
 * (For async callbacks, "completely done" means awaiting the completion of the callback)
 * @param callback The function to execute after the delay. If {callback} returns a promise (e.g. is async), that promise will be awaited before this method resolves its own promise.
 * @delayInMs Optional. Same as "delay" in setTimeout
 * @args Optional. Same as "args" in setTimeout. Specifies the arguments to pass into the callback
 */
export function setTimeoutPromise(
    callback: (...args: any[]) => void | Promise<unknown>,
    delayInMs?: number | undefined,
    ...args: any[]
): Promise<void> {
    return new Promise<void>((resolve, reject) => {
        setTimeout(
            async (...args: any[]) => {
                try {
                    const result = callback(args);

                    if (result instanceof Promise) {
                        // Note: even though you can technically await it either way,
                        // awaiting a non-promise still gives up control to its caller,
                        // This is a deviation from synchronous code that might not matter or might make a huge difference
                        // I don't want to risk that, so we await only if it's necessary.
                        await result;
                    }
                    resolve();
                } catch (e) {
                    reject(e);
                }
            },
            delayInMs,
            args,
        );
    });
}
