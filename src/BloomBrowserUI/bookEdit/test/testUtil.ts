// A generic wrapper for Jasmine tests with a done() async callback
// Runs an Arrange-Act-Assert pattern test where Setup or Act may be asynchronous
// It is assumed that verify is synchronous.
// Handles properly wrapping try/catches and calling done() at the right places.
//
// Note: This wrapper is only necessary if you must have a done() based callback.
// However, if you're just using async/await or promises, this wrapper isn't necessary.
// You can just mark the "it" call's function as async and then await all the appropriate things.
// Jasmine will await your async it's completion before moving on to the next thing.
// Using just async/await is more recommended, cleaner, and less error-prone than using done callbacks.
// For more info: https://jasmine.github.io/tutorials/async
export async function runAsyncTest(
    done: () => void, // The done callback provided by Jasmine framework
    setupAsync: () => void | Promise<void>,
    runAsync: () => void | Promise<void>,
    verify: () => void
) {
    // For async tests, the whole thing should be wrapped in a try/catch to ensure that done() always gets called by the end, no matter what.
    try {
        await setupAsync();
        await runAsync();
        verify();
    } catch (error) {
        // Note: When run asynchronously, exceptions thrown don't fail tests in the same way that synchronous exceptions do.
        // Asynchronous ones usually just cause the test to time out).
        // So, we explicitly catch and fail the test so that the cause is more obvious than recognizing that timeout -> probably exception thrown.
        fail(error);
    } finally {
        done();
    }
}
