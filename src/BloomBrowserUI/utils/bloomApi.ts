import axios, { AxiosResponse, AxiosRequestConfig, AxiosPromise } from "axios";


export class BloomApi {
    // This function is designed to be used lilke this:
    // BloomApi.wrapAxios(axios.{get, post, etc}().then(...));
    // That is, the argument should be an AxiosPromise;
    // typically the promise returned by a .then() clause.
    // Wrapping it in this function takes the place of writing
    // your own .catch clause, and provides better error reporting
    // if the server unexpectedly returns a failure result
    // (or an exception is thrown in the .then code)
    // You can also pass a get, post, etc. call without a .then().
    public static wrapAxios(call: AxiosPromise | Promise<void>) {
        // Save the place where the original axios call was made.
        // The stack in the error passed to catch is usually not very
        // useful, containing just a few levels from the axios code
        // that handle the rejection of a promise, thoug it might contain
        // useful information about a problem within the '.then' block.
        // The stack that we would get by throwing in the catch function
        // itself is even less useful, typically containing nothing but
        // this method. But the one we can make where this function is called
        // really tells us where the problem is. Unfortunately, to be able
        // to report it, we have to save it even if we don't need it.
        // But fortunately, axios calls tend not to be used for very
        // performance-critical calls.
        var fullError = new Error("dummy");
        call.catch(error => {
            // The error object we get from Axios is typically a Javascript
            // Error (with message and stack), and often also has a response
            // property, though we code defensively against the possibility
            // that it does not.
            if (error && error.message && error.stack) {
                fullError.message = "Unexpected promise failure: " + error.message;
                if (error.response && error.response.statusText) {
                    fullError.message += " (response: " + error.response.statusText;
                }
                fullError.stack += "\ninner exception:\n" + error.stack;
            } else {
                fullError.message = "Unexpected promise failure: " + error;
            }
            // until we have better error handling, we only seem to see the stack
            // at the point where we threw. Put the one we want into the message
            // so we will have it.
            fullError.message += "\n stack:\n" + fullError.stack;
            throw fullError;
        });
    }

    // This method is used to get a result from Bloom.
    public static get(urlSuffix: string, successCallback: (r: AxiosResponse) => void) {
        BloomApi.wrapAxios(axios.get("/bloom/" + urlSuffix).then(successCallback));
    }

    // This method is used to get a result from Bloom, passing paramaters to the nested axios call.
    public static getWithConfig(urlSuffix: string, config: AxiosRequestConfig, successCallback: (r: AxiosResponse) => void) {
        BloomApi.wrapAxios(axios.get("/bloom/" + urlSuffix, config).then(successCallback));
    }

    // This method is used to post something from Bloom.
    public static post(urlSuffix: string, successCallback?: (r: AxiosResponse) => void) {
        if (successCallback) {
            BloomApi.wrapAxios(axios.post("/bloom/" + urlSuffix).then(successCallback));
        } else {
            BloomApi.wrapAxios(axios.post("/bloom/" + urlSuffix));
        }
    }

    // This method is used to post something from Bloom with data.
    public static postData(urlSuffix: string, data: any, successCallback?: (r: AxiosResponse) => void) {
        if (successCallback) {
            BloomApi.wrapAxios(axios.post("/bloom/" + urlSuffix, data).then(successCallback));
        } else {
            BloomApi.wrapAxios(axios.post("/bloom/" + urlSuffix, data));
        }
    }

    // This method is used to post something from Bloom with data and params.
    public static postDataWithConfig(urlSuffix: string, data: any, config: AxiosRequestConfig,
        successCallback?: (r: AxiosResponse) => void) {
        if (successCallback) {
            BloomApi.wrapAxios(axios.post("/bloom/" + urlSuffix, data, config).then(successCallback));
        } else {
            BloomApi.wrapAxios(axios.post("/bloom/" + urlSuffix, data, config));
        }
    }
}