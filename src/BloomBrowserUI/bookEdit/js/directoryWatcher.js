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
        this.changeEventHandlers = {};
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
    * @param responseData 'yes' = changed, 'no' = not changed
    * @param {DirectoryWatcher} self
    */
    DirectoryWatcher.prototype.ifChangedFireEvents = function (responseData, self) {
        var changed = (responseData === 'yes');

        // if there were changes, call the registered onChanged handlers
        if (changed) {
            var handlers = Object.keys(self.changeEventHandlers);
            for (var j = 0; j < handlers.length; j++)
                self.changeEventHandlers[handlers[j]]();
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
            self.ifChangedFireEvents(data, self);
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
