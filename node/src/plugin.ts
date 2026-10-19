import streamDeck from "@elgato/streamdeck";

import { BreakoutAction } from "./actions/breakout";
import { FoosballAction } from "./actions/foosball";
import { RacerAction } from "./actions/racer";
import { RacerMapAction } from "./actions/racer-map";
import { Racer4PAction } from "./actions/racer4p";
import { SnakeAction } from "./actions/snake";

streamDeck.logger.setLevel("info");

streamDeck.actions.registerAction(new BreakoutAction());
streamDeck.actions.registerAction(new SnakeAction());
streamDeck.actions.registerAction(new FoosballAction());
streamDeck.actions.registerAction(new RacerAction());
streamDeck.actions.registerAction(new Racer4PAction());
streamDeck.actions.registerAction(new RacerMapAction());

streamDeck.connect();
