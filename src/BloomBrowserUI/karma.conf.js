/* note: when you run karma, you'll get a failure for every <script> and <link> in the html, because
those start with "/bloom". While the Bloom server strips those off, karma doesn't. Seems like
Karma's "proxies" argument should be able to do that, but I haven't been successful.
*/


//note: if you change this, change it in gulpfile.js & webpack.config.js 
var outputDir ="../../output/browser";

module.exports = function (config) {
    config.set({
    
        // base path, that will be used to resolve files and exclude
        basePath: outputDir,
        frameworks: ['jasmine'],

        // list of files / patterns to load in the browser
        files: [           
            'bookEdit/test/GetSettingsMock.js', 
            'commonBundle.js',           
            'testBundle.js',   // If you want a test run, make sure it is in this bundle that webpack produces
                               // If you want a jasmine fixture to be served, make sure the name ends in Fixture.html
            { pattern: '**/*Fixture.html', included: false, served: true },
            { pattern: '**/*.js.map', included: false },
        ],

        preprocessors: {
        '**/*.js': ['sourcemap'], // this doesn't actually work, in the error message it generates, you can see the name of the original file
         '**/*.html': [] //prevent  karma from preprocessing html (blocks jasmine fixture feature) https://github.com/karma-runner/karma/issues/788
        },
        
        // test results reporter to use
        // possible values: 'spec' (nice list with checkboxes), 'dots', 'progress', 'junit', 'growl', 'coverage', 'teamcity'
        reporters: ['spec'],
        port: 9876,
        colors: true,
        
        // possible values: config.LOG_DISABLE || config.LOG_ERROR || config.LOG_WARN || config.LOG_INFO || config.LOG_DEBUG
        logLevel: config.LOG_INFO,
        autoWatch: true,
        
        // Start these browsers, currently available:
        // - Chrome IF YOU USE CHROME, NOTE THAT IF YOU MINIMIZE CHROME, IT WILL RUN TESTS SUPER SLOWLY
        // - Firefox
        // - PhantomJS
        browsers: ['Firefox'],

        // If browser does not capture in given timeout [ms], kill it
        captureTimeout: 6000,

        // Continuous Integration mode
        // if true, it capture browsers, run tests and exit
        singleRun: false
    });
};
