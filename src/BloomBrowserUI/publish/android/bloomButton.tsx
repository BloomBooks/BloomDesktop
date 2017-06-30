import axios = require("axios");
import * as React from "react";
import * as ReactDOM from "react-dom";

interface ComponentProps {
    clickEndpoint: string;
}

interface ComponentState {
}

export default class ProgressBox extends React.Component<ComponentProps, ComponentState> {
    constructor(props) {
        super(props);
        let self = this;
        this.state = {};
    }
    render() {
        return (
            <button
            // onClick={() => axios.get<string>("/bloom/api/publish/" + clickEndpoint).then((response) => {
            //     todo run our onStateChanged and let the parent subscribe to that. this.setState({ stateId: response.data });
            // })}
            >
                {/* TODO: Make localizable. Perhaps something like
                        localize({id:'publish.android.connectWithUSB', comment:'button label' en:'Connect with USB cable'})
                        Or a react component, like
                        <String id='publish.android.connectWithUSB' comment='button label'>
                            Connect with USB cable
                        </String>
                    */}
                Some Text
                </button>
        );
    }
}
