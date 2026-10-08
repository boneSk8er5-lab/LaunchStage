import streamDeck from "@elgato/streamdeck";

import { GamesAction } from "./actions/games";
import { ProfileAction } from "./actions/profile";
import { ResnapAction } from "./actions/resnap";

// Log enough to troubleshoot (in the plugin's logs folder), without recording every message.
streamDeck.logger.setLevel("info");

streamDeck.actions.registerAction(new ProfileAction());
streamDeck.actions.registerAction(new ResnapAction());
streamDeck.actions.registerAction(new GamesAction());

streamDeck.connect();
