import streamDeck from "@elgato/streamdeck";
import type { JsonValue } from "@elgato/utils";
import { spawn } from "node:child_process";
import { existsSync, readdirSync, readFileSync } from "node:fs";
import path from "node:path";

/**
 * Talking to LaunchStage. The plugin never does the work itself: it runs LaunchStage.exe with the same command
 * lines a Stream Deck "Open" button would use (--activate, --close, --toggle, --resnap, --games), so PINs, gentle
 * closing and everything else work exactly like in the app.
 */

const dataFolder = path.join(process.env.APPDATA ?? "", "LaunchStage");

/** Reads a small JSON file that LaunchStage wrote (tolerates a byte-order mark). */
function readJson(file: string): unknown {
	return JSON.parse(readFileSync(file, "utf8").replace(/^﻿/, ""));
}

/** Where LaunchStage.exe is: LaunchStage writes it to app-path.txt every time it starts. */
export function findLaunchStage(): string | undefined {
	try {
		const exe = readFileSync(path.join(dataFolder, "app-path.txt"), "utf8").trim();
		if (exe.length > 0 && existsSync(exe)) {
			return exe;
		}
	} catch {
		// Not written yet: LaunchStage hasn't been opened on this PC.
	}

	return undefined;
}

/** Runs LaunchStage.exe with these arguments. Returns false when LaunchStage can't be found. */
export function runLaunchStage(args: string[]): boolean {
	const exe = findLaunchStage();
	if (exe === undefined) {
		streamDeck.logger.warn("LaunchStage.exe wasn't found. Open LaunchStage once so the plugin can find it.");
		return false;
	}

	streamDeck.logger.info(`Running LaunchStage ${args.join(" ")}`);
	const child = spawn(exe, args, { detached: true, stdio: "ignore", windowsHide: true });
	child.on("error", (error) => streamDeck.logger.error(`Couldn't start LaunchStage: ${error.message}`));
	child.unref();
	return true;
}

/** The names of the saved profiles, A to Z. */
export function listProfiles(): string[] {
	const folder = path.join(dataFolder, "Profiles");
	try {
		return readdirSync(folder)
			.filter((file) => file.toLowerCase().endsWith(".json"))
			.map((file) => {
				try {
					const profile = readJson(path.join(folder, file)) as { name?: unknown };
					if (typeof profile.name === "string" && profile.name.trim().length > 0) {
						return profile.name.trim();
					}
				} catch {
					// A damaged file still shows up by its file name.
				}

				return file.slice(0, -".json".length);
			})
			.sort((a, b) => a.localeCompare(b, undefined, { sensitivity: "base" }));
	} catch {
		return [];
	}
}

/** The profiles that are open right now (lower case), from the list LaunchStage keeps while it runs. */
export function openProfiles(): Set<string> {
	try {
		const names = readJson(path.join(dataFolder, "open-profiles.json"));
		if (Array.isArray(names)) {
			return new Set(names.filter((n): n is string => typeof n === "string").map((n) => n.toLowerCase()));
		}
	} catch {
		// LaunchStage isn't running (it removes the list when it exits), so nothing counts as open.
	}

	return new Set();
}

/**
 * Answers the settings panel's request for the profile drop-down (sdpi-components "datasource").
 * Returns true when the message was that request.
 */
export async function answerProfileList(payload: JsonValue): Promise<boolean> {
	if (payload === null || typeof payload !== "object" || Array.isArray(payload) || payload.event !== "getProfiles") {
		return false;
	}

	const profiles = listProfiles();
	await streamDeck.ui.sendToPropertyInspector({
		event: "getProfiles",
		items: profiles.length > 0
			? profiles.map((name) => ({ label: name, value: name }))
			: [{ label: "No profiles yet: make one in LaunchStage", value: "", disabled: true }]
	});
	return true;
}
