import {
	action,
	type DidReceiveSettingsEvent,
	type KeyDownEvent,
	type SendToPluginEvent,
	SingletonAction,
	type WillAppearEvent,
	type WillDisappearEvent
} from "@elgato/streamdeck";
import type { JsonValue } from "@elgato/utils";

import { answerProfileList, openProfiles, runLaunchStage } from "../launchstage";

/** What the button does: open or close (toggle), only open, or only close. */
type ProfileSettings = {
	profile?: string;
	mode?: "toggle" | "open" | "close";
};

const modeArgument = { toggle: "--toggle", open: "--activate", close: "--close" } as const;

/**
 * Opens and/or closes a LaunchStage profile. The button shows its "open" picture (state 1) while the profile is
 * open, checked every 2 seconds from the list LaunchStage keeps.
 */
@action({ UUID: "com.bones84.launchstage.profile" })
export class ProfileAction extends SingletonAction<ProfileSettings> {
	private readonly settings = new Map<string, ProfileSettings>();
	private readonly shownState = new Map<string, 0 | 1>();
	private timer: ReturnType<typeof setInterval> | undefined;

	override async onWillAppear(ev: WillAppearEvent<ProfileSettings>): Promise<void> {
		this.settings.set(ev.action.id, ev.payload.settings);
		this.shownState.delete(ev.action.id);
		this.timer ??= setInterval(() => void this.showOpenStates(), 2000);
		if (ev.action.isKey()) {
			await ev.action.setTitle(ev.payload.settings.profile ?? "");
		}

		await this.showOpenStates();
	}

	override onWillDisappear(ev: WillDisappearEvent<ProfileSettings>): void {
		this.settings.delete(ev.action.id);
		this.shownState.delete(ev.action.id);
		if (this.settings.size === 0 && this.timer !== undefined) {
			clearInterval(this.timer);
			this.timer = undefined;
		}
	}

	override async onDidReceiveSettings(ev: DidReceiveSettingsEvent<ProfileSettings>): Promise<void> {
		this.settings.set(ev.action.id, ev.payload.settings);
		this.shownState.delete(ev.action.id);
		if (ev.action.isKey()) {
			await ev.action.setTitle(ev.payload.settings.profile ?? "");
		}

		await this.showOpenStates();
	}

	override async onKeyDown(ev: KeyDownEvent<ProfileSettings>): Promise<void> {
		const { profile, mode = "toggle" } = ev.payload.settings;
		if (!profile) {
			await ev.action.showAlert(); // no profile picked yet
			return;
		}

		if (!runLaunchStage([modeArgument[mode] ?? "--toggle", profile])) {
			await ev.action.showAlert();
		}
	}

	override async onSendToPlugin(ev: SendToPluginEvent<JsonValue, ProfileSettings>): Promise<void> {
		await answerProfileList(ev.payload);
	}

	/** Lights up the buttons of open profiles (only sending changes). */
	private async showOpenStates(): Promise<void> {
		const open = openProfiles();
		for (const button of this.actions) {
			if (!button.isKey()) {
				continue;
			}

			const profile = this.settings.get(button.id)?.profile;
			const state: 0 | 1 = profile !== undefined && open.has(profile.toLowerCase()) ? 1 : 0;
			if (this.shownState.get(button.id) !== state) {
				this.shownState.set(button.id, state);
				await button.setState(state);
			}
		}
	}
}
