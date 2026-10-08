# Stream Deck and other button pads

Any button pad that can open a program can control LaunchStage: Stream Deck, Soomfon, macro pads, even a desktop shortcut.

## Get the command for a button

1. Right-click the profile's card and click **Stream Deck commands...**.
2. Click **Copy** next to the command you want (see the list below).
3. In your button pad's software, add an action that opens a program or website, and paste the command.

The commands:

- **Open this profile**
- **Open or close this profile (one button for both)**
- **Close this profile**
- **Put its windows back in place**
- **Open the game picker**

![The Stream Deck commands window](commands.png)

> If your pad's software asks for the program and its arguments separately, copy the **LaunchStage program** line as the program, and use the part after it (like `--toggle "Stream"`) as the arguments.

LaunchStage doesn't even need to be open: the button starts it if needed. A private profile still asks for its PIN.

## The Stream Deck plugin

If you have an Elgato Stream Deck (Stream Deck app 7.1 or newer), there's also a LaunchStage plugin with three buttons:

- **Profile:** pick a profile and what pressing does. The button lights up while the profile is open.
- **Put windows back:** pick a profile.
- **Play a game:** opens the game picker.

To install it, double-click the file `com.bones84.launchstage.streamDeckPlugin`, then drag the buttons from the **LaunchStage** group onto your Stream Deck. Open LaunchStage once first, and keep it running (in the tray is fine).
