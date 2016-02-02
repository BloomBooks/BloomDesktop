/* note: when you run karma, you'll get a failure for every <script> and <link> in the html, because
those start with "/bloom". While the Bloom server strips those off, karma doesn't. Seems like
Karma's "proxies" argument should be able to do that, but I haven't been successful.
*/

module.exports = function (config) {
    config.set({
        // base path, that will be used to resolve files and exclude
        basePath: '.',
        frameworks: ['jasmine'],

        // list of files / patterns to load in the browser
        files: [           
            'bookEdit/test/cSharpDependencyInjector.js', //defines a global GetSettings()
            'output/commonCode.js',           
            'output/testBundle.js',           
            // fixtures
            { pattern: 'test/fixtures/**/*.html', included: false, served: true },
        ],

        preprocessors: {
        '**/*.js': ['sourcemap'],
         '**/*.html': [] //prevent  karma from preprocessing html (blocks jasmine fixture feature) https://github.com/karma-runner/karma/issues/788
        },
                
        // test results reporter to use
        // possible values: 'dots', 'progress', 'junit', 'growl', 'coverage', 'teamcity'
        reporters: ['progress'],
        port: 9876,
        colors: true,
        
        // possible values: config.LOG_DISABLE || config.LOG_ERROR || config.LOG_WARN || config.LOG_INFO || config.LOG_DEBUG
        logLevel: config.LOG_WARN,
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
