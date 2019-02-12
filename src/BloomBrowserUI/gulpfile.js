/// <binding />
var gulp = require("gulp");
var semver = require("semver");
var { engines } = require("./package");
var tap = require("gulp-tap");
var replaceExt = require("replace-ext");

//set up markdown with the extensions that we use to mark lines for localization
var markdownIt = require("markdown-it")({
    html: true, // enable HTML tags in source
    linkify: true // autoconvert URL-like text to links
});
var markdownItContainer = require("markdown-it-container");
var markdownItAttrs = require("markdown-it-attrs");
markdownIt.use(markdownItContainer);
markdownIt.use(markdownItAttrs);

var debug = require("gulp-debug");
var batch = require("gulp-batch");
var watch = require("gulp-watch");
var path = require("path");
var sourcemaps = require("gulp-sourcemaps");
var webpackStream = require("webpack-stream");
var del = require("del");
var runSequence = require("run-sequence");
var gulpCopy = require("gulp-copy");
var gulpFlatten = require("gulp-flatten");
var globule = require("globule");
var child_process = require("child_process");

// Ensure the version of node we are running is the one we require
const version = engines.node;
if (!semver.satisfies(process.version, version)) {
    console.log(
        `Required node version ${version} not satisfied with current version ${
            process.version
        }.`
    );
    process.exit(1);
}

//this is where we eventually want everything. Note: the same value must be in the webpack config.
//This one is really only used for 'clean';
var outputDir = "../../output/browser";

//todo: can remove these output exlusions now that output/ is now 2 levels up with the c# outpuuts
var paths = {
    help: ["./help/**/*.md"],
    templateReadme: ["./templates/**/ReadMe*.md"],
    infoPages: ["./infoPages/*.md"],
    distInfo: [
        "../../DistFiles/license.md",
        "../../DistFiles/AdobeColorProfileEULA.md"
    ],
    less: ["./**/*.less", "!./node_modules/**/*.less"],
    pug: ["./**/*.pug", "!./node_modules/**/*.pug", "!./**/*mixins.pug"],

    //files we are *not* running through some compiler that need to make it into the outputDir directory.
    filesThatMightBeNeededInOutput: [
        "./**/*.*",
        "!./**/*.ts",
        "!./**/*.tsx",
        "!./**/*.pug",
        "!./**/*.md",
        "!./**/*.less",
        "!./**/*.bat",
        "!./**/node_modules/**/*.*"
    ],
    // List all the HTML files created by markdown or pug earlier in this gulp process.
    htmlFiles: ["../../output/browser/**/*-en.htm*"],
    // List all the available translated Xliff files. (omitting original English Xliff files)
    xliff: [
        "../../DistFiles/localization/**/*.xlf",
        "!../../DistFiles/localization/en/*.xlf",
        "!../../DistFiles/localization/**/*-en.xlf"
    ]
};

// Expand the wildcards to get actual file list for translated Xliff.
var allXliffFiles = globule.find(paths.xliff);
// Check for the existence of the SIL mono, which flags we're on Linux
// and need to use it to execute HtmlXliff.exe
var IsLinux = globule.find(["/opt/mono4-sil/**/mono"]).length > 0;

gulp.task("less", function() {
    var less = require("gulp-less");
    return gulp
        .src(paths.less)
        .pipe(debug({ title: "less:" }))
        .pipe(sourcemaps.init())
        .pipe(
            less()
                // Without this, the task will happily go on its merry way and you have to
                // scroll up through the log messages to know if there was any problem at all.
                .on("error", function(error) {
                    console.error(error.message);
                    process.exit(1);
                })
        )
        .pipe(sourcemaps.write(outputDir))
        .pipe(gulp.dest(outputDir)); //drop all css's into the same dirs.
});

gulp.task("pug", function() {
    var pug = require("gulp-pug");
    return gulp
        .src(paths.pug)
        .pipe(debug({ title: "pug:" }))
        .pipe(
            pug({
                pretty: true
            })
        )
        .pipe(gulp.dest(outputDir)); //drop all html's into the same dirs.
});

gulp.task("webpack", function() {
    var webpackconfig = require("./webpack.config.js");
    return gulp
        .src("unused") // webpack appears to ignore this since we're defining multiple entry points in webpack.config.js, which is good!
        .pipe(webpackStream(webpackconfig, require("webpack")))
        .pipe(gulp.dest(outputDir));
});

gulp.task("webpack-prod", function() {
    var webpackconfig = require("./webpack.config-prod.js");
    return gulp
        .src("unused") // webpack appears to ignore this since we're defining multiple entry points in webpack.config.js, which is good!
        .pipe(webpackStream(webpackconfig, require("webpack")))
        .pipe(gulp.dest(outputDir));
});

gulp.task("clean", function() {
    return del([outputDir + "/**/*"], { force: true });
});

