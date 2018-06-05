import * as React from "react";
import * as ReactDOM from "react-dom";
import { IUILanguageAwareProps } from "../../react_components/l10n";
import axios from "axios";
interface IProps extends IUILanguageAwareProps {
    apiCheckName: string;
    label: string;
}

// Each "CheckItem" conveys the status and results of a single automated accessibility test.

interface CheckResult {
    result: string;
    problems: string[];
}

interface IState {
    checkStatus: CheckResult;
}
export class CheckItem extends React.Component<IProps, IState> {
    constructor(props) {
        super(props);
        this.state = {
            checkStatus: { result: "pending", problems: [] }
        };
    }
    public componentDidMount() {
        axios
            .get(`/bloom/api/accessibilityCheck/${this.props.apiCheckName}`)
            .then(result => {
                this.setState({ checkStatus: result.data });
            })
            .catch(error => {
                this.setState({
                    checkStatus: {
                        result: "unknown",
                        problems: [error.response.statusText]
                    }
                });
            });
    }
    public render() {
        var line = this.props.label;
        return (
            <li className={`checkItem ${this.state.checkStatus.result}`}>
                {line}
                <ul>
                    {this.state.checkStatus.problems.map((problem, index) => (
                        //react requires unique keys on each
                        <li key={"p" + index}>{problem}</li>
                    ))}
                </ul>
            </li>
        );
    }
}
