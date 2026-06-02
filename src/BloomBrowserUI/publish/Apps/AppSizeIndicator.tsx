import { css } from "@emotion/react";
import { Alert, Tooltip } from "@mui/material";
import * as React from "react";
import { kBloomBlue, kBloomRed } from "../../bloomMaterialUITheme";
import { useL10n } from "../../react_components/l10nHooks";

export interface IAppSizeEstimateBook {
    bookId: string;
    folderPath: string;
    title: string;
    sizeBytes: number;
    isActual?: boolean;
}

export interface IAppSizeEstimates {
    books: IAppSizeEstimateBook[];
    estimatedAppOverheadBytes: number;
    maxAppSizeBytes: number;
}

export interface IAppSizeSelectionBook {
    bookId?: string;
    folderPath?: string;
    title?: string;
}

export const defaultAppSizeEstimates: IAppSizeEstimates = {
    books: [],
    estimatedAppOverheadBytes: 12 * 1000 * 1000,
    maxAppSizeBytes: 100 * 1000 * 1000,
};

interface IAppSizeSegment {
    key: string;
    label: string;
    sizeBytes: number;
    visibleBytes: number;
    color: string;
    isActual?: boolean;
}

interface IAppSizeSummary {
    totalBytes: number;
    maxBytes: number;
    exceedsMax: boolean;
    segments: IAppSizeSegment[];
}

function formatSize(bytes: number, useUppercaseUnit = false): string {
    const megabytes = bytes / 1000 / 1000;
    const unit = useUppercaseUnit ? "MB" : "mb";

    if (megabytes >= 10) {
        return `${Math.round(megabytes)} ${unit}`;
    }

    if (megabytes >= 1) {
        return `${megabytes.toFixed(1)} ${unit}`;
    }

    return `${megabytes.toFixed(2)} ${unit}`;
}

function getBookEstimate(
    sizeEstimates: IAppSizeEstimates,
    selectedBook: IAppSizeSelectionBook,
): IAppSizeEstimateBook | undefined {
    return sizeEstimates.books.find(
        (book) =>
            (!!selectedBook.bookId && book.bookId === selectedBook.bookId) ||
            (!!selectedBook.folderPath &&
                book.folderPath === selectedBook.folderPath),
    );
}

function buildSummary(
    sizeEstimates: IAppSizeEstimates,
    books: IAppSizeSelectionBook[],
    overheadLabel: string,
): IAppSizeSummary {
    const maxBytes = sizeEstimates.maxAppSizeBytes;
    const sourceSegments: Array<{
        key: string;
        label: string;
        sizeBytes: number;
        kind: "overhead" | "book";
        isActual?: boolean;
    }> = [
        {
            key: "overhead",
            label: overheadLabel,
            sizeBytes: sizeEstimates.estimatedAppOverheadBytes,
            kind: "overhead",
        },
        ...books.map((book, index) => {
            const estimate = getBookEstimate(sizeEstimates, book);
            return {
                key:
                    book.bookId ||
                    book.folderPath ||
                    `${book.title || "book"}-${index}`,
                label: book.title || estimate?.title || `Book ${index + 1}`,
                sizeBytes: estimate?.sizeBytes ?? 0,
                kind: "book" as const,
                isActual: estimate?.isActual,
            };
        }),
    ];

    const totalBytes = sourceSegments.reduce(
        (sum, segment) => sum + segment.sizeBytes,
        0,
    );
    const exceedsMax = totalBytes > maxBytes;
    let shownBytes = 0;

    const segments = sourceSegments
        .map((segment) => {
            // Cap the visible bar at the Android limit while still reporting the real total below it.
            const remainingBytes = Math.max(0, maxBytes - shownBytes);
            const visibleBytes = Math.min(segment.sizeBytes, remainingBytes);
            shownBytes += visibleBytes;

            if (visibleBytes <= 0) {
                return undefined;
            }

            const reachesLimit = shownBytes >= maxBytes;
            const color =
                segment.kind === "overhead"
                    ? "#757575"
                    : exceedsMax && reachesLimit
                      ? kBloomRed
                      : kBloomBlue;

            return {
                key: segment.key,
                label: segment.label,
                sizeBytes: segment.sizeBytes,
                visibleBytes,
                color,
                isActual: segment.isActual,
            };
        })
        .filter(Boolean) as IAppSizeSegment[];

    return {
        totalBytes,
        maxBytes,
        exceedsMax,
        segments,
    };
}

