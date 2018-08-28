// Remove unwanted bundle (and css) files produced by processing less into css.
// Also rename css files that are supposed to have hyphens or spaces in their names
// move all of the css files to where we want them in the folder structure.
// (The last two steps will not be needed once mini-css-extract-plugin allows a function
// for determining the output file path.  The first step would still be nice.)
//
// NOTE: This is not called by webpack watch when recompiling one of the less files.
// The result of the recompilation accumulates in output/browser/styles with the names
// given by the original webpack processing.  Running this explicitly won't help because
// of the check for a specific output file to determine whether to proceed.
// I suppose the cleanupAndRename method could be revised to use the Promise and timeout
// functionality instead of the simple fs.existsSync method, but I'm not sure it's worth
// the effort.

const fs = require("fs");
const path = require("path");
const outputDir = "../../output/browser";

// This method is derived from the third answer at
// https://stackoverflow.com/questions/26165725/nodejs-check-file-exists-if-not-wait-till-it-exist
function checkExistsWithTimeout(path, timeout) {
    return new Promise((resolve, reject) => {
        const timeoutTimerId = setTimeout(handleTimeout, timeout);
        const interval = 500; // check every half-second
        let intervalCount = -1;

        function handleTimeout() {
            clearTimeout(timerId);
            const error = new Error("check for " + path + " timed out");
            error.name = "PATH_CHECK_TIMED_OUT";
            reject(error);
        }

        function handleInterval() {
            fs.access(path, err => {
                ++intervalCount;
                if (err) {
                    let intervalTimerId = setTimeout(handleInterval, interval);
                } else {
                    clearTimeout(timeoutTimerId);
                    resolve({ path: path, time: intervalCount * interval });
                }
            });
        }

        handleInterval(); // test for path immediately.
    });
}

function removeWithTimeout(filepath) {
    checkExistsWithTimeout(filepath, 5000)
        .then(function(response) {
            fs.unlinkSync(response.path);
            let message = "removed " + filepath;
            if (response.time != 0) {
                message = message + " after " + response.time + " msec";
            }
            console.log(message);
        })
        .catch(function(error) {
            console.log(error);
        });
}

function renameWithTimeout(oldpath, newpath) {
    checkExistsWithTimeout(oldpath, 5000)
        .then(function(response) {
            // ensure no problems with existing files or missing directories
            if (fs.existsSync(newpath)) {
                fs.unlinkSync(newpath);
            } else {
                const newdir = path.dirname(newpath);
                if (!fs.existsSync(newdir)) {
                    fs.mkdirSync(newdir);
                }
            }
            fs.renameSync(oldpath, newpath);
            let message = "renamed " + oldpath + " to " + newpath;
            if (response.time != 0) {
                message = message + " after " + response.time + " msec";
            }
            console.log(message);
        })
        .catch(function(error) {
            console.log(error);
        });
}

// Remove the unwanted javascript bundle produced by webpack and move the
// generated css file to the desired location.  The latter won't be needed
// once mini-css-extract-plugin allows us to use a function for the output
// filename instead of its current limited choices.
function cleanupAndRename(bundleName, pathAndBasename) {
    // Delete the .js bundle that we don't need or want.
    let unwanted = outputDir + "/" + bundleName + ".js";
    removeWithTimeout(unwanted);
    removeWithTimeout(unwanted + ".map");

    let oldpath = outputDir + "/styles/" + bundleName + ".css";
    let newpath = outputDir + "/" + pathAndBasename + ".css";
    renameWithTimeout(oldpath, newpath);
    renameWithTimeout(oldpath + ".map", newpath + ".map");
}

function removeBundleCss(bundleName) {
    let unwanted = outputDir + "/styles/" + bundleName + ".css";
    removeWithTimeout(unwanted);
    removeWithTimeout(unwanted + ".map");
}

