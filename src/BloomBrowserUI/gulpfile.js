/// <binding BeforeBuild='default' />
var gulp = require('gulp');  
var debug = require('gulp-debug');
var ts = require('gulp-typescript');
var less = require('gulp-less');
var jade = require('gulp-jade');
var path = require('path');
var sourcemaps = require('gulp-sourcemaps');
const babel = require('gulp-babel');
var browserify = require('gulp-browserify');
var webpack = require('gulp-webpack');
var del = require('del');
var runSequence = require('run-sequence');

var destination = './'; //this is a temporary measure, to put transpiled stuff in the same dir.
var output = "output"; //this is where we eventually want everything

var paths = {
   less: ['./**/*.less',  '!./node_modules/**/*.less'],
   jade: ['./**/*.jade',  '!./node_modules/**/*.jade'],
   //all Typescript in the toolbox are handled via the webpack task (haven't cleaned up the rest enough, yet)
   typescriptNotYetWebPacked: ['./**/*.ts','!./**/*.d.ts', '!./bookEdit/toolbox/**/*.ts']
};
//Currently we are putting all css's into the same directories as the less
//A next step would be to set it out to \output but that will require 
//changing where things are looked for at development time and on user machines,
//and some build/installer modifications
gulp.task('less', function () {
  return gulp.src(paths.less)
    .pipe(debug({title: 'less:'}))
    .pipe(sourcemaps.init())
    .pipe(less())
    .pipe(sourcemaps.write(destination))
    .pipe(gulp.dest(destination)); //drop all css's into the same dirs.
});

gulp.task('jade', function () {
  return gulp.src(paths.jade)
    .pipe(debug({title: 'jade:'}))
    .pipe(jade())
    .pipe(gulp.dest(destination)); //drop all css's into the same dirs.
});

var webpackconfig = require('./webpack.config.js');

gulp.task('webpack', function() {
  return gulp.src('unused') // webpack appears to ignore this since we're defining multiple entry points in webpack.config.js, which is good!
    .pipe(webpack( webpackconfig))
    .pipe(gulp.dest(output));
});

gulp.task('clean', function () {
  return del(["output/**/*", "output"]);
});

// gulp.task('jsx', () => {
// 	return gulp.src(paths.jsx)
// 		.pipe(babel({
// 			presets: ['es2015', "react"]
// 		}))
//         .pipe(browserify({
// 		  insertGlobals : true
// 		}))
// 		.pipe(gulp.dest(destination));
// });

gulp.task('typescript', function () {
  return gulp.src(paths.typescriptNotYetWebPacked)
    .pipe(debug({title: 'typescript:'}))
    .pipe(sourcemaps.init())
    .pipe(ts({
        target: "es5",// need to keep this down to the level that our gecko can directly handle. Things going through webpack+babel can target es6, fine. But not this other un-converted  stuff.
        module:"commonjs"
    }))
    .pipe(sourcemaps.write(destination))
    .pipe(gulp.dest(destination)); //drop all js's into the same dirs.
});

// Rerun the task when a file changes
gulp.task('watch', function() {
  gulp.watch(paths.typescriptNotYetWebPacked, ['typescript']),
  gulp.watch(paths.less, ['less']),
  gulp.watch(paths.jade, ['jade']);
});

gulp.task('default', 
    function(callback) { 
        //NB: run-sequence is needed for gulp 3.x, but soon there will be gulp which will have a built-in "series" function.
        runSequence('clean', ['typescript', 'less', 'jade', 'webpack'], callback)
});