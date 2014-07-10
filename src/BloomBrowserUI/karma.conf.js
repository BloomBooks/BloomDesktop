// Karma configuration
// Generated on Wed Oct 23 2013 16:38:14 GMT-0700 (Pacific Daylight Time)

module.exports = function (config) {
    config.set({

        // base path, that will be used to resolve files and exclude
        basePath: '.',

        // frameworks to use
        frameworks: ['jasmine'],

        // list of files / patterns to load in the browser
        files: [
            'lib/jquery-1.10.1.js',
            'lib/jquery-ui-1.10.3.custom.min.js',
            'lib/jquery.myimgscale.js',

            // helpers -- jasmine-query
            'test/lib/**/*.js',

            // fixtures
            { pattern: 'test/fixtures/**/*.htm', included: false, served: true },
            //enhance: if we just say *.js, we get lots of errors as various library js files are all dumped in.
            //But this way here, of specifying each one we're actually testing, is obviously flawed too.
            //One idea would be to have TypeScript compiler name files such that we auto-include all the typescript-created js files,
            //under the assumption that anything we want to test will be written with typescript (at least eventually)
            '**/js/bloomBootstrap.js',
            '**/js/bloomEditing.js',
            '**/js/StyleEditor.js',
            // as long as the test filename is in the test/specs folder, it will be included in the test run
            'test/specs/**/*.js',

            // synphony files
            'bookEdit/js/libsynphony/underscore_min_152.js',
            'bookEdit/js/libsynphony/xregexp-all-min.js',
            'bookEdit/js/libsynphony/synphony_lib.js',
            'bookEdit/js/libsynphony/bloom_xregexp_categories.js',
            'bookEdit/js/libsynphony/bloom_lib.js',
            'bookEdit/js/libsynphony/jquery.text-markup.js',
            'bookEdit/js/*.js',
            'bookEdit/accordion/readerTools.js',
            'bookEdit/test/*.js',
            'bookEdit/test/libsynphony/*.test.js'
        ],
        // test results reporter to use
        // possible values: 'dots', 'progress', 'junit', 'growl', 'coverage'
        reporters: ['progress'],


        // web server port
        port: 9876,


        // enable / disable colors in the output (reporters and logs)
        colors: true,


        // level of logging
        // possible values: config.LOG_DISABLE || config.LOG_ERROR || config.LOG_WARN || config.LOG_INFO || config.LOG_DEBUG
        logLevel: config.LOG_INFO,


        // enable / disable watching file and executing tests whenever any file changes
        autoWatch: true,


        // Start these browsers, currently available:
        // - Chrome IF YOU USE CHROME, NOTE THAT IF YOU MINIMIZE CHROME, IT WILL RUN TESTS SUPER SLOWLY
        // - ChromeCanary
        // - Firefox
        // - Opera
        // - Safari (only Mac)
        // - PhantomJS
        // - IE (only Windows)
        browsers: ['Chrome'],


        // If browser does not capture in given timeout [ms], kill it
        captureTimeout: 60000,


        // Continuous Integration mode
        // if true, it capture browsers, run tests and exit
        singleRun: false
    });
};
