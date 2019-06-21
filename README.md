# PixelPlanetBot
Bot for [pixelplanet.fun](https://pixelplanet.fun).

Partially based on [woyken/pixelplanet.fun-bot](https://github.com/Woyken/pixelplanet.fun-bot/)

### Some important stuff
- PixelPlanet recognizes user by IP, so launching multiple bots on same computer \/ from computers in same LAN wouldn't work.
- IDK what PixelPlanet admins think about bots, but Woyken mentions that abusers and griefers might be banned.
- Dithering or something similar is not provided yet, color is picked as closest available; use [PixelPlanet converter](https://pixelplanet.fun/convert) to get image for building.
- If you place image in the internet, [Imgur](https://imgur.com/upload) would be good choice to store your image.
- There is [executable file](https://raw.githubusercontent.com/Topinambur223606/PixelPlanetTools/master/executable/PixelPlanetBot.exe) available with third party DLLs included. You can make it by yourself - copy [ILRepack utility](https://www.nuget.org/packages/ILRepack/) to ```executable``` directory, launch "release" profile compilation and combined EXE will appear in that folder.
- After mass attack of russian griefers with proxies captcha was introduced at site, so you should deal with it for bot. First, give your default browser fingerprint to the bot. Then, when bot will ask to pass captcha again, you should just open site, place pixel anywhere and then press any key in bot shell window.  
To get fingerprint, open [fingerprint.html](https://raw.githubusercontent.com/Topinambur223606/PixelPlanetTools/master/fingerprint.html) and it will appear; adblock plugins may block fingerprint script - if they did, disable them all and refresh with Shift+F5. This is simple, but sometimes that fingerprint differs from real one.  
Another way to get fingerprint - open dev tools before placing pixel, switch to "Network" tab (in Chrome and Opera) and place pixel. Request to ```api/pixel``` will appear, its body contains field named ```fingerprint``` with value that you should copy and pass to bot before usage. 

### Linking with browser:
```batch
bot.exe fingerprint
```  
**fingerprint** - 128-bit (32 hex symbols) value that represents your browser specs hash; allows to pass captcha task to user.

### Regular usage:
```batch
bot.exe X Y imageURL [notificationMode] [defendMode] [placementOrder] [logFileName]
```  
- **X, Y** - top left coordinates of image, both in range -32768..32767.
- **imageURL** - URL or path to image file that is built. Transparent parts are ignored. Don't forget to check that image fits into map.  
- **notificationMode** - defines bot behaviour when captcha appears: "B" - opens default browser in place of last attempt, "S" - produces beep sounds, "BS" - combined; if parameter doesn't contain this two letters, bot waits silently. Non-required, opens browser by default.
- **defendMode** - if enabled, bot wouldn't finish its work after first iteration and will provide the integrity of image. Pass "Y" to enable, "N" (or anything else) to disable. Non-required, disabled by default.
- **placementOrder** - indicates how bot will place pixels: L - from left, R - from right, T - from top, B - from bottom, RND (or anything else) - random order. Non-required, random order by default.
- **logFileName** - if specified, enables writing logs to file at given path.  

# PixelPlanetWatcher
Program that logs updates in given rectangle to the binary file.  

File format:
- Coordinates: X1, Y1, X2, Y2 - signed 16-bit integers;
- Start time: DateTime 64-bit representation;
- Start field state: pixel colors, width \* height unsigned bytes (by Y, then by X - in a rows);
- Updates (saved every minute):
  - Save time: DateTime 64-bit representation;
  - Count of updates: unsigned 32-bit integer;
  - Pixel updates:
    - Coordinates: X, Y - signed 16-bit integers;
    - Pixel color: unsigned byte.

File is available to use after program is closed.
Utility for generating video from binary files is planned to be implemented.

### Usage:
```batch
watcher.exe X1 Y1 X2 Y2 [logFileName]
```  
- **X1, Y1, X2, Y2** - top left and bottom right coordinates of rectangle to track, all in range -32768..32767.
- **logFileName** - if specified, enables writing logs to file at given path.  
