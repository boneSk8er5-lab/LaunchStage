import { action, type KeyDownEvent, SingletonAction } from "@elgato/streamdeck";

import { runLaunchStage } from "../launchstage";

/** Opens LaunchStage's game picker. */
@action({ UUID: "com.bones84.launchstage.games" })
export class GamesAction extends SingletonAction {
	override async onKeyDown(ev: KeyDownEvent): Promise<void> {
		if (!runLaunchStage(["--games"])) {
			await ev.action.showAlert();
		}
	}
}
