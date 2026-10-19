import streamDeck from "@elgato/streamdeck";

import { SnakeAction } from "./actions/snake";

streamDeck.logger.setLevel("info");

streamDeck.actions.registerAction(new SnakeAction());

streamDeck.connect();
