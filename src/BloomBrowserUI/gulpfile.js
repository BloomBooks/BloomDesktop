/// <binding />
var gulp = require("gulp");
var semver = require("semver");
var { engines } = require("./package");
var tap = require("gulp-tap");
var replaceExt = require("replace-ext");
var ts = require("gulp-typescript");

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
var gulpCopy = require("gulp-copy");
var gulpFlatten = require("gulp-flatten");
var globule = require("globule");
var child_process = require("child_process");

// Ensure the version of node we are running is the one we require
const version = engines.node;
if (!semver.satisfies(process.version, version)) {
    console.log(
        `Required node version ${version} not satisfied with current version ${process.version}.`
    );
    process.exit(1);
}

//this is where we eventually want everything. Note: the same value must be in the webpack config.
//This one is really only used for 'clean';
var outputDir = "../../output/browser";

//todo: can remove these output exclusions now that output/ is now 2 levels up with the c# outputs
var paths = {
    help: ["./help/**/*.md"],
    templateReadme: ["../content/templates/**/ReadMe*.md"],
    infoPages: ["./infoPages/*.md"],
    distInfo: [
        "../../DistFiles/license.md",
        "../../DistFiles/AdobeColorProfileEULA.md"
    ],
    less: ["./**/*.less", "!./node_modules/**/*.less"],
    // Notice that, unlike with less, we are still compiling the content pug here. This is because
    // we haven't found a good cross-platform way of using a glob with pug yet outside of gulp, and
    // the /content project is trying to get away form using gulp.
    pug: [
        "./**/*.pug",
        "!./node_modules/**/*.pug",
        "../content/**/*.pug",
        "!../content/node_modules/**/*.pug",
        "!./**/*mixins.pug"
    ],

    //files we are *not* running through some compiler that need to make it into the outputDir directory.
    filesThatMightBeNeededInOutput: [
        "./**/*.*",
        "!./**/*.ts",
        "!./**/*.tsx",
        "!./**/*.pug",
        "!./**/*.md",
        "!./**/*.less",
        "!./**/*.bat",
        "!./**/node_modules/**/*.*",
        "!./**/tsconfig.json"
    ],
    nodeFilesNeededInOutput: [
        // Previously, we listed specific files (or extensions) we want to copy.
        // But for now, I'm not seeing any reason not to get all of them.
        "./node_modules/bloom-player/dist/*"
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
var IsLinux = globule.find(["/opt/mono5-sil/**/mono"]).length > 0;

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
        .src("unused", { allowEmpty: true }) // webpack appears to ignore this since we're defining multiple entry points in webpack.config.js, which is good!
        .pipe(webpackStream(webpackconfig, require("webpack")))
        .pipe(gulp.dest(outputDir));
});

gulp.task("webpack-prod", function() {
    var webpackconfig = require("./webpack.config-prod.js");
    return gulp
        .src("unused", { allowEmpty: true }) // webpack appears to ignore this since we're defining multiple entry points in webpack.config.js, which is good!
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

    // Handling these separately lets us strip of the node_modules prefix,
    // but also, it's confusing to try to get things out of node_modules
    // as part of filesThatMightBeNeededInOutput when that glob explicitly
    // excludes node_modules; it ought to work to include some of them after
    // the exclude, but I could not make it do so.
    gulp.src(paths.nodeFilesNeededInOutput)
        .pipe(debug())
        .pipe(gulpCopy(outputDir, { prefix: 1 }));

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

gulp.task("watchlp", gulp.series(gulp.parallel("less", "pug"), "watchInner"));

gulp.task("watch", async function() {
    console.log(
        '****** PLEASE run "webpack --watch" in a separate console *********'
    );
    gulp.series(
        "clean",
        "copy",
        gulp.parallel(
            "less",
            "pug",
            "markdownHelp",
            "markdownTemplateReadme",
            "markdownInfoPages",
            "markdownDistInfo"
        ),
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
                file.contents = Buffer.from(
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
                    var xliffFile = xliffFiles[i]; // needed for error message to work
                    var outfile = getOutputFilename(file.path, xliffFile);
                    var cmd = "";
                    if (IsLinux)
                        cmd =
                            "/opt/mono5-sil/bin/mono --debug ../../lib/dotnet/HtmlXliff.exe --inject";
                    else cmd = "..\\..\\lib\\dotnet\\HtmlXliff.exe --inject";
                    cmd = cmd + ' -x "' + xliffFile + '"';
                    cmd = cmd + ' -o "' + outfile + '"';
                    cmd = cmd + ' "' + file.path + '"';
                    child_process.exec(cmd, function(err, stdout, stderr) {
                        if (err) {
                            console.error(
                                `\nTRANSLATE ${file.path} WITH ${xliffFile}\n${stdout}\n\n${stderr}`
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
                        "/opt/mono5-sil/bin/mono --debug ../../lib/dotnet/HtmlXliff.exe --extract --preserve";
                else
                    cmd =
                        "..\\..\\lib\\dotnet\\HtmlXliff.exe --extract --preserve";
                cmd = cmd + ' -o "' + xliffFile + '"';
                cmd = cmd + ' "' + file.path + '"';
                child_process.exec(cmd, function(err, stdout, stderr) {
                    if (err) {
                        console.error(
                            `\nCREATE ${xliffFile} FROM ${file.path}\n${stdout}\n\n${stderr}`
                        );
                    }
                });
                return;
            })
        );
});

gulp.task("brandings", async function() {
    return child_process.exec("npm run buildBrandings");
});

gulp.task("compileTemplateTypescript", function() {
    // Specifying no typeRoots is a kludge. Without it, the task reports large numbers of
    // errors in various .d.ts files in node_modules that aren't even referenced
    // by the file we're supposed to be compiling. If we eventually get stuff in the template books
    // directory that uses other modules and needs their types, we'll have to find another
    // approach. But in that case, we'll probably want to switch to webpack anyway
    // in order to produce a single minimal bundle as output. This is good enough for
    // a simple transformation of simple typescript.
    var tsProject = ts.createProject("tsconfig.json", { typeRoots: [] });
    return gulp
        .src("./templates/template books/**/*.ts")
        .pipe(tsProject())
        .js.pipe(gulp.dest(outputDir + "/templates/template books"));
});

gulp.task(
    "default",
    gulp.series(
        "clean",
        "copy",
        gulp.parallel(
            "brandings",
            "less",
            "pug",
            "markdownHelp",
            "markdownTemplateReadme",
            "markdownInfoPages",
            "markdownDistInfo"
        ),
        gulp.parallel("webpack", "compileTemplateTypescript")
    )
);

// Skip branding and l10n tasks
gulp.task(
    "build-short",
    gulp.series(
        "clean",
        "copy",
        gulp.parallel("less", "pug"),
        gulp.parallel("webpack")
    )
);

// Run when needing to test l10n on user machines, or when markdown/html content
// changes and xliff files need to be updated for checkin.
gulp.task(
    "build-l10n",
    gulp.parallel("translateHtmlFiles", "createXliffFiles")
);

gulp.task(
    //Should generally match default, with just those changes needed for a production
    // build.
    "build-prod",
    gulp.series(
        "clean",
        "copy",
        gulp.parallel(
            "brandings",
            "less",
            "pug",
            "markdownHelp",
            "markdownTemplateReadme",
            "markdownInfoPages",
            "markdownDistInfo"
        ),
        gulp.parallel(
            "webpack-prod",
            "translateHtmlFiles",
            "createXliffFiles",
            "compileTemplateTypescript"
        )
    )
);

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
                file.contents = Buffer.from(
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
