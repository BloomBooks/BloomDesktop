import * as React from "react";
import { css } from "@emotion/react";
import { getBoolean, post, postBoolean, postJson } from "../../utils/bloomApi";
import Menu from "@mui/material/Menu";
import Divider from "@mui/material/Divider";
import {
    LocalizableMenuItem,
    LocalizableSelectableMenuItem,
} from "../localizableMenuItem";
import { useMountEffect } from "../../utils/useMountEffect";

interface ITopBarContextMenuPoint {
    clientX: number;
    clientY: number;
}

interface ITopBarContextMenuItem {
    label: string;
    enabled?: boolean;
    selected?: boolean;
    onClick?: () => void;
}

export const TopBarContextMenu: React.FunctionComponent<{
    targetRef: React.RefObject<HTMLDivElement>;
}> = (props) => {
    const [menuPoint, setMenuPoint] = React.useState<ITopBarContextMenuPoint>();
    const [currentlyMeasuring, setCurrentlyMeasuring] = React.useState(false);
    const [alwaysMeasurePerformance, setAlwaysMeasurePerformance] =
        React.useState(false);
    const [isMeddlingWithNewFiles, setIsMeddlingWithNewFiles] =
        React.useState(false);

    const onClose = React.useCallback(() => {
        setMenuPoint(undefined);
    }, []);

    useMountEffect(() => {
        // We only read these once to discover the starting state. After that,
        // this menu is the only thing that changes them, so local state stays authoritative.
        getBoolean("app/currentlyMeasuringPerformance", (value) => {
            setCurrentlyMeasuring(value);
        });
        getBoolean("app/alwaysMeasurePerformance", (value) => {
            setAlwaysMeasurePerformance(value);
        });
        getBoolean("app/isMeddlingWithNewFiles", (value) => {
            setIsMeddlingWithNewFiles(value);
        });

        const target = props.targetRef.current;
        if (!target) {
            return;
        }

        const handleTopBarContextMenu = (event: MouseEvent) => {
            // Don't block the Ctrl+RightClick context menu
            if (event.ctrlKey) {
                return;
            }

            event.preventDefault();
            event.stopPropagation();

            setMenuPoint({
                clientX: event.clientX,
                clientY: event.clientY,
            });
        };

        target.addEventListener("contextmenu", handleTopBarContextMenu);

        return () => {
            target.removeEventListener("contextmenu", handleTopBarContextMenu);
        };
    });

    function handleResizeWindow(width: number, height: number) {
        postJson("app/resizeWindow", {
            width,
            height,
        });
    }

    const menuItems = React.useMemo<ITopBarContextMenuItem[]>(() => {
        return [
            {
                label: "1024 x 586 Low-end netbook with windows Task bar",
                onClick: () => {
                    handleResizeWindow(1024, 586);
                },
            },
            { label: "-" },
            {
                label: "800 x 600",
                onClick: () => {
                    handleResizeWindow(800, 600);
                },
            },
            {
                label: "1024 x 600",
                onClick: () => {
                    handleResizeWindow(1024, 600);
                },
            },
            {
                label: "1024 x 768",
                onClick: () => {
                    handleResizeWindow(1024, 768);
                },
            },
            { label: "-" },
            {
                label: "Always Measure Performance",
                selected: alwaysMeasurePerformance,
                onClick: () => {
                    const newValue = !alwaysMeasurePerformance;
                    postBoolean("app/alwaysMeasurePerformance", newValue);
                    setAlwaysMeasurePerformance(newValue);
                    if (newValue) {
                        setCurrentlyMeasuring(true);
                    }
                },
            },
            {
                label: currentlyMeasuring
                    ? "Currently Measuring Performance"
                    : "Start Measuring Performance",
                enabled: !currentlyMeasuring,
                onClick: () => {
                    post("app/startMeasuringPerformance");
                    setCurrentlyMeasuring(true);
                },
            },
            {
                label: "Show Performance Page",
                enabled: currentlyMeasuring,
                onClick: () => {
                    post("app/showPerformancePage");
                },
            },
            {
                label: isMeddlingWithNewFiles
                    ? "Stop Meddling with New Files"
                    : "Meddle with New Files",
                onClick: () => {
                    const newValue = !isMeddlingWithNewFiles;
                    postBoolean("app/isMeddlingWithNewFiles", newValue);
                    setIsMeddlingWithNewFiles(newValue);
                },
            },
        ];
    }, [alwaysMeasurePerformance, currentlyMeasuring, isMeddlingWithNewFiles]);

    return (
        <Menu
            keepMounted={true}
            open={!!menuPoint}
            onClose={onClose}
            anchorReference="anchorPosition"
            anchorPosition={
                menuPoint
                    ? {
                          top: menuPoint.clientY,
                          left: menuPoint.clientX,
                      }
                    : undefined
            }
            slotProps={{
                paper: {
                    css: css`
                        min-width: 220px;
                        max-width: 440px;
                    `,
                },
            }}
        >
            {menuItems.map((item, index) => {
                if (item.label === "-") {
                    return <Divider key={`separator-${index}`} />;
                }

                const commonProps = {
                    key: `${item.label ?? index}`,
                    english: item.label,
                    l10nId: null,
                    onClick: () => {
                        item.onClick?.();
                        onClose();
                    },
                    disabled: item.enabled === false,
                };

                return item.selected !== undefined ? (
                    <LocalizableSelectableMenuItem
                        {...commonProps}
                        selected={item.selected}
                    />
                ) : (
                    <LocalizableMenuItem
                        {...commonProps}
                        hasLeadingIconSpace={true}
                    />
                );
            })}
        </Menu>
    );
};
