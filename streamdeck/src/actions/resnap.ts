import {
	action,
	type DidReceiveSettingsEvent,
	type KeyDownEvent,
	type SendToPluginEvent,
	SingletonAction,
	type WillAppearEvent
} from "@elgato/streamdeck";
import type { JsonValue } from "@elgato/utils";

import { answerProfileList, runLaunchStage } from "../launchstage";

type ResnapSettings = {
	profile?: string;
};

/** Puts a profile's open windows back in their spots, without opening or closing anything. */
@action({ UUID: "com.bones84.launchstage.resnap" })
export class ResnapAction extends SingletonAction<ResnapSettings> {
	override async onWillAppear(ev: WillAppearEvent<ResnapSettings>): Promise<void> {
		if (ev.action.isKey()) {
			await ev.action.setTitle(ev.payload.settings.profile ?? "");
		}
	}

	override async onDidReceiveSettings(ev: DidReceiveSettingsEvent<ResnapSettings>): Promise<void> {
		if (ev.action.isKey()) {
			await ev.action.setTitle(ev.payload.settings.profile ?? "");
		}
	}

	override async onKeyDown(ev: KeyDownEvent<ResnapSettings>): Promise<void> {
		const { profile } = ev.payload.settings;
		if (!profile || !runLaunchStage(["--resnap", profile])) {
			await ev.action.showAlert();
		}
	}

	override async onSendToPlugin(ev: SendToPluginEvent<JsonValue, ResnapSettings>): Promise<void> {
		await answerProfileList(ev.payload);
	}
}
