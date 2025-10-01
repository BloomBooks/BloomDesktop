/// <reference path="../typings/jquery/jquery.d.ts" />
import $ from "jquery";

interface JQuery {
    onSafe(eventName: string, data?: any, handler?: any);
}

/**
 * Like "on", but first does an "off" so that it can be called repeatedly without getting multiple event subscriptions
 * @param {String} eventName One or more space-separated event types and optional namespaces, such as "click" or "keydown.myPlugin".
 * @param data Optional. Data to be passed to the handler in event.data when an event is triggered.
 * @param [handler]
 * @returns {Object}
 */
$.fn.onSafe = function (eventName, data, handler) {
    this.each(function () {
        if (data) $(this).off(eventName).on(eventName, data, handler);
        else $(this).off(eventName).on(eventName, handler);
    });

    return this;
};
