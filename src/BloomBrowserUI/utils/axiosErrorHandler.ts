import axios from "axios";
// This function is designed to be used lilke this: 
// checkAxiosError(axios.{get, post, etc}().then(...));
// That is, the argument should be an AxiosPromise;
// typically the promise returned by a .then() clause.
// Wrapping it in this function takes the place of writing
// your own .catch clause, and provides better error reporting
// if the server unexpectedly returns a failure result.
// (I haven't declared call to be AxiosPromise because I can't figure out
// what to include to make Typescript recognize that class.)
// You can also pass a get, post, etc. call without a .then().

export function checkAxiosError(call) {
    // Save the place where the original axios call was made.
    // The stack in the error passed to catch is usually not very
    // useful, containing just a few levels from the axios code
    // that handle the rejection of a promise.
    // The stack that we would get by throwing in the catch function
    // itself is even less useful, typically containing nothing but
    // this method. But the one we can make where this function is called
    // really tells us where the problem is. Unfortunately, to be able
    // to report it, we have to save it even if we don't need it.
    // But fortunately, axios calls tend not to be used for very
    // performance-critical calls.
    var saveStack = new Error("dummy");
    call.catch(error => {
        // The error object we get from Axios is typically a Javascript
        // Error (with message and stack), and often also has a response
        // property, though we code defensively against the possibility
        // that it does not.
        throw new Error("Unexpected promise failure:\n   message: "
            + error.message
            + "\n   response: " + (error.response ? (error.response.statusText || "") : "")
            + "\n   error stack: " + error.stack
            + "\n   caller stack: " + saveStack.stack);
    })

}