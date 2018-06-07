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
    // this is passed, failed, unknown, or pending. Only the stylesheet cares, this code doesn't.
    resultClass: string;
    // for now, simple strings. Someday may be links to problem items.
    problems: string[];
}

interface IState {
    checkResult: CheckResult;
}
export class CheckItem extends React.Component<IProps, IState> {
    constructor(props) {
        super(props);
        this.state = {
            checkResult: { resultClass: "pending", problems: [] }
        };
    }
    public componentDidMount() {
        axios
            .get(`/bloom/api/accessibilityCheck/${this.props.apiCheckName}`)
            .then(result => {
                this.setState({ checkResult: result.data });
            })
            .catch(error => {
                this.setState({
                    checkResult: {
                        resultClass: "unknown",
                        problems: [error.response.statusText]
                    }
                });
            });
    }
    public render() {
        return (
            <li className={`checkItem ${this.state.checkResult.resultClass}`}>
                {
                    this.props.label // TODO Make localizable, just based on our props.apiCheckName
                }
                <ul>
                    {// problem descriptions are already localized by the backend
                    this.state.checkResult.problems.map((problem, index) => (
                        //react requires unique keys on each
                        <li key={"p" + index}>{problem}</li>
                    ))}
                </ul>
            </li>
        );
    }
}
