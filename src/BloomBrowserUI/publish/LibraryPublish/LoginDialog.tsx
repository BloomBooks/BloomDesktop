import * as React from "react";
import ReactDOM = require("react-dom");
import { ThemeProvider } from "@material-ui/styles";
import theme from "../../bloomMaterialUITheme";

import StyledFirebaseAuth from "react-firebaseui/StyledFirebaseAuth";
// these two firebase imports are strange, but not an error. See https://github.com/firebase/firebase-js-sdk/issues/1832
import * as firebase from "firebase/app";
import "firebase/auth";
import { BloomApi } from "../../utils/bloomApi";
import { useState, useEffect } from "react";
import axios from "axios";
import theOneLocalizationManager from "../../lib/localizationManager/localizationManager";
import { H2 } from "../../react_components/l10nComponents";

// This component supports logging in using Firebase. In addition to support
// for showing the dialog, it supports being loaded into a browser (but not
// displayed) in order to logout or in order to get a parse server token after
// restarting the main app.
export const LoginDialog: React.FunctionComponent<{}> = props => {
    const [done, setDone] = useState(false);
    // Configuration params for FirebaseUI.
    const uiConfig = {
        signInFlow: "popup",
        // signInSuccessUrl: "/",
        credentialHelper: "none", // don't show some weird "Account Chooser" thing with email login
        // We will display Google and Facebook as auth providers.
        signInOptions: [
            {
                // One option is to sign in with Google. It's quite hard to get
                // signed out of Google...using our own logout command wont' do it.
                // That can make it hard to get an opportunity to sign in using
                // a different account, so we configure it to always confirm
                // which Google account is to be used, even if there is a single
                // account to which the user is already logged in...which normally
                // causes the "sign in with Google" button to just use that account.
                provider: firebase.auth.GoogleAuthProvider.PROVIDER_ID,
                customParameters: {
                    // Forces account selection even when one account
                    // is available.
                    prompt: "select_account"
                }
            },
            {
                // Can also sign in by just providing an email and password.
                provider: firebase.auth.EmailAuthProvider.PROVIDER_ID,
                signInMethod: "password" //getEmailSignInMethod()
            }
        ],
        callbacks: {
            signInSuccessWithAuthResult: (
                authResult: any,
                redirectUrl: any
            ) => {
                // If the user has not verified their email...typically they have just now
                // created a new account...require them to do so before logging in.
                // This is especially important to prevent someone just taking over
                // an existing parse server account, since we will merge a new firebase
                // account automatically with an existing parse server one for the same
                // email.
                if (!authResult.user.emailVerified) {
                    authResult.user.sendEmailVerification().then(() => {
                        theOneLocalizationManager
                            .asyncGetTextInLang(
                                "PublishTab.Upload.CheckEmail",
                                "Please check your email and click on the link there, then log in again.",
                                "UI",
                                ""
                            )
                            .done(translation => {
                                alert(translation);
                                setDone(true);
                            });
                    });
                    firebase.auth().signOut();
                    return;
                }
                // get token from parse, return to desktop
                const bucketName = getBucketName();
                authResult.user.getIdToken().then(async (idToken: string) => {
                    await connectParseServer(
                        idToken,
                        authResult.user.email!,
                        bucketName
                    )
                        // .then(result =>
                        //     console.log("ConnectParseServer resolved with " + result)
                        // )
                        .catch(err => {
                            console.log(
                                "*** Signing out of firebase because of an error connecting to ParseServer"
                            );
                            firebase.auth().signOut();
                        });
                    // We want to wait for another render and some cleanup before actually
                    // closing the dialog...see comments below.
                    setDone(true);
                });

                return false;
            },
            signInFailure: (error: any) => {
                //Sentry.captureException(error); // probably won't happen, nothing seems to bring us here
                console.error("!!!!!!!!!!! signInFailure");
                alert("signInFailure");
                return new Promise((r, x) => {});
            }
        }
    };
    useEffect(() => {
        if (done) {
            // This is a workaround to prevent a weird crash in GeckoFx when disposing of the browser.
            while (document.body.firstChild) {
                document.body.removeChild(document.body.firstChild!);
            }
            setTimeout(() => closeDialog(), 200);
        }
    }, [done]);
    return (
        // The box is plenty big enough for its contents, and the browser indicates
        // they are actually not big enough to overflow; but we get a scroll bar.
        // It appears some descendent, perhaps a hidden one, overflows greatly,
        // somewhere in the StyledFirebaseAuth. Hiding overflow is the only way
        // I've found to get rid of the scroll bar.
        <div style={{ overflow: "hidden" }}>
            <H2 l10nKey="PublishTab.Upload.SignInSignUp">Sign In / Sign Up</H2>
            <div>
                {done || (
                    <StyledFirebaseAuth
                        uiConfig={uiConfig as any}
                        firebaseAuth={firebase.auth()}
                    />
                )}
            </div>
        </div>
    );
};

