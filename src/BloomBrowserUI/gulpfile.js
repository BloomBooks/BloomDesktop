/// <binding />
var gulp = require('gulp');
var semver = require('semver');
var { engines } = require('./package');
var gutil = require('gulp-util');
var tap = require('gulp-tap');

//set up markdown with the extensions that we use to mark lines for localization
var markdownIt = require('markdown-it')({
    html: true,     // enable HTML tags in source
    linkify: true   // autoconvert URL-like text to links
    });
var markdownItContainer = require('markdown-it-container');
var markdownItAttrs = require('markdown-it-attrs');
markdownIt.use(markdownItContainer);
markdownIt.use(markdownItAttrs);

var debug = require('gulp-debug');
//var ts = require('gulp-typescript');
var batch = require('gulp-batch');
var watch = require('gulp-watch');
var path = require('path');
var sourcemaps = require('gulp-sourcemaps');
//const babel = require('gulp-babel');
//var browserify = require('gulp-browserify');
var webpack = require('gulp-webpack');
var del = require('del');
var runSequence = require('run-sequence');
var gulpCopy = require('gulp-copy');
var gulpFlatten = require('gulp-flatten');
var globule = require('globule');
var myProcess = require('child_process');

// Ensure the version of node we are running is the one we require
const version = engines.node;
if (!semver.satisfies(process.version, version)) {
    console.log(`Required node version ${version} not satisfied with current version ${process.version}.`);
    process.exit(1);
}

//this is where we eventually want everything. Note: the same value must be in the webpack config.
//This one is really only used for 'clean';
var outputDir = "../../output/browser";

const leveledRTInfoPath = '../../DistFiles/leveledRTInfo';

//todo: can remove these output exlusions now that output/ is now 2 levels up with the c# outpuuts
var paths = {
    help: ['./help/**/*.md'],
    templateReadme: ['./templates/**/ReadMe*.md'],
    distInfo: [ '../../DistFiles/**/*.md', '!../../DistFiles/ReleaseNotes.md'],
    less: ['./**/*.less', '!./node_modules/**/*.less'],
    pug: ['./**/*.pug', '!./node_modules/**/*.pug', '!./**/*mixins.pug'],
    //typescript: ['./**/*.ts','!./**/*.d.ts', '!./**/node_modules/**/*.*'],

    //files we are *not* running through some compiler that need to make it into the outputDir directory.
    filesThatMightBeNeededInOutput: ['./**/*.*', '!./**/*.ts', '!./**/*.pug', '!./**/*.md', '!./**/*.less', '!./**/*.bat', '!./**/node_modules/**/*.*'],
    // List all the HTML files created by markdown or pug earlier in this gulp process.
    htmlFiles: ['../../DistFiles/**/*-en.htm*', '../../output/browser/**/*-en.htm*'],
    // List all the available translated Xliff files. (omitting original English Xliff files)
    xliff: ['../../DistFiles/localization/**/*.xlf', '!../../DistFiles/localization/en/*.xlf',
                '!../../DistFiles/localization/**/*-en.xlf'],
};

// Expand the wildcards to get actual file list for translated Xliff.
var allXliffFiles = globule.find(paths.xliff);
// Check for the existence of the SIL mono, which flags we're on Linux
// and need to use it to execute HtmlXliff.exe
var IsLinux = globule.find(['/opt/mono4-sil/**/mono']).length > 0;

gulp.task('less', function () {
    var less = require('gulp-less');
    return gulp.src(paths.less)
        .pipe(debug({ title: 'less:' }))
        .pipe(sourcemaps.init())
        .pipe(less())
        .pipe(sourcemaps.write(outputDir))
        .pipe(gulp.dest(outputDir)); //drop all css's into the same dirs.
});

gulp.task('pug', function () {
    var pug = require('gulp-pug');
    return gulp.src(paths.pug)
        .pipe(debug({ title: 'pug:' }))
        .pipe(pug({
            pretty: true
        }))
        .pipe(gulp.dest(outputDir)); //drop all html's into the same dirs.
});

gulp.task('pugLRT', function () {
    var pug = require('gulp-pug');
    return gulp.src(leveledRTInfoPath + '/*.pug')
        .pipe(debug({ title: 'pug:' }))
        .pipe(pug({
            pretty: true
        }))
        .pipe(gulp.dest(leveledRTInfoPath)); // these html's stay in place.
});

gulp.task('webpack', function () {
    var webpackconfig = require('./webpack.config.js');
    return gulp.src('unused') // webpack appears to ignore this since we're defining multiple entry points in webpack.config.js, which is good!
        .pipe(webpack(webpackconfig))
        .pipe(gulp.dest(outputDir));
});

