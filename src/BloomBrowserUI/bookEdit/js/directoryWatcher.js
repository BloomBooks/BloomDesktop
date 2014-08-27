/// <reference path="../../lib/jquery.d.ts" />
/// <reference path="libsynphony/underscore-1.5.2.d.ts" />
/**
* Implements a simple directory watcher in javascript. The localhost is listening for requests that begin with this
* pattern: "/bloom/directoryWatcher/..."
*/
var DirectoryWatcher = (function () {
    /**
    * @param {String} directoryToWatch
    * @param {Number} [refreshIntervalSeconds] If missing or less than one, automatic updating will be disabled.
    */
    function DirectoryWatcher(directoryToWatch, refreshIntervalSeconds) {
        this.refreshInterval = 0;
        this.previousData = {};
        this.changeEventHandlers = {};
        this.initialRunComplete = false;
        this.directoryToWatch = directoryToWatch;

        if ((typeof refreshIntervalSeconds !== 'undefined') || (refreshIntervalSeconds > 0))
            this.refreshInterval = Math.ceil(refreshIntervalSeconds);
    }
    DirectoryWatcher.prototype.start = function () {
        this.run = true;
        this.checkNow(this);
    };

    DirectoryWatcher.prototype.stop = function () {
        this.run = false;
    };

    /**
    * Sends request to localhost
    */
    DirectoryWatcher.prototype.checkNow = function (self) {
        var postData = { dir: self.directoryToWatch };
        var url = '/bloom/directoryWatcher/';
        this.watcherAjaxPost(url, self, postData);
    };

    /**
    * Process the headers returned.
    * @param responseData A list of file names and header data (Content-Length and Last-Modified).
    * Example: { "file1.txt": [ 1024, "2012-04-23T18:25:43.511Z" ], "file2.txt": [ 2048, "2012-04-23T18:25:43.511Z" ] }
    * @param {DirectoryWatcher} self
    */
    DirectoryWatcher.prototype.compareNewAndOld = function (responseData, self) {
        // there will be nothing to compare the first time
        if (!self.initialRunComplete) {
            self.initialRunComplete = true;
            self.previousData = responseData;
            self.restartTimer(self);
            return;
        }

        var newKeys = Object.keys(responseData);
        var oldKeys = Object.keys(self.previousData);

        // check for new files
        var newFiles = _.difference(newKeys, oldKeys);

        // check for deleted files
        var deletedFiles = _.difference(oldKeys, newKeys);

        // check for changed files
        var changedFiles = [];
        var filesToCheck = _.intersection(newKeys, oldKeys);

        for (var i = 0; i < filesToCheck.length; i++) {
            // key is the file name
            var key = filesToCheck[i];
            var newInfo = responseData[key];
            var oldInfo = this.previousData[key];

            // index 0 = file size
            // index 1 = last modified timestamp
            if ((newInfo[0] !== oldInfo[0]) || (newInfo[1] !== oldInfo[1]))
                changedFiles.push(key);
        }

        // remember for next time
        self.previousData = responseData;

        // was there a change?
        var changed = ((newFiles.length > 0) || (deletedFiles.length > 0) || (changedFiles.length > 0));

        // if there were changes, call the registered onChanged handlers
        if (changed) {
            var handlers = Object.keys(self.changeEventHandlers);
            for (var j = 0; j < handlers.length; j++)
                self.changeEventHandlers[handlers[j]](newFiles, deletedFiles, changedFiles);
        }

        self.restartTimer(self);
    };

    /**
    * Restarts the timer, if needed.
    * @param {DirectoryWatcher} self
    */
    DirectoryWatcher.prototype.restartTimer = function (self) {
        if (self.run === true) {
            if (self.refreshInterval > 0) {
                setTimeout(function () {
                    self.checkNow(self);
                }, self.refreshInterval * 1000);
            }
        }
    };

    /**
    * Retrieve data from localhost
    * @param {String} url The URL to request
    * @param {DirectoryWatcher} self
    * @param {Object} [postKeyValueDataObject] Values passed in the post.
    */
    DirectoryWatcher.prototype.watcherAjaxPost = function (url, self, postKeyValueDataObject) {
        var ajaxSettings = { type: 'POST', url: url };
        if (postKeyValueDataObject)
            ajaxSettings['data'] = postKeyValueDataObject;

        // If/when the ajax call returns a response, the entire contents of the response
        // will be passed to the function that was passed in the "callback" parameter.
        // The data can be almost anything: an html document, a json object, a single
        // string or number, etc., whatever the "callback" function is expecting.
        $.ajax(ajaxSettings).done(function (data) {
            self.compareNewAndOld(data, self);
        });
    };

    /**
    * Adds a listener for the changed event.
    * @param listenerNameAndContext Name and context that identifies this handler. The handler will be called with
    * these parameters: handler(newFiles, deletedFiles, changedFiles)
    * @param handler Function to call
    */
    DirectoryWatcher.prototype.onChanged = function (listenerNameAndContext, handler) {
        this.changeEventHandlers[listenerNameAndContext] = handler;
    };

    /**
    * Removes a listener for the changed event.
    * @param listenerNameAndContext Name and context that identifies the handler to remove.
    */
    DirectoryWatcher.prototype.offChanged = function (listenerNameAndContext) {
        if (this.changeEventHandlers.hasOwnProperty(listenerNameAndContext))
            delete this.changeEventHandlers[listenerNameAndContext];
    };
    return DirectoryWatcher;
})();
//# sourceMappingURL=directoryWatcher.js.map
