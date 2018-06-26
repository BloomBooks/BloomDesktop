/// <reference path="typings/jquery/jquery.d.ts" />
import { BloomApi } from "./utils/bloomApi";
/**
 * Class to with methods related to invoking bloom help
 * @constructor
 */
export default class BloomHelp {
    /**
 * Opens the application help topic
 * @param topic
 * @returns {boolean} Returns false to prevent navigation if link clicked.
 */
    static show(topic: string): boolean {
        BloomApi.postData("help", topic);
        return false;
    }
}
