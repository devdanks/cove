import { useCallback, useState, type KeyboardEvent, type ReactNode } from "react";

export interface DragHandleProps {
  tabIndex: number;
  role: "button";
  "aria-label": string;
  "aria-pressed": boolean;
  onKeyDown: (event: KeyboardEvent<HTMLElement>) => void;
}

interface SortableListRenderState {
  index: number;
  isDragging: boolean;
  isOver: boolean;
  keyboardDragging: boolean;
  dragHandleProps: DragHandleProps;
}

interface SortableListProps<T> {
  items: T[];
  getKey: (item: T) => string | number;
  onReorder: (nextItems: T[]) => void;
  renderItem: (item: T, state: SortableListRenderState) => ReactNode;
  disabled?: boolean;
  className?: string;
}

function moveItem<T>(items: T[], fromIndex: number, toIndex: number) {
  if (fromIndex === toIndex || fromIndex < 0 || toIndex < 0 || fromIndex >= items.length || toIndex >= items.length) {
    return items;
  }

  const nextItems = [...items];
  const [movedItem] = nextItems.splice(fromIndex, 1);
  nextItems.splice(toIndex, 0, movedItem);
  return nextItems;
}

export function SortableList<T>({
  items,
  getKey,
  onReorder,
  renderItem,
  disabled = false,
  className = "space-y-2",
}: SortableListProps<T>) {
  const [dragKey, setDragKey] = useState<string | number | null>(null);
  const [overKey, setOverKey] = useState<string | number | null>(null);
  const [keyboardDragKey, setKeyboardDragKey] = useState<string | number | null>(null);

  const reorderByIndex = useCallback((fromIndex: number, toIndex: number) => {
    const nextItems = moveItem(items, fromIndex, toIndex);
    if (nextItems !== items) {
      onReorder(nextItems);
    }
  }, [items, onReorder]);

  const commitDrag = useCallback(() => {
    if (disabled || dragKey == null || overKey == null || dragKey === overKey) {
      return;
    }

    const fromIndex = items.findIndex((item) => getKey(item) === dragKey);
    const toIndex = items.findIndex((item) => getKey(item) === overKey);
    reorderByIndex(fromIndex, toIndex);
  }, [disabled, dragKey, getKey, items, overKey, reorderByIndex]);

  const resetDragState = useCallback(() => {
    setDragKey(null);
    setOverKey(null);
  }, []);

  return (
    <div className={className} role="listbox" aria-disabled={disabled}>
      {items.map((item, index) => {
        const itemKey = getKey(item);
        const isDragging = dragKey === itemKey;
        const isOver = overKey === itemKey;
        const keyboardDragging = keyboardDragKey === itemKey;
        const dragHandleProps: DragHandleProps = {
          tabIndex: disabled ? -1 : 0,
          role: "button",
          "aria-label": keyboardDragging ? "Drop item" : "Pick up item to reorder",
          "aria-pressed": keyboardDragging,
          onKeyDown: (event) => {
            if (disabled) {
              return;
            }

            if (event.key === "Escape") {
              if (keyboardDragging) {
                event.preventDefault();
                setKeyboardDragKey(null);
              }
              return;
            }

            if (event.key === " " || event.key === "Enter") {
              event.preventDefault();
              setKeyboardDragKey((current) => current === itemKey ? null : itemKey);
              return;
            }

            if (event.key === "ArrowUp" && (event.altKey || keyboardDragging)) {
              event.preventDefault();
              reorderByIndex(index, index - 1);
              return;
            }

            if (event.key === "ArrowDown" && (event.altKey || keyboardDragging)) {
              event.preventDefault();
              reorderByIndex(index, index + 1);
            }
          },
        };

        return (
          <div
            key={String(itemKey)}
            draggable={!disabled}
            onDragStart={(event) => {
              if (disabled) {
                return;
              }

              event.dataTransfer.effectAllowed = "move";
              setDragKey(itemKey);
              setOverKey(itemKey);
            }}
            onDragOver={(event) => {
              if (disabled) {
                return;
              }

              event.preventDefault();
              if (overKey !== itemKey) {
                setOverKey(itemKey);
              }
            }}
            onDrop={(event) => {
              event.preventDefault();
              commitDrag();
              resetDragState();
            }}
            onDragEnd={resetDragState}
            aria-grabbed={isDragging || keyboardDragging}
          >
            {renderItem(item, { index, isDragging, isOver, keyboardDragging, dragHandleProps })}
          </div>
        );
      })}
    </div>
  );
}