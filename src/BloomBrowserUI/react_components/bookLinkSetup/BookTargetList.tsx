import * as React from "react";
import { Link } from "./BookLinkTypes";
import { BookLinkCard } from "./BookLinkCard";
import {
    DndContext,
    closestCenter,
    KeyboardSensor,
    PointerSensor,
    useSensor,
    useSensors,
    DragEndEvent,
    DragOverlay,
    DragStartEvent,
} from "@dnd-kit/core";
import {
    arrayMove,
    SortableContext,
    sortableKeyboardCoordinates,
    useSortable,
    rectSortingStrategy,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { useEffect, useRef } from "react";
import { bookGridContainerStyles } from "./sharedStyles";

interface BookTargetListProps {
    links: Link[];
    onRemoveBook: (link: Link) => void;
    onReorderBooks?: (newLinks: Link[]) => void;
}

const SortableBookItem: React.FC<{
    link: Link;
    onRemove: () => void;
}> = ({ link, onRemove }) => {
    const {
        attributes,
        listeners,
        setNodeRef,
        transform,
        transition,
        isDragging,
    } = useSortable({
        id: link.book.id,
    });

    const style = {
        transform: CSS.Transform.toString(transform),
        transition,
        opacity: isDragging ? 0.5 : 1,
    };

    return (
        <div
            ref={setNodeRef}
            style={style}
            data-testid={`target-book-${link.book.id}`}
            {...attributes}
            {...listeners}
        >
            <BookLinkCard
                link={link}
                onRemove={onRemove}
                preferFolderName={false}
                isDragging={isDragging}
            />
        </div>
    );
};

export const BookTargetList: React.FC<BookTargetListProps> = ({
    links,
    onRemoveBook,
    onReorderBooks,
}) => {
    const linkRefs = useRef<Map<string, HTMLDivElement>>(new Map());
    const prevLinksLengthRef = useRef(links.length);
    const [activeId, setActiveId] = React.useState<string | null>(null);

    // Auto-scroll to newly added books so users can see confirmation of their action.
    // We track the previous length to distinguish additions from removals or reordering.
    useEffect(() => {
        // Only scroll if a new link was added (length increased)
        if (links.length > prevLinksLengthRef.current) {
            const lastLinkId = links[links.length - 1]?.book.id;
            if (lastLinkId) {
                const element = linkRefs.current.get(lastLinkId);
                element?.scrollIntoView({
                    behavior: "smooth",
                    block: "nearest",
                });
            }
        }
        prevLinksLengthRef.current = links.length;
    }, [links]);

    // Configure drag sensors for both mouse and keyboard interaction.
    // The distance threshold prevents accidental drags when clicking the remove button.
    const sensors = useSensors(
        useSensor(PointerSensor, {
            activationConstraint: {
                distance: 8, // Require 8px of movement before drag starts
            },
        }),
        useSensor(KeyboardSensor, {
            coordinateGetter: sortableKeyboardCoordinates,
        }),
    );

    const handleDragStart = (event: DragStartEvent) => {
        setActiveId(event.active.id as string);
    };

    const handleDragEnd = (event: DragEndEvent) => {
        const { active, over } = event;
        // Reorder the links array based on where the item was dropped.
        // The dnd-kit library provides the active (dragged) and over (drop target) elements.
        if (over && active.id !== over.id) {
            const oldIndex = links.findIndex(
                (link) => link.book.id === active.id,
            );
            const newIndex = links.findIndex(
                (link) => link.book.id === over.id,
            );
            if (oldIndex >= 0 && newIndex >= 0) {
                const newLinks = arrayMove(links, oldIndex, newIndex);
                onReorderBooks?.(newLinks);
            }
        }
        setActiveId(null);
    };

    // Track which link is being dragged so we can render it in the DragOverlay.
    // This provides visual feedback during the drag operation.
    const activeLink = activeId
        ? links.find((link) => link.book.id === activeId)
        : null;

    return (
        <DndContext
            sensors={sensors}
            collisionDetection={closestCenter}
            onDragStart={handleDragStart}
            onDragEnd={handleDragEnd}
        >
            <SortableContext
                items={links.map((link) => link.book.id)}
                strategy={rectSortingStrategy} // Use rect strategy for grid layouts
            >
                <div css={bookGridContainerStyles}>
                    {links.map((link) => (
                        <div
                            key={link.book.id}
                            ref={(el) => {
                                if (el) {
                                    linkRefs.current.set(link.book.id, el);
                                } else {
                                    linkRefs.current.delete(link.book.id);
                                }
                            }}
                        >
                            <SortableBookItem
                                link={link}
                                onRemove={() => onRemoveBook(link)}
                            />
                        </div>
                    ))}
                </div>
            </SortableContext>
            <DragOverlay>
                {activeLink ? (
                    <BookLinkCard
                        link={activeLink}
                        preferFolderName={false}
                        isDragging={true}
                    />
                ) : null}
            </DragOverlay>
        </DndContext>
    );
};
