# PixelPlanetBot
Bot for [pixelplanet.fun](https://pixelplanet.fun).

Partially based on [woyken/pixelplanet.fun-bot](https://github.com/Woyken/pixelplanet.fun-bot/)

### Some important stuff
- PixelPlanet recognizes user by IP, so launching multiple bots on same computer / from computers in same LAN wouldn't work.
- IDK what PixelPlanet admins think about bots, but Woyken mentions that abusers and griefers might be banned.
- Dithering or something similar is not provided yet, color is picked as closest available; use [PixelPlanet converter](https://pixelplanet.fun/convert) to get image for building.
- You can launch bot as background service on dedicated server using [NSSM](http://nssm.cc/) (very useful thing).
- [Imgur](https://imgur.com/upload) would be good choice to store your image.
- You can use ILMerge to get single executable file with third party DLLs included; just launch script ```PixelPlanetBot/executable/MergeExecutable.ps1``` after compilation in "release" profile and combined EXE will appear in the same folder.

### Usage:
```batch
bot.exe X Y imageURL [defendMode]
```
- X, Y - top left coordinates of image, both in range -32768..32767.
- imageURL - URL of image to build; transparent parts are ignored. Don't forget to check if image fits into map.  
- defendMode - if enabled, bot doesn't finish its work after first iteration and provides the integrity of image. Pass "Y" as fourth parameter to enable.