export const EstimatedAppSizeIndicator: React.FunctionComponent<{
    sizeEstimates: IAppSizeEstimates;
    books: IAppSizeSelectionBook[];
}> = (props) => {
    const overheadLabel = useL10n(
        "App overhead",
        "PublishTab.Apps.SizeEstimate.OverheadLabel",
    );
    const estimateTemplate = useL10n(
        "Apps have a maximum size of about %1. The current estimate for this app is %0.",
        "PublishTab.Apps.SizeEstimate.Total",
    );
    const overLimitText = useL10n(
        "This estimate is above the %0 Android app size limit. Choose fewer books or smaller books.",
        "PublishTab.Apps.SizeEstimate.OverLimit",
    );
    const summary = buildSummary(
        props.sizeEstimates,
        props.books,
        overheadLabel,
    );

    return (
        <div
            css={css`
                margin-top: 12px;
                max-width: 900px;
            `}
        >
            <div
                css={css`
                    width: min(640px, 100%);
                `}
            >
                <div
                    css={css`
                        display: flex;
                        height: 33px;
                        border: 1px solid darkgray;
                        border-radius: 0;
                        overflow: hidden;
                        background: #fff;
                    `}
                >
                    {summary.segments.map((segment, index) => {
                        const widthPercent =
                            summary.maxBytes > 0
                                ? (segment.visibleBytes / summary.maxBytes) *
                                  100
                                : 0;

                        return (
                            <Tooltip
                                key={segment.key}
                                title={`${segment.label}: ${formatSize(segment.sizeBytes)}${segment.isActual === true ? " (actual)" : segment.isActual === false ? " (est)" : ""}`}
                                placement="top"
                            >
                                <div
                                    css={css`
                                        width: ${widthPercent}%;
                                        background: ${segment.color};
                                        border-right: ${index <
                                        summary.segments.length - 1
                                            ? "2px solid rgba(255, 255, 255, 0.9)"
                                            : "none"};
                                        min-width: ${segment.visibleBytes > 0
                                            ? "6px"
                                            : "0"};
                                    `}
                                    aria-label={`${segment.label}: ${formatSize(segment.sizeBytes)}${segment.isActual === true ? " (actual)" : segment.isActual === false ? " (est)" : ""}`}
                                />
                            </Tooltip>
                        );
                    })}
                </div>
                <div
                    css={css`
                        margin-top: 12px;
                    `}
                >
                    {estimateTemplate
                        .replace("%0", formatSize(summary.totalBytes))
                        .replace("%1", formatSize(summary.maxBytes))}
                </div>
            </div>
            {summary.exceedsMax && (
                <Alert
                    severity="warning"
                    css={css`
                        margin-top: 12px;
                        width: min(640px, 100%);
                    `}
                >
                    {overLimitText.replace("%0", formatSize(summary.maxBytes))}
                </Alert>
            )}
        </div>
    );
};

export const ActualApkSizeText: React.FunctionComponent<{
    apkSizeBytes: number;
}> = (props) => {
    const summaryTemplate = useL10n(
        "Actual app size is %0.",
        "PublishTab.Apps.ApkSize.Text",
    );

    return (
        <div
            css={css`
                margin-top: 16px;
                max-width: 900px;
            `}
        >
            <div>
                {summaryTemplate.replace(
                    "%0",
                    formatSize(props.apkSizeBytes, true),
                )}
            </div>
        </div>
    );
};
