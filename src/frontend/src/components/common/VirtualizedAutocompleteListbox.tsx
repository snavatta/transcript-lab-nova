import {
  Children,
  cloneElement,
  forwardRef,
  type CSSProperties,
  type ForwardedRef,
  type HTMLAttributes,
  type ReactElement,
} from 'react';
import { List, type RowComponentProps } from 'react-window';

const LISTBOX_PADDING_PX = 8;
const ROW_HEIGHT_PX = 56;
const MAX_VISIBLE_ROWS = 8;
const OVERSCAN_ROWS = 5;

type VirtualizedRowProps = {
  items: ReactElement<{ style?: CSSProperties }>[];
};

function VirtualizedRow({ index, style, items }: RowComponentProps<VirtualizedRowProps>) {
  const item = items[index];

  return cloneElement(item, {
    style: {
      ...item.props.style,
      ...style,
      top: typeof style.top === 'number' ? style.top + LISTBOX_PADDING_PX : style.top,
    },
  });
}

const VirtualizedAutocompleteListbox = forwardRef<HTMLDivElement, HTMLAttributes<HTMLDivElement>>(
  function VirtualizedAutocompleteListbox(props, ref: ForwardedRef<HTMLDivElement>) {
    const { children, style, ...other } = props;
    const items = Children.toArray(children) as ReactElement<{ style?: CSSProperties }>[];
    const listHeight = Math.max(
      ROW_HEIGHT_PX + LISTBOX_PADDING_PX * 2,
      Math.min(items.length, MAX_VISIBLE_ROWS) * ROW_HEIGHT_PX + LISTBOX_PADDING_PX * 2,
    );

    return (
      <div ref={ref} {...other}>
        <List
          tagName="ul"
          rowComponent={VirtualizedRow}
          rowCount={items.length}
          rowHeight={ROW_HEIGHT_PX}
          rowProps={{ items }}
          overscanCount={OVERSCAN_ROWS}
          style={{
            ...style,
            height: listHeight,
            margin: 0,
            padding: `${LISTBOX_PADDING_PX}px 0`,
          }}
        />
      </div>
    );
  },
);

export default VirtualizedAutocompleteListbox;