//This task is needed to move files we are *not* running through some
//compiler into the outputDir directory.
gulp.task("copy", function() {
    // this prefix:3 thing strips off node_modules/jquery/dist so that the file ends up right in the ouput dir
    gulp.src("./node_modules/jquery/dist/jquery.min.js").pipe(
        gulpCopy(outputDir, { prefix: 3 })
    );

    return gulp
        .src(paths.filesThatMightBeNeededInOutput)
        .pipe(gulpCopy(outputDir));
});

gulp.task("watchInner", function() {
    watch(
        paths.less,
        batch(function(events, done) {
            gulp.start("copy", done);
        })
    );
    watch(
        paths.less,
        batch(function(events, done) {
            gulp.start("less", done);
        })
    );
    watch(
        paths.pug,
        batch(function(events, done) {
            gulp.start("pug", done);
        })
    );
    watch(
        paths.help,
        batch(function(events, done) {
            gulp.start("markdownHelp", done);
        })
    );
    watch(
        paths.templateReadme,
        batch(function(events, done) {
            gulp.start("markdownTemplateReadme", done);
        })
    );
    watch(
        paths.infoPages,
        batch(function(events, done) {
            gulp.start("markdownInfoPages", done);
        })
    );
    watch(
        paths.distInfo,
        batch(function(events, done) {
            gulp.start("markdownDistInfo", done);
        })
    );
});

gulp.task("watchlp", function() {
    runSequence(["less", "pug"], "watchInner");
});

gulp.task("watch", function() {
    console.log(
        '****** PLEASE run "webpack --watch" in a separate console *********'
    );
    runSequence(
        "clean",
        "copy",
        [
            "less",
            "pug",
            "markdownHelp",
            "markdownTemplateReadme",
            "markdownInfoPages",
            "markdownDistInfo"
        ],
        "watchInner"
    );
});

gulp.task("markdownHelp", function() {
    //here we are assigning an unfortunately named stylesheet that goes with all Bloom help pages
    return basicMarkdown(
        paths.help,
        "<link rel='stylesheet' href='help.css' type='text/css'/>",
        "/help"
    );
});

gulp.task("markdownTemplateReadme", function() {
    return gulp
        .src(paths.templateReadme)
        .pipe(debug({ title: "md:" }))
        .pipe(
            tap(function(file) {
                var result = markdownIt.render(file.contents.toString());
                // wrap the generated HTML in a document and make it use our standard stylesheet.
                // strip out the string we insert to obfuscate email addresses in source code.
                file.contents = new Buffer(
                    `<html><head><meta charset='utf-8'><link rel='stylesheet' href='../../../bookPreview/BookReadme.css' type='text/css' /></head><body>
                ` +
                        result.replace("removethis", "") +
                        `
                </body></html>`
                );
                file.path = replaceExt(file.path, ".htm");
                return;
            })
        )
        .pipe(gulp.dest(outputDir + "/templates")) // we lose this level because it's part of the glob base
        .pipe(debug({ title: " md --> " }));
});

gulp.task("markdownInfoPages", function() {
    return basicMarkdown(paths.infoPages, "", "/infoPages");
});

gulp.task("markdownDistInfo", function() {
    return basicMarkdown(paths.distInfo, "", "");
});

gulp.task("translateHtmlFiles", function() {
    return gulp
        .src(paths.htmlFiles)
        .pipe(debug({ title: "translateHtmlFiles:" }))
        .pipe(
            tap(function(file) {
                var xliffFiles = getXliffFiles(file.path);
                for (i = 0; i < xliffFiles.length; ++i) {
                    var outfile = getOutputFilename(file.path, xliffFiles[i]);
                    var cmd = "";
                    if (IsLinux)
                        cmd =
                            "/opt/mono4-sil/bin/mono --debug ../../lib/dotnet/HtmlXliff.exe --inject";
                    else cmd = "..\\..\\lib\\dotnet\\HtmlXliff.exe --inject";
                    cmd = cmd + ' -x "' + xliffFiles[i] + '"';
                    cmd = cmd + ' -o "' + outfile + '"';
                    cmd = cmd + ' "' + file.path + '"';
                    child_process.exec(cmd, function(err, stdout, stderr) {
                        if (err) {
                            console.error(
                                "\nTRANSLATE ${file.path} WITH ${xliffFiles[i]}\n${stdout}\n\n${stderr}"
                            );
                        }
                    });
                }
                return;
            })
        );
});

