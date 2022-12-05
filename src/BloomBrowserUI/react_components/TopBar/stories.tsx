/** @jsx jsx **/
import { jsx, css } from "@emotion/core";
import * as React from "react";
import { storiesOf } from "@storybook/react";
import { TopBar } from "./TopBar";

storiesOf("TopBar", module).add("TopBar", () => <TopBar />);
