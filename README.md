# BP-translate

Partial translator for the Japanese Blue Protocol server. It only translates strings that are downloaded from the api (quests, items, mobs...)  

So far it does not work if you use a proxy for Blue Protocol. VPN is fine.
 
<img src="https://i.imgur.com/PwC50La.png" alt= “” width="328" height="341">

(In this screenshot, SkyProc was also used to translate the interface)

[If you have any problems or want to help with translations, check out this discord](https://discord.gg/nVfDBy97aK) 

## About this repository

Thanks to the original creator (https://github.com/ArtFect/BP-translate) to make this great app.

This repository purpose is used to host the modified code, and provide remote setting for auto-update translation feature.

## Feature ahead original repository

1. Fixed "Translation sent" but in the game didn't translated.
2. Auto-Update to the Latest Translation each time `BP-translate.exe` opened.

![image](https://github.com/DOTzX/BP-translate/assets/16914200/090bd844-2b23-4cbc-a677-09db281ca299)


# Frequently Asked Question

## How does it work?

The program doesn't change game files, doesn't interact with the game memory, doesn't do dll injection

It redirects domain from which the data is downloaded to localhost via the hosts file.  
Then it creates a server at localhost that proxies all requests to the real server, but replaces localisation file with its translated one.

## How to use it?

1. Download latest release from https://github.com/DOTzX/BP-translate/releases
2. Unpack
3. Open `BP-translate.exe` (It is necessary to open each time before you open the game)
4. Launch the game as usual from the launcher.
5. Wait till `Translation sent, the program will close in 15 seconds` shown up, and the window will automatically close after the game launched.
6. The game will have translation patch.

## How to change translation language

1. Make sure `BP-translate.exe` is closed.
2. Open file using notepad: `bptl_setting.json`.
3. Change value of `selected_language` to available language.
4. Save.
5. Reopen the `BP-translate.exe`

Current available language is:
- English = `en`
- Indonesia = `id`
- Espanol = `es`
- Portugues = `pt`
- Deutsch = `de`
- Russian = `ru`
- Russian without English translation = `ru-ru`
- Francais = `fr` (need more localization)
- Italian = `it` (need more localization)

## How to turn on/off auto-update

1. Open file using notepad: `bptl_setting.json`.
2. Change value of `auto_update` to `true` or `false`
3. Save.

## How to turn online/offline mode of the application

1. Open file using notepad: `bptl_setting.json`.
2. Change value of `online_mode` to `true` or `false`
3. Save.

# Known issue

If you kill a program via Task Manager or otherwise, you may have an endless load when starting the game.

To fix this, run the program again and close normally via X.  Or you can go to `C:/Windows/System32/drivers/etc/hosts` file and remove this line `127.0.0.1 masterdata-main.aws.blue-protocol.com`

# Known project

Original Creator of this Repository = https://github.com/ArtFect/BP-translate

Full Translation Repository = https://github.com/digitalstars/BlueProtocol-Translate