function getBucketName() {
    return new URLSearchParams(window.location.search.substring(1)).get(
        "bucket"
    )!;
}

async function connectParseServer(
    jwtToken: string,
    userId: string,
    bucketName: string
) {
    const connection = getConnection(bucketName);
    // console.log(
    //     "Connecting to parse server for user " +
    //         userId +
    //         " in bucket " +
    //         bucketName +
    //         " with token " +
    //         jwtToken
    // );
    try {
        // Run a cloud code function (bloomLink) which,
        // if this is a new Firebase user with the email of a known parse server user, will link them.
        // It will do nothing if
        // - we have an existing parse server user with authData
        //   - in this case, the POST to users will log them in
        // - we have no existing parse server user
        //   - in this case, the POST to users will create the parse server user and link to the Firebase user
        await axios.post(
            `${connection.url}functions/bloomLink`,
            {
                token: jwtToken,
                id: userId
            },
            {
                headers: connection.headers
            }
        );
    } catch (err) {
        console.log("The `Bloom Link` call failed:" + JSON.stringify(err));
        failedToLoginInToParseServer();
        return;
    }
    // Now we can log in (or create a new parse server user if needed)
    try {
        //console.log("Posting login request for " + userId);
        const usersResult = await axios.post(
            `${connection.url}users`,
            {
                authData: {
                    bloom: { token: jwtToken, id: userId }
                },
                username: userId,
                email: userId // needed in case we are creating a new user
            },
            {
                headers: connection.headers
            }
        );
        // console.log(
        //     "got results " +
        //         usersResult.data.email +
        //         ", " +
        //         usersResult.data.name +
        //         " with userId " +
        //         usersResult.data.objectId +
        //         "with token " +
        //         usersResult.data.sessionToken
        // );
        if (usersResult.data.sessionToken) {
            // Don't rely on parse server to give us back the email, apparently it does
            // not do so when creating a new user.
            BloomApi.postData("common/loginData", {
                sessionToken: usersResult.data.sessionToken,
                email: userId,
                userId: usersResult.data.objectId
            });
        } else {
            failedToLoginInToParseServer();
        }
        closeDialog();
    } catch (err) {
        failedToLoginInToParseServer();
        closeDialog();
    }
}

function failedToLoginInToParseServer() {
    // Sentry.captureException(
    //     new Error(
    //         "Login to parse server failed after successful firebase login"
    //     )
    // );
    theOneLocalizationManager
        .asyncGetTextInLang(
            "PublishTab.Upload.SomethingWrong",
            "Oops, something went wrong when trying to log you into our database.",
            "UI",
            ""
        )
        .done(translation => {
            alert(translation);
        });
}