gulp.task('clean', function () {
    return del([outputDir + "/**/*"], { force: true });
});

// gulp.task('jsx?', () => {
// 	return gulp.src(paths.jsx)
// 		.pipe(babel({
// 			presets: ['es2015', "react"]
// 		}))
//         .pipe(browserify({
// 		  insertGlobals : true
// 		}))
// 		.pipe(gulp.dest(outputDir));
// });

//This task is needed to move files we are *not* running through some
//compiler into the outputDir directory.
gulp.task('copy', function () {
    // this prefix:3 thing strips off node_modules/jquery/dist so that the file ends up right in the ouput dir
    gulp.src('./node_modules/jquery/dist/jquery.min.js')
        .pipe(gulpCopy(outputDir, { prefix: 3 }))

    //   gulp.src('./node_modules/jquery.hotkeys/jquery.hotkeys.js')
    //     .pipe(gulpCopy(outputDir, {prefix:2}))

    return gulp.src(paths.filesThatMightBeNeededInOutput)
        .pipe(gulpCopy(outputDir))
});

// gulp.task('typescript', function () {
//   return gulp.src(paths.typescript)
//     .pipe(debug({title: 'typescript:'}))
//     .pipe(sourcemaps.init())
//     .pipe(ts({
//         target: "es5",// need to keep this down to the level that our gecko can directly handle. Things going through webpack+babel can target es6, fine. But not this other un-converted  stuff.
//         module:"commonjs"
//     }))
//     .pipe(sourcemaps.write(outputDir))
//     .pipe(gulp.dest(outputDir)); //drop all js's into the same dirs.
// });

gulp.task('watchInner', function () {
    watch(paths.less, batch(function (events, done) {
        gulp.start('copy', done);
    }));
    watch(paths.less, batch(function (events, done) {
        gulp.start('less', done);
    }));
    watch(paths.pug, batch(function (events, done) {
        gulp.start('pug', done);
    }));
    watch(leveledRTInfoPath + '/*.pug', batch(function (events, done) {
        gulp.start('pugLRT', done);
    }));
    watch(paths.help, batch(function (events, done) {
        gulp.start('markdownHelp', done);
    }));
    watch(paths.templateReadme, batch(function (events, done) {
        gulp.start('markdownTemplateReadme', done);
    }));
    watch(paths.distInfo, batch(function (events, done) {
        gulp.start('markdownDistInfo', done);
    }));
})

gulp.task('watchlp', function () {
    runSequence(['less', 'pug', 'pugLRT'], 'watchInner');
});

gulp.task('watch', function () {
    console.log('****** PLEASE run "webpack --watch" in a separate console *********');
    runSequence('clean', 'copy', ['less', 'pug', 'pugLRT', 'markdownHelp', 'markdownTemplateReadme', 'markdownDistInfo'], 'watchInner');
});

gulp.task('markdownHelp', function () {
    return gulp.src(paths.help)
        .pipe(debug({ title: 'md:' }))
        .pipe(tap(function (file) {
            var result = markdownIt.render(file.contents.toString());
            file.contents = new Buffer(
                //here we are assigning an unfortunately named stylesheet that goes with all Bloom help pages
                `<html><head><meta charset='utf-8'><link rel='stylesheet' href='help.css' type='text/css'/></head><body>
` + result + `
</body></html>`);
            file.path = gutil.replaceExtension(file.path, '.htm');
            return;
        }))
        .pipe(gulpFlatten({ includeParents: 0 })) // number of parent folders to include
        .pipe(gulp.dest(outputDir + "/help"))
        .pipe(debug({ title: ' md --> ' }));
});

gulp.task('markdownTemplateReadme', function () {
    return gulp.src(paths.templateReadme)
        .pipe(debug({ title: 'md:' }))
        .pipe(tap(function (file) {
            var result = markdownIt.render(file.contents.toString());
	    // this is converted to full HTML in C# code, adding base href and link to css file
            file.contents = new Buffer(result);
            file.path = gutil.replaceExtension(file.path, '.htm');
            return;
        }))
        .pipe(gulp.dest(outputDir + "/templates"))	// we lose this level somewhere
        .pipe(debug({ title: ' md --> ' }));
});

gulp.task('markdownDistInfo', function () {
    return gulp.src(paths.distInfo)
        .pipe(debug({ title: 'md:' }))
        .pipe(tap(function (file) {
            var result = markdownIt.render(file.contents.toString());
	    // convert to full HTML, ensuring that the file is known to be utf-8.
            file.contents = new Buffer(`<html><head><meta charset='utf-8'></head><body>
` + result + `
</body></html>`);
            file.path = gutil.replaceExtension(file.path, '.htm');
            return;
        }))
        .pipe(gulp.dest("../../DistFiles"))
        .pipe(debug({ title: ' md --> ' }));
});

