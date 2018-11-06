/// <reference path="../../../typings/jquery/jquery.d.ts" />

/**
 * Implements a simple directory watcher in javascript. The localhost is listening for requests that begin with this
 * pattern: "/bloom/api/directoryWatcher/..."
 */
export class DirectoryWatcher {
    private directoryToWatch: string;
    private refreshInterval: number = 0;
    private changeEventHandlers = {};
    private run: boolean;

    /**
     * @param {String} directoryToWatch
     * @param {Number} [refreshIntervalSeconds] If missing or less than one, automatic updating will be disabled.
     */
    constructor(directoryToWatch: string, refreshIntervalSeconds: number) {
        this.directoryToWatch = directoryToWatch;

        if (
            typeof refreshIntervalSeconds !== "undefined" &&
            refreshIntervalSeconds > 0
        )
            this.refreshInterval = Math.ceil(refreshIntervalSeconds);
    }

    public start(): void {
        this.run = true;
        this.checkNow(this);
    }

    public stop(): void {
        this.run = false;
    }

    /**
     * Sends request to localhost
     */
    public checkNow(self): void {
        const postData = { dir: self.directoryToWatch };
        const url = "/bloom/api/directoryWatcher/";
        this.watcherAjaxPost(url, self, postData);
    }

    /**
     * Process the headers returned.
     * @param responseData 'yes' = changed, 'no' = not changed
     * @param {DirectoryWatcher} self
     */
    public ifChangedFireEvents(responseData, self): void {
        var changed = responseData === "yes";

        // if there were changes, call the registered onChanged handlers
        if (changed) {
            var handlers = Object.keys(self.changeEventHandlers);
            for (var j = 0; j < handlers.length; j++)
                self.changeEventHandlers[handlers[j]]();
        }

        self.restartTimer(self);
    }

    /**
     * Restarts the timer, if needed.
     * @param {DirectoryWatcher} self
     */
    private restartTimer(self): void {
        if (self.run === true) {
            if (self.refreshInterval > 0) {
                setTimeout(() => {
                    self.checkNow(self);
                }, self.refreshInterval * 1000);
            }
        }
    }

    /**
     * Retrieve data from localhost
     * @param {String} url The URL to request
     * @param {DirectoryWatcher} self
     * @param {Object} [postKeyValueDataObject] Values passed in the post.
     */
    public watcherAjaxPost(url, self, postKeyValueDataObject): void {
        var ajaxSettings = { type: "POST", url: url };
        if (postKeyValueDataObject)
            ajaxSettings["data"] = postKeyValueDataObject;

        // we are expecting the value returned in 'data' to be either 'yes' or 'no'
        $.ajax(ajaxSettings).done(data => {
            self.ifChangedFireEvents(data, self);
        });
    }

    /**
     * Adds a listener for the changed event.
     * @param listenerNameAndContext Name and context that identifies this handler.
     * @param handler Function to call
     */
    public onChanged(listenerNameAndContext: string, handler: () => any): void {
        this.changeEventHandlers[listenerNameAndContext] = handler;
    }

    /**
     * Removes a listener for the changed event.
     * @param listenerNameAndContext Name and context that identifies the handler to remove.
     */
    public offChanged(listenerNameAndContext: string): void {
        if (this.changeEventHandlers.hasOwnProperty(listenerNameAndContext))
            delete this.changeEventHandlers[listenerNameAndContext];
    }
}
