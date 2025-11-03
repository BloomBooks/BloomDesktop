import * as React from "react";
import { Link } from "./BookLinkTypes";
import { LinkCard } from "./LinkCard";
import {
    DndContext,
    closestCenter,
    KeyboardSensor,
    PointerSensor,
    useSensor,
    useSensors,
    DragEndEvent
} from "@dnd-kit/core";
import {
    arrayMove,
    SortableContext,
    sortableKeyboardCoordinates,
    useSortable,
    rectSortingStrategy
} from "@dnd-kit/sortable";
import { useEffect, useRef } from "react";
import { css } from "@emotion/react";

interface BookTargetListProps {
    links: Link[];
    onRemoveBook: (link: Link) => void;
    onReorderBooks?: (newLinks: Link[]) => void;
}

const SortableBookItem: React.FC<{
    link: Link;
    onRemove: (link: Link) => void;
}> = ({ link, onRemove }) => {
    const {
        attributes,
        listeners,
        setNodeRef,
        transform,
        transition
    } = useSortable({
        id: link.book.id
    });

    return (
        <div
            ref={setNodeRef}
            css={css`
                transform: ${transform
                    ? `translate3d(${transform.x}px, ${transform.y}px, 0)`
                    : "none"};
                transition: ${transition};
                position: relative;
                width: 140px;
                padding: 10px;
            `}
            {...attributes}
            {...listeners}
        >
            <LinkCard
                link={link}
                onRemove={() => {
                    console.log(
                        "SortableBookItem onRemove called for:",
                        link.book.folderName || link.book.title
                    );
                    onRemove(link);
                }}
                displayRealTitle={true}
            />
        </div>
    );
};

export const BookTargetList: React.FC<BookTargetListProps> = ({
    links,
    onRemoveBook,
    onReorderBooks
}) => {
    const linkRefs = useRef<Map<string, HTMLDivElement>>(new Map());
    const prevLinksLengthRef = useRef(links.length);

    useEffect(() => {
        // Only scroll if a new link was added (length increased)
        if (links.length > prevLinksLengthRef.current) {
            const lastLinkId = links[links.length - 1]?.book.id;
            if (lastLinkId) {
                const element = linkRefs.current.get(lastLinkId);
                element?.scrollIntoView({
                    behavior: "smooth",
                    block: "nearest"
                });
            }
        }
        prevLinksLengthRef.current = links.length;
    }, [links]);

    const sensors = useSensors(
        useSensor(PointerSensor, {
            activationConstraint: {
                distance: 8 // Use distance instead of delay+tolerance
            }
        }),
        useSensor(KeyboardSensor, {
            coordinateGetter: sortableKeyboardCoordinates
        })
    );

    const handleDragEnd = (event: DragEndEvent) => {
        const { active, over } = event;
        if (over && active.id !== over.id) {
            const oldIndex = links.findIndex(
                link => link.book.id === active.id
            );
            const newIndex = links.findIndex(link => link.book.id === over.id);
            const newLinks = arrayMove(links, oldIndex, newIndex);
            onReorderBooks?.(newLinks);
        }
    };

    return (
        <DndContext
            sensors={sensors}
            collisionDetection={closestCenter}
            onDragEnd={handleDragEnd}
        >
            <SortableContext
                items={links.map(link => link.book.id)}
                strategy={rectSortingStrategy}
            >
                <div
                    css={css`
                        display: flex;
                        flex-wrap: wrap;
                        gap: 8px;
                        height: 100%;
                        align-content: flex-start;
                        width: 100%; // Changed from min-width: 500px
                        overflow-x: hidden; // Add this to prevent horizontal scroll
                        background-color: lightgray;
                    `}
                >
                    {links.map(link => (
                        <div
                            key={link.book.id}
                            ref={el => {
                                if (el) {
                                    linkRefs.current.set(link.book.id, el);
                                } else {
                                    linkRefs.current.delete(link.book.id);
                                }
                            }}
                        >
                            <SortableBookItem
                                key={link.book.id}
                                link={link}
                                onRemove={link => {
                                    onRemoveBook(link);
                                }}
                            />
                        </div>
                    ))}
                </div>
            </SortableContext>
        </DndContext>
    );
};