gulp.task("createXliffFiles", function() {
    return gulp
        .src(paths.htmlFiles)
        .pipe(debug({ title: "createXliffFiles:" }))
        .pipe(
            tap(function(file) {
                var xliffFile = getXliffFilename(file.path);
                var cmd = "";
                if (IsLinux)
                    cmd =
                        "/opt/mono4-sil/bin/mono --debug ../../lib/dotnet/HtmlXliff.exe --extract --preserve";
                else
                    cmd =
                        "..\\..\\lib\\dotnet\\HtmlXliff.exe --extract --preserve";
                cmd = cmd + ' -o "' + xliffFile + '"';
                cmd = cmd + ' "' + file.path + '"';
                child_process.exec(cmd, function(err, stdout, stderr) {
                    if (err) {
                        console.error(
                            "\nCREATE ${xliffFile} FROM ${file.path}\n${stdout}\n\n${stderr}"
                        );
                    }
                });
                return;
            })
        );
});

gulp.task("default", function(callback) {
    //NB: run-sequence is needed for gulp 3.x, but soon there will be gulp which will have a built-in "series" function.
    // This parallelism seems to provide the best performance.  The pug and markdown tasks must all be done before the
    // translateHtml and createXliff tasks.  Putting 'less' with the first group and 'webpack' with the second group
    // minimizes the overall build time according to tests.
    // Keep the "build-prod" task in sync with this.
    runSequence(
        "clean",
        "copy",
        [
            "brandings",
            "less",
            "pug",
            "markdownHelp",
            "markdownTemplateReadme",
            "markdownInfoPages",
            "markdownDistInfo"
        ],
        ["webpack", "translateHtmlFiles", "createXliffFiles"],
        callback
    );
});

gulp.task("build-prod", function(callback) {
    //Should generally match default, with just those changes needed for a production
    // build.
    runSequence(
        "clean",
        "copy",
        [
            "brandings",
            "less",
            "pug",
            "markdownHelp",
            "markdownTemplateReadme",
            "markdownInfoPages",
            "markdownDistInfo"
        ],
        ["webpack-prod", "translateHtmlFiles", "createXliffFiles"],
        callback
    );
});

gulp.task("brandings", function() {
    // run("npm run buildBrandings"));
    return child_process.execFile("npm run buildBrandings");
});

// Find which of the translated xliff files match up with the given html file.
// Note that allXliffFiles uses / to separate directories even on Windows, but
// htmFile uses / on Linux and \ on Windows.
var getXliffFiles = function(htmFile) {
    var pathPieces = htmFile.split("/");
    if (htmFile.includes("\\")) pathPieces = htmFile.split("\\");
    var basename = pathPieces[pathPieces.length - 1];
    if (basename == "ReadMe-en.htm") {
        basename = "/" + pathPieces[pathPieces.length - 2] + "/ReadMe-";
    } else {
        var pos = basename.search("-en.htm");
        if (pos > 0) basename = "/" + basename.substring(0, pos);
    }
    var retval = [];
    for (i = 0; i < allXliffFiles.length; i++) {
        var pos = allXliffFiles[i].search(basename);
        if (pos > 0) retval.push(allXliffFiles[i]);
    }
    return retval;
};

// Get the language code from the xlfFile path and put it into the htmFile path
// (replacing the English language code) for an output file pathname.
var getOutputFilename = function(htmFile, xlfFile) {
    var langCode = "";
    if (xlfFile.search("/ReadMe-") > 0)
        // as in "blah/foo/ReadMe-en.htm"
        langCode = xlfFile.replace(/.*\/ReadMe-/, "").replace(".xlf", "");
    // as in "blah/foo/fr/something.htm"
    else langCode = xlfFile.split("/").slice(-2, -1)[0]; // penultimate item has the language code
    return htmFile.replace("-en.htm", "-" + langCode + ".htm");
};

// Get the name of the English Xliff file corresponding to the English HTML file.
var getXliffFilename = function(htmFile) {
    var htmPieces = htmFile.split("/");
    if (htmFile.includes("\\")) htmPieces = htmFile.split("\\");
    var basename = htmPieces[htmPieces.length - 1];
    if (basename == "ReadMe-en.htm") {
        return (
            "../../DistFiles/localization/" +
            htmPieces[htmPieces.length - 2] +
            "/" +
            basename.replace(".htm", ".xlf")
        );
    } else {
        return (
            "../../DistFiles/localization/en/" +
            basename.replace(/-en\.html?$/, ".xlf")
        );
    }
};

var basicMarkdown = function(files, style, folder) {
    return gulp
        .src(files)
        .pipe(debug({ title: "md:" }))
        .pipe(
            tap(function(file) {
                var result = markdownIt.render(file.contents.toString());
                // convert to full HTML, ensuring that the file is known to be utf-8.
                file.contents = new Buffer(
                    `<html><head><meta charset='utf-8'>` +
                        style +
                        `</head><body>
` +
                        result +
                        `
</body></html>`
                );
                file.path = replaceExt(file.path, ".htm");
                return;
            })
        )
        .pipe(gulpFlatten({ includeParents: 0 })) // number of parent folders to include
        .pipe(gulp.dest(outputDir + folder))
        .pipe(debug({ title: " md --> " }));
};
