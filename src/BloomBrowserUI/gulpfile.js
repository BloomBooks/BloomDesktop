/// <binding />
var gulp = require('gulp');  
var debug = require('gulp-debug');
var ts = require('gulp-typescript');
var less = require('gulp-less');
var jade = require('gulp-jade');
var batch = require('gulp-batch');
var watch = require('gulp-watch');
var path = require('path');
var sourcemaps = require('gulp-sourcemaps');
const babel = require('gulp-babel');
var browserify = require('gulp-browserify');
var webpack = require('gulp-webpack');
var del = require('del');
var runSequence = require('run-sequence');
var gulpCopy = require('gulp-copy');

//this is where we eventually want everything. Note: the same value must be in the webpack config. 
//This one is really only used for 'clean';
var outputDir = "../../output/browser"; 

//todo: can remove these output exlusions now that output/ is now 2 levels up with the c# outpuuts
var paths = {
   less: ['./**/*.less',  '!./node_modules/**/*.less','!./output/**/*.*'],
   jade: ['./**/*.jade',  '!./node_modules/**/*.jade','!./output/**/*.*'],
   typescript: ['./**/*.ts','!./**/*.d.ts', '!./**/node_modules/**/*.*','!./output/**/*.*'],
   
   //files we are *not* running through some compiler that need to make it into the outputDir directory.
   filesThatMightBeNeededInOutput: ['./**/*.*', '!./**/*.ts','!./**/node_modules/**/*.*','!./output/**/*.*'],
   
   
   node_modulesCalledDirectlyAtRuntime: ['./node_modules/jquery/dist/jquery.min.js']
};

gulp.task('less', function () {
  return gulp.src(paths.less)
    .pipe(debug({title: 'less:'}))
    .pipe(sourcemaps.init())
    .pipe(less())
    .pipe(sourcemaps.write(outputDir))
    .pipe(gulp.dest(outputDir)); //drop all css's into the same dirs.
});

gulp.task('jade', function () {
  return gulp.src(paths.jade)
    .pipe(debug({title: 'jade:'}))
    .pipe(jade())
    .pipe(gulp.dest(outputDir)); //drop all css's into the same dirs.
});

var webpackconfig = require('./webpack.config.js');

gulp.task('webpack', function() {
  return gulp.src('unused') // webpack appears to ignore this since we're defining multiple entry points in webpack.config.js, which is good!
    .pipe(webpack( webpackconfig))
    .pipe(gulp.dest(outputDir));
});

gulp.task('clean', function () {
  return del([outputDir+"/**/*"], {force:true});
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
  gulp.src(paths.node_modulesCalledDirectlyAtRuntime)
    .pipe(gulpCopy(outputDir));
  return gulp.src(paths.filesThatMightBeNeededInOutput)
    .pipe(gulpCopy(outputDir))
});
gulp.task('c', function () {
  return gulp.src(paths.node_modulesCalledDirectlyAtRuntime)
    .pipe(gulpCopy(outputDir))
});


gulp.task('typescript', function () {
  return gulp.src(paths.typescript)
    .pipe(debug({title: 'typescript:'}))
    .pipe(sourcemaps.init())
    .pipe(ts({
        target: "es5",// need to keep this down to the level that our gecko can directly handle. Things going through webpack+babel can target es6, fine. But not this other un-converted  stuff.
        module:"commonjs"
    }))
    .pipe(sourcemaps.write(outputDir))
    .pipe(gulp.dest(outputDir)); //drop all js's into the same dirs.
});

gulp.task('watchInner', function() {
    watch(paths.less, batch(function (events, done) {
        gulp.start('copy', done);
    }));    
    watch(paths.less, batch(function (events, done) {
        gulp.start('typescript', done);
    }));
    watch(paths.less, batch(function (events, done) {
        gulp.start('less', done);
    }));
    watch(paths.jade, batch(function (events, done) {
        gulp.start('jade', done);
    }));
})


gulp.task('watch', function() {
      console.log('****** PLEASE run "webpack --watch" in a separate console *********');     
      runSequence('clean', 'copy',  [ 'less', 'jade',  'typescript'],'watchInner');
});


gulp.task('code', function() {
    runSequence(['copy','typescript']);
});

gulp.task('default', 
    function(callback) { 
        //NB: run-sequence is needed for gulp 3.x, but soon there will be gulp which will have a built-in "series" function.
        //currently our webpack run is pure javascript, so do it only after the typescript is all done
         runSequence('clean', 'copy',  [ 'less', 'jade',  'typescript'],'webpack', callback)
});
    