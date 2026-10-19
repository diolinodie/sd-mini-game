import streamDeck from "@elgato/streamdeck";

import { BreakoutAction } from "./actions/breakout";
import { SnakeAction } from "./actions/snake";

streamDeck.logger.setLevel("info");

streamDeck.actions.registerAction(new BreakoutAction());
streamDeck.actions.registerAction(new SnakeAction());

streamDeck.connect();
