# PixelPlanetBot
Bot for [pixelplanet.fun](https://pixelplanet.fun).

Partially based on [woyken/pixelplanet.fun-bot](https://github.com/Woyken/pixelplanet.fun-bot/)

### Some important stuff
- PixelPlanet recognizes user by IP, so launching multiple bots on same computer / from computers in same LAN wouldn't work.
- IDK what PixelPlanet admins think about bots, but Woyken mentions that abusers and griefers might be banned.
- Dithering or something similar is not provided yet, color is picked as closest available; use [PixelPlanet converter](https://pixelplanet.fun/convert) to get image for building.
- You can launch bot as background service on dedicated server using [NSSM](http://nssm.cc/) (very useful thing).
- [Imgur](https://imgur.com/upload) would be good choice to store your image.
- There is [executable file](https://raw.githubusercontent.com/Topinambur223606/PixelPlanetBot/master/PixelPlanetBot/executable/PixelPlanetBot.exe) available with third party DLLs included. You can make it by yourself - copy ```ILMerge.exe``` to ```PixelPlanetBot/executable/```, launch "release" profile compilation and combined EXE will appear in that folder.
- After mass attack of russian griefers with proxies captcha was introduced at site, so you should deal with it for bot. First, give your default browser fingerprint to the bot. Then, when site will ask to pass captcha again, default browser will be opened, you should just place pixel anywhere and then press Enter in bot shell window.  
To get fingerprint, open dev tools before placing pixel, switch to "Network" tab (in Chrome and Opera) and place pixel. Request to ```api/pixel``` will appear, its body contains field named ```fingerprint``` with value that you should copy and pass to bot before usage.

### Linking with browser:
```batch
bot.exe fingerprint
```  
**fingerprint** - 128-bit (32 hex symbols) value that represents your browser specs hash; allows to pass captcha task to user.


###Regular usage:
```batch
bot.exe X Y imageURL [defendMode] [placementOrder] [logFileName]
```  

- **X, Y** - top left coordinates of image, both in range -32768..32767.
- **imageURL** - URL of image to build; transparent parts are ignored. Don't forget to check that image fits into map.  
- **defendMode** - if enabled, bot wouldn't finish its work after first iteration and will provide the integrity of image. Pass "Y" to enable, "N" (or anything else) to disable. Non-required, disabled by default.
- **placementOrder** - indicates how bot will place pixels: L - from left, R - from right, T - from top, B - from bottom, RND (or anything else) - random order. Non-required, random order by default.
- **logFileName** - if specified, enables writing logs to file at given path.