function cleanupAll() {
    cleanupAndRename("page_chooser", "pageChooser/page-chooser");
    cleanupAndRename("rc_slider_bloom", "bookEdit/css/rc-slider-bloom");

    cleanupAndRename(
        "Basic_Book",
        "templates/template books/Basic Book/Basic Book"
    );
    cleanupAndRename("Big_Book", "templates/template books/Big Book/Big Book");
    cleanupAndRename(
        "Decodable_Reader",
        "templates/template books/Decodable Reader/Decodable Reader"
    );

    cleanupAndRename(
        "Device_XMatter",
        "templates/xMatter/Device-XMatter/Device-XMatter"
    );
    cleanupAndRename(
        "Factory_XMatter",
        "templates/xMatter/Factory-XMatter/Factory-XMatter"
    );
    cleanupAndRename(
        "SIL_Cameroon_XMatter",
        "templates/xMatter/SIL-Cameroon-Mothballed/SIL-Cameroon-XMatter"
    );
    cleanupAndRename(
        "SuperPaperSaver_XMatter",
        "templates/xMatter/SuperPaperSaver-XMatter/SuperPaperSaver-XMatter"
    );
    cleanupAndRename(
        "TemplateStarter_XMatter",
        "templates/xMatter/TemplateStarter-XMatter/TemplateStarter-XMatter"
    );
    cleanupAndRename(
        "Traditional_XMatter",
        "templates/xMatter/Traditional-XMatter/Traditional-XMatter"
    );
    cleanupAndRename(
        "Video_XMatter",
        "templates/xMatter/Video-XMatter/Video-XMatter"
    );
    cleanupAndRename(
        "MXBBook_XMatter",
        "templates/customXMatter/MXBBook-XMatter/MXBBook-XMatter"
    );
    cleanupAndRename(
        "MBXPamphlet_XMatter",
        "templates/customXMatter/MXBPamphlet-XMatter/MXBPamphlet-XMatter"
    );
    cleanupAndRename(
        "Dari_XMatter",
        "templates/customXMatter/Dari-XMatter/Dari-XMatter"
    );
    cleanupAndRename(
        "Pashti_XMatter",
        "templates/customXMatter/Pashti-XMatter/"
    );

    cleanupAndRename(
        "ArithmeticTemplate",
        "templates/template books/Arithmetic Template/ArithmeticTemplate"
    );
    cleanupAndRename(
        "audioRecording",
        "bookEdit/toolbox/talkingBook/audioRecording"
    );
    cleanupAndRename("baseEPUB", "publish/epub/baseEPUB");
    cleanupAndRename("basePage", "bookLayout/basePage");
    cleanupAndRename("bloomDialog", "bookEdit/css/bloomDialog");
    cleanupAndRename(
        "bookSettings",
        "bookEdit/toolbox/bookSettings/bookSettings"
    );
    cleanupAndRename(
        "configuration",
        "templates/template books/Wall Calendar/configuration"
    );
    cleanupAndRename("editMode", "bookEdit/css/editMode");
    cleanupAndRename("editPaneGlobal", "bookEdit/css/editPaneGlobal");
    cleanupAndRename("languageDisplay", "bookLayout/languageDisplay");
    cleanupAndRename("motion", "bookEdit/toolbox/motion/motion");
    cleanupAndRename("music", "bookEdit/toolbox/music/music");
    cleanupAndRename("origami", "bookEdit/css/origami");
    cleanupAndRename("origamiEditing", "bookEdit/css/origamiEditing");
    cleanupAndRename(
        "pageControls",
        "bookEdit/pageThumbnailList/pageControls/pageControls"
    );
    cleanupAndRename(
        "pageThumbnailList",
        "bookEdit/pageThumbnailList/pageThumbnailList"
    );
    cleanupAndRename("previewMode", "bookPreview/previewMode");
    cleanupAndRename(
        "readerSetup",
        "bookEdit/toolbox/readers/readerSetup/readerSetup"
    );
    cleanupAndRename("readerStyles", "publish/android/readerStyles");
    cleanupAndRename("readerTools", "bookEdit/toolbox/readerTools");
    cleanupAndRename("Special", "templates/template books/Special/Special");
    cleanupAndRename("toolbox", "bookEdit/toolbox/toolbox");
    cleanupAndRename("topicChooser", "bookEdit/TopicChooser/topicChooser");
    cleanupAndRename(
        "wallCalendar",
        "templates/template books/Wall Calendar/wallCalendar"
    );

    // A pair of regular javascript bundles are producing css output: remove this unwanted css.
    removeBundleCss("pageControlsBundle");
    removeBundleCss("toolboxBundle");
}

cleanupAll();