// An instance of this object tracks the parse server information that depends on whether
// we are in production or development.
interface IConnection {
    headers: {
        "Content-Type": string;
        "X-Parse-Application-Id": string;
        "X-Parse-Session-Token"?: string;
    };
    url: string;
}
const prod: IConnection = {
    headers: {
        "Content-Type": "text/json",
        "X-Parse-Application-Id": "R6qNTeumQXjJCMutAJYAwPtip1qBulkFyLefkCE5"
    },
    url: "https://bloom-parse-server-production.azurewebsites.net/parse/"
};
// eslint-disable-next-line @typescript-eslint/no-unused-vars
const dev: IConnection = {
    headers: {
        "Content-Type": "text/json",
        "X-Parse-Application-Id": "yrXftBF6mbAuVu3fO6LnhCJiHxZPIdE7gl1DUVGR"
    },
    url: "https://bloom-parse-server-develop.azurewebsites.net/parse/"
};

// If we need it this allows us to work with a local parse server database.
// const local: IConnection = {
//     headers: {
//         "Content-Type": "text/json",
//         "X-Parse-Application-Id": "myAppId",
//     },
//     url: "http://localhost:1337/parse/",
// };

export function getConnection(bucketName: string): IConnection {
    if (bucketName.endsWith("-Sandbox")) {
        return dev;
    }
    return prod;
}

function closeDialog() {
    //console.log("closing dialog");
    BloomApi.post("dialog/close");
}

// The code below is automatically executed as a result of loading the package
// containing this file. It is loaded in various modes identified by a mode
// search param.

const firebaseConfig = {
    apiKey: "AIzaSyACJ7fi7_Rg_bFgTIacZef6OQckr6QKoTY",
    authDomain: "sil-bloomlibrary.firebaseapp.com",
    databaseURL: "https://sil-bloomlibrary.firebaseio.com",
    projectId: "sil-bloomlibrary",
    storageBucket: "sil-bloomlibrary.appspot.com",
    messagingSenderId: "481016061476",
    appId: "1:481016061476:web:8c9905ffec02e8579b82b1"
};

const mode = new URLSearchParams(window.location.search.substring(1)).get(
    "mode"
);
if (mode === "logout") {
    // Used (without UI) when the user logs out. Note that this
    // logs them out of firebase, but does NOT log them out of google,
    // if they chose to use a google sign-in. Doing that is both difficult
    // and dubious...they may be using it in other apps. Of course it's also
    // dubious NOT to do it...they may be counting on being safely logged out
    // of everything before leaving this computer to another user.
    firebase.initializeApp(firebaseConfig);
    firebase
        .auth()
        .signOut()
        .then(() => {
            // it's not really open, but this allows resources to be disposed
            closeDialog();
        });
} else if (mode === "getToken") {
    // used at startup, if the user is in a logged-in state return the token.
    firebase.initializeApp(firebaseConfig);
    firebase.auth().onAuthStateChanged(() => {
        const user = firebase.auth().currentUser;
        if (!user || !user.emailVerified || !user.email) {
            // it's not really open, but this allows resources to be disposed
            closeDialog();
            return;
        }
        const bucketName = getBucketName();
        user.getIdToken().then((idToken: string) => {
            connectParseServer(idToken, user.email!, bucketName)
                // .then(result =>
                //     console.log("ConnectParseServer resolved with " + result)
                // )
                .catch(err => {
                    console.log(
                        "*** Signing out of firebase because of an error connecting to ParseServer"
                    );
                    firebase.auth().signOut();
                    // it's not really open, but this allows resources to be disposed
                    closeDialog();
                });
        });
    });
} else if (document.getElementById("LoginDialog")) {
    // a bit goofy... currently the html loads everything in publishUIBundlejs. So all the publish screens
    // get any not-in-a-class code called, including ours. But it only makes sense to get wired up
    // if that html has the root page we need.
    firebase.initializeApp(firebaseConfig);

    ReactDOM.render(
        <ThemeProvider theme={theme}>
            <LoginDialog />
        </ThemeProvider>,
        document.getElementById("LoginDialog")
    );
}
// If none of the above apply, this component is not really being used...
// it's merely being loaded as part of some other component that the publish
// bundle supports.
