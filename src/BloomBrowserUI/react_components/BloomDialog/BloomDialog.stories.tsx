import * as React from "react";
import {
    BloomDialog,
    DialogBottomButtons,
    DialogMiddle,
    DialogTitle
} from "./BloomDialog";
import { DialogCancelButton } from "./commonDialogComponents";

export default {
    title: "BloomDialog"
};

export const TestDragResize = () => (
    <BloomDialog onClose={() => undefined} open={true}>
        <DialogTitle title="Drag Me" />
        <DialogMiddle>
            <p>Blah</p>
            <p>Blah</p>
            <p>
                Lorem ipsum dolor sit amet, consectetur adipiscing elit.
                Curabitur in felis feugiat est pellentesque bibendum. Maecenas
                non sem a augue vulputate ultricies. In hac habitasse platea
                dictumst. Quisque augue quam, facilisis in laoreet ac,
                consectetur luctus lectus. Cras eu condimentum sem.
            </p>
            <p>Blah</p>
        </DialogMiddle>
        <DialogBottomButtons>
            <DialogCancelButton onClick_DEPRECATED={() => undefined} />
        </DialogBottomButtons>
    </BloomDialog>
);

TestDragResize.story = {
    name: "Test drag & resize"
};
