///<reference path="typings/axios/axios.d.ts"/>
import axios = require('axios');
export default class BloomHelp {
    /**
   * Opens the application help topic
   * @param topic
   * @returns {boolean} Returns false to prevent navigation if link clicked.
   */
    static show(topic: string): boolean {
        axios.post('/bloom/help', topic);
        return false;
    }
}