gulp.task('translateHtmlFiles', function() {
    return gulp.src(paths.htmlFiles)
        .pipe(debug({ title: 'translateHtmlFiles:' }))
        .pipe(tap(function (file) {
            var xliffFiles = getXliffFiles(file.path);
            for (i = 0; i < xliffFiles.length; ++i) {
                var outfile = getOutputFilename(file.path, xliffFiles[i]);
                var cmd = "";
                if (IsLinux)
                    cmd = "/opt/mono4-sil/bin/mono ../../lib/dotnet/HtmlXliff.exe --inject";
                else
                    cmd = "..\\..\\lib\\dotnet\\HtmlXliff.exe --inject";
                cmd = cmd + " -x \"" + xliffFiles[i] + "\"";
                cmd = cmd + " -o \"" + outfile + "\"";
                cmd = cmd + " \"" + file.path + "\"";
                console.log("Translating " + outfile);
                myProcess.exec(cmd, function(err, stdout, stderr) {
                    if (err) {
                        console.error("\n" + stderr);
                    }
                });
            }
            return;
        }))
});

gulp.task('createXliffFiles', function() {
    return gulp.src(paths.htmlFiles)
        .pipe(debug({ title: 'createXliffFiles:' }))
        .pipe(tap(function (file) {
            var xliffFile = getXliffFilename(file.path);
            var cmd = "";
            if (IsLinux)
                cmd = "/opt/mono4-sil/bin/mono ../../lib/dotnet/HtmlXliff.exe --extract";
            else
                cmd = "..\\..\\lib\\dotnet\\HtmlXliff.exe --extract";
            cmd = cmd + " -o \"" + xliffFile + "\"";
            cmd = cmd + " \"" + file.path + "\"";
            console.log("Extracting " + xliffFile + " from " + file.path);
            myProcess.exec(cmd, function(err, stdout, stderr) {
                if (err) {
                    console.error("\n" + stderr);
                }
            });
            return;
        }))

});

gulp.task('default',
    function (callback) {
        //NB: run-sequence is needed for gulp 3.x, but soon there will be gulp which will have a built-in "series" function.
        //currently our webpack run is pure javascript, so do it only after the typescript is all done
        runSequence('clean', 'copy', ['less', 'pug', 'pugLRT', 'markdownHelp', 'markdownTemplateReadme', 'markdownDistInfo'], ['webpack', 'translateHtmlFiles', 'createXliffFiles'], callback)
    });

// Find which of the translated xliff files match up with the given html file.
// Note that allXliffFiles uses / to separate directories even on Windows, but
// htmFile uses / on Linux and \ on Windows.
var getXliffFiles = function (htmFile) {
    var pathPieces = htmFile.split("/");
    if (htmFile.includes("\\"))
        pathPieces = htmFile.split("\\");
    var basename = pathPieces[pathPieces.length - 1];
    if (basename == "ReadMe-en.htm") {
        basename = "/" + pathPieces[pathPieces.length - 2] + "/ReadMe-";
    } else {
        var pos = basename.search("-en.htm");
        if (pos > 0)
           basename = "/" + basename.substring(0, pos);
    }
    var retval = [];
    for (i = 0 ; i < allXliffFiles.length; i++) {
        var pos = allXliffFiles[i].search(basename);
        if (pos > 0)
           retval.push(allXliffFiles[i]);
    }
    return retval;
}

// Get the language code from the xlfFile path and put it into the htmFile path
// (replacing the English language code) for an output file pathname.
var getOutputFilename = function(htmFile, xlfFile) {
    var langCode = "";
    if ( xlfFile.search("/ReadMe-") > 0)  // as in "blah/foo/ReadMe-en.htm"
        langCode = xlfFile.replace(/.*\/ReadMe-/, "").replace(".xlf","");
    else  // as in "blah/foo/fr/something.htm"
        langCode = xlfFile.split("/").slice(-2, -1)[0]; // penultimate item has the language code
    return htmFile.replace("-en.htm", "-" + langCode + ".htm");
}

// Get the name of the English Xliff file corresponding to the English HTML file.
var getXliffFilename = function(htmFile) {
    var htmPieces = htmFile.split("/");
    if (htmFile.includes("\\"))
        htmPieces = htmFile.split("\\");
    var basename = htmPieces[htmPieces.length - 1];
    if (basename == "ReadMe-en.htm") {
        return "../../DistFiles/localization/" + htmPieces[htmPieces.length - 2] + "/" + basename.replace(".htm", ".xlf");
    } else {
        return "../../DistFiles/localization/en/" + basename.replace(/-en\.html?$/, ".xlf");
    }
}