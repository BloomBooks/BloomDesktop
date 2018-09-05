import { library as fontAwesomeInitializer } from "@fortawesome/fontawesome-svg-core";
export { FontAwesomeIcon } from "@fortawesome/react-fontawesome";

// Each font-awesome icon we want to use needs to be imported once like this.
// For example, if you import faCheck, and then add it, in React you can use
// <FontAwesomeIcon icon="check" />
// Note that the "fa" prefix is required here but must NOT be used in the
// React component.
import {
    faQuestion,
    faCheck,
    faExclamationCircle,
    faCopy,
    faPaste
} from "@fortawesome/free-solid-svg-icons";

fontAwesomeInitializer.add(faCheck);
fontAwesomeInitializer.add(faQuestion);
fontAwesomeInitializer.add(faExclamationCircle);
fontAwesomeInitializer.add(faCopy);
fontAwesomeInitializer.add(faPaste);
