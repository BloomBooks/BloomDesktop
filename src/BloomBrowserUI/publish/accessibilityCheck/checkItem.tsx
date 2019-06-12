import * as React from "react";
import {
    IUILanguageAwareProps,
    Label
} from "../../react_components/l10nComponents";
import axios from "axios";

interface IProps extends IUILanguageAwareProps {
    apiCheckName: string;
    label: string;
    // The parent can give us this function which we use to subscribe to refresh events
    // See notes in accessibiltiyChecklist for a thorough discussion.
    subscribeToRefresh?: (queryData: () => void) => void;
}

// Each "CheckItem" conveys the status and results of a single automated accessibility test.
// this should match the struct Problem in AccessibilityCheckers.cs
interface IProblem {
    message: string;
    problemText: string;
}
interface CheckResult {
    // this is passed, failed, unknown, or pending. Only the stylesheet cares, this code doesn't.
    resultClass: string;
    // for now, simple strings. Someday may be links to problem items.
    problems: IProblem[];
}

interface IState {
    checkResult: CheckResult;
}
export class CheckItem extends React.Component<IProps, IState> {
    public readonly state: IState = {
        checkResult: { resultClass: "pending", problems: [] }
    };

    public componentDidMount() {
        this.queryData();

        if (this.props.subscribeToRefresh) {
            this.props.subscribeToRefresh(() => this.queryData());
        }
    }

    private queryData() {
        axios
            .get(`/bloom/api/accessibilityCheck/${this.props.apiCheckName}`)
            .then(result => {
                this.setState({ checkResult: result.data });
            })
            .catch(error => {
                this.setState({
                    checkResult: {
                        resultClass: "unknown",
                        problems: [
                            {
                                // note "file not found" here may have nothing to do with files
                                // it may just be a poor choice of return codes.
                                message: `Error from Bloom Server: ${
                                    error.message
                                } ${error.response.statusText}`,
                                problemText: ""
                            }
                        ]
                    }
                });
            });
    }

    public render() {
        let labelKey = "AccessibilityCheck." + this.props.apiCheckName;
        return (
            <li className={`checkItem ${this.state.checkResult.resultClass}`}>
                <Label l10nKey={labelKey}>{this.props.label}</Label>
                <ul>
                    {// problem descriptions are already localized by the backend
                    this.state.checkResult.problems.map((problem, index) => (
                        //react requires unique keys on each
                        <li key={"p" + index}>
                            {problem.message}
                            {problem.problemText ? (
                                <blockquote>{problem.problemText}</blockquote>
                            ) : (
                                ""
                            )}
                        </li>
                    ))}
                </ul>
            </li>
        );
    }
}
