import { css } from "@emotion/react";
import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { Button } from "@mui/material";
import * as React from "react";
import { kBloomBlue } from "../../../../utils/colorUtils";
import { ReaderSettings, ReaderStage } from "../ReaderSettings";
import { cleanSpaceDelimitedList } from "./decodableStagesUtils";
import {
    closestCenter,
    DndContext,
    DragEndEvent,
    PointerSensor,
    useSensor,
    useSensors,
} from "@dnd-kit/core";
import {
    arrayMove,
    SortableContext,
    verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import AddIcon from "@mui/icons-material/Add";
import DeleteOutlineIcon from "@mui/icons-material/DeleteOutline";
import { Span } from "../../../../react_components/l10nComponents";
import { cloneReaderSettings } from "./decodableStagesUtils";
import axios from "axios";
import { getBloomApiPrefix } from "../../../../utils/bloomApi";

/** Displays one sortable decodable-reader stage. */
const DraggablePhaseRow: React.FunctionComponent<{
    id: string;
    index: number;
    stage: ReaderStage;
    isSelected: boolean;
    useAllowedWords: boolean;
    fontName: string;
    onSelect: () => void;
}> = (props) => {
    const {
        attributes,
        listeners,
        setNodeRef,
        transform,
        transition,
        isDragging,
    } = useSortable({ id: props.id });

    return (
        <div
            ref={setNodeRef}
            style={{
                transform: CSS.Transform.toString(transform),
                transition,
                opacity: isDragging ? 0.5 : 1,
            }}
            onClick={props.onSelect}
            onKeyDown={(event) => {
                if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault();
                    props.onSelect();
                }
            }}
            {...attributes}
            {...listeners}
            css={css`
                position: relative;
                display: grid;
                width: 100%;
                box-sizing: border-box;
                grid-template-columns: ${props.useAllowedWords
                    ? "76px minmax(0, 1fr)"
                    : "76px 88px minmax(0, 1fr)"};
                align-items: start;
                min-height: 42px;
                padding: 10px 14px;
                border: 0;
                border-bottom: 1px solid #eeeeee;
                background: ${props.isSelected ? "#e4f3f4" : "white"};
                color: #202020;
                font-size: 11pt;
                font-weight: 400;
                text-align: left;
                cursor: grab;
                ${props.isSelected
                    ? `box-shadow: inset 4px 0 ${kBloomBlue};`
                    : ""}
                &:active {
                    cursor: grabbing;
                }
            `}
        >
            <span
                css={css`
                    color: ${props.isSelected ? kBloomBlue : "#202020"};
                `}
            >
                {props.index + 1}
            </span>
            {props.useAllowedWords ? (
                <span
                    css={css`
                        display: block;
                        min-width: 0;
                        padding-right: 22px;
                        line-height: 18px;
                        overflow-wrap: break-word;
                        color: #4a4a4a;
                        font-family: ${props.fontName};
                    `}
                >
                    {props.stage.allowedWordsFile}
                </span>
            ) : (
                <>
                    <span
                        css={css`
                            display: block;
                            min-width: 0;
                            box-sizing: border-box;
                            padding-right: 12px;
                            line-height: 18px;
                            overflow-wrap: break-word;
                            color: #202020;
                            font-family: ${props.fontName};
                        `}
                    >
                        {props.stage.letters}
                    </span>
                    <span
                        css={css`
                            display: block;
                            min-width: 0;
                            padding-right: 22px;
                            line-height: 18px;
                            overflow-wrap: break-word;
                            color: #4a4a4a;
                            font-family: ${props.fontName};
                        `}
                    >
                        {cleanSpaceDelimitedList(props.stage.sightWords)}
                    </span>
                </>
            )}
        </div>
    );
};

export const ReaderDialogPhaseSection: React.FunctionComponent<{
    settings: ReaderSettings;
    setSettings: (value: ReaderSettings) => void;
    selectedStageIndex: number;
    setSelectedStageIndex: (value: number) => void;
    fontName: string;
}> = (props) => {
    const nextStageId = React.useRef(0);
    const [stageIds, setStageIds] = React.useState(() =>
        props.settings.stages.map(
            () => `decodable-stage-${nextStageId.current++}`,
        ),
    );
    const stageSensors = useSensors(
        useSensor(PointerSensor, {
            activationConstraint: { distance: 8 },
        }),
    );

    const addNewStage = () => {
        const updatedSettings = cloneReaderSettings(props.settings);
        updatedSettings.stages.push(
            new ReaderStage((updatedSettings.stages.length + 1).toString()),
        );
        props.setSettings(updatedSettings);
        props.setSelectedStageIndex(updatedSettings.stages.length - 1);
        setStageIds([...stageIds, `decodable-stage-${nextStageId.current++}`]);
    };

    const removeSelectedStage = () => {
        const removedFile =
            props.settings.stages[props.selectedStageIndex]?.allowedWordsFile;
        const updatedSettings = cloneReaderSettings(props.settings);
        updatedSettings.stages.splice(props.selectedStageIndex, 1);
        props.setSelectedStageIndex(Math.max(0, props.selectedStageIndex - 1));
        setStageIds(
            stageIds.filter((_, index) => index !== props.selectedStageIndex),
        );
        props.setSettings(updatedSettings);
        // Delete the word-list file if no remaining stage references it.
        if (removedFile) {
            const stillUsed = updatedSettings.stages.some(
                (oneStage) => oneStage.allowedWordsFile === removedFile,
            );
            if (!stillUsed) {
                axios.delete(
                    `${getBloomApiPrefix()}readers/io/allowedWordsList`,
                    { params: { fileName: removedFile } },
                );
            }
        }
    };

    const reorderStages = (event: DragEndEvent) => {
        const { active, over } = event;
        if (!over || active.id === over.id) {
            return;
        }

        const oldIndex = stageIds.indexOf(active.id as string);
        const newIndex = stageIds.indexOf(over.id as string);
        if (oldIndex < 0 || newIndex < 0) {
            return;
        }

        const selectedStageId = stageIds[props.selectedStageIndex]!;
        const updatedSettings = cloneReaderSettings(props.settings);
        updatedSettings.stages = arrayMove(
            updatedSettings.stages,
            oldIndex,
            newIndex,
        );
        const reorderedStageIds = arrayMove(stageIds, oldIndex, newIndex);
        setStageIds(reorderedStageIds);
        props.setSelectedStageIndex(reorderedStageIds.indexOf(selectedStageId));
        props.setSettings(updatedSettings);
    };

    return (
        <div
            css={css`
                display: flex;
                flex-direction: column;
                min-width: 0;
                min-height: 0;
                border-right: 1px solid #e5e5e5;
            `}
        >
            <div
                css={css`
                    display: grid;
                    grid-template-columns: ${props.settings.useAllowedWords ===
                    1
                        ? "76px minmax(0, 1fr)"
                        : "76px 88px minmax(0, 1fr)"};
                    padding: 11px 14px;
                    background: #fafafa;
                    border-bottom: 1px solid #e5e5e5;
                    color: #8a929c;
                    font-size: 8pt;
                    font-weight: 700;
                    letter-spacing: 0.03em;
                    text-transform: uppercase;
                `}
            >
                <Span l10nKey="ReaderSetup.StageLabel">Stage</Span>
                {props.settings.useAllowedWords === 1 ? (
                    <Span l10nKey="ReaderSetup.AllowedWordsFileHeader">
                        Allowed Words File
                    </Span>
                ) : (
                    <>
                        <Span l10nKey="ReaderSetup.lettersHeader">Letters</Span>
                        <Span l10nKey="ReaderSetup.SightWordsHeader">
                            Sight Words
                        </Span>
                    </>
                )}
            </div>
            <div
                css={css`
                    flex: 1 1 auto;
                    min-height: 0;
                    overflow-y: auto;
                `}
            >
                <DndContext
                    sensors={stageSensors}
                    collisionDetection={closestCenter}
                    onDragEnd={reorderStages}
                >
                    <SortableContext
                        items={stageIds}
                        strategy={verticalListSortingStrategy}
                    >
                        {props.settings.stages.map((oneStage, index) => (
                            <DraggablePhaseRow
                                key={stageIds[index]}
                                id={stageIds[index]!}
                                index={index}
                                stage={oneStage}
                                isSelected={index === props.selectedStageIndex}
                                useAllowedWords={
                                    props.settings.useAllowedWords === 1
                                }
                                fontName={props.fontName}
                                onSelect={() =>
                                    props.setSelectedStageIndex(index)
                                }
                            />
                        ))}
                    </SortableContext>
                </DndContext>
                <div
                    css={css`
                        width: 100%;
                        border-bottom: 1px solid #eeeeee;
                    `}
                >
                    <Button
                        onClick={addNewStage}
                        startIcon={<AddIcon />}
                        css={css`
                            justify-content: flex-start;
                            min-height: 42px;
                            padding: 0 14px;
                            border-radius: 0;
                            color: ${kBloomBlue};
                            font-size: 11pt;
                            font-weight: 500;
                            letter-spacing: 0;
                            text-transform: none;
                            .MuiButton-startIcon {
                                margin-right: 6px;
                                svg {
                                    font-size: 18px;
                                }
                            }
                        `}
                    >
                        <Span l10nKey="ReaderSetup.AddStage">Add Stage</Span>
                    </Button>
                </div>
            </div>
            <div
                css={css`
                    display: flex;
                    align-items: center;
                    margin-top: auto;
                    flex: 0 0 40px;
                    box-sizing: border-box;
                    padding: 0 14px;
                    color: #858a8e;
                    font-size: 8pt;
                    border-top: 1px solid #eeeeee;
                `}
            >
                <Span l10nKey="ReaderSetup.ReorderStages">
                    Drag rows to reorder stages.
                </Span>
                <Button
                    onClick={removeSelectedStage}
                    disabled={props.settings.stages.length === 1}
                    startIcon={<DeleteOutlineIcon />}
                    css={css`
                        margin-left: auto;
                        min-width: 0;
                        min-height: 28px;
                        padding: 0;
                        color: #858a8e;
                        font-size: 8pt;
                        font-weight: 400;
                        letter-spacing: 0;
                        text-transform: none;
                        .MuiButton-startIcon {
                            margin-right: 5px;
                            svg {
                                font-size: 16px;
                            }
                        }
                    `}
                >
                    <Span
                        l10nKey="ReaderSetup.RemoveStage"
                        l10nParam0={(props.selectedStageIndex + 1).toString()}
                    >
                        Remove Stage {props.selectedStageIndex + 1}
                    </Span>
                </Button>
            </div>
        </div>
    );
};
