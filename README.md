# PixelPlanetBot
Bot for [pixelplanet.fun](https://pixelplanet.fun).

Partially based on [woyken/pixelplanet.fun-bot](https://github.com/Woyken/pixelplanet.fun-bot/).

You can download executable file [here](https://github.com/Topinambur223606/PixelPlanetTools/releases/latest).

### Some important stuff
- PixelPlanet recognizes user by IP, so launching multiple bots on same computer \/ from computers in same LAN wouldn't work.
- Admins allow bots only if they don't bother anyone and if it's not about super huge art in the center of active map segment.
- Dithering or something similar is not provided yet, color is picked as closest available; use [PixelPlanet converter](https://pixelplanet.fun/convert) or Photoshop "to web" export to get image for building.
- If you place image in the internet, [Imgur](https://imgur.com/upload) would be good choice to store your image.
- To build EXE with third party DLLs included by yourself, copy [ILRepack utility](https://www.nuget.org/packages/ILRepack/) to ```executable``` directory, launch "release" profile compilation and combined EXE will appear in that folder.
- After mass attack of russian griefers with proxies captcha was introduced at site, so you should deal with it for bot. When bot asks to pass captcha, you should just open site as usual, place pixel anywhere and then press any key in bot shell window.  
  
### Usage:
```batch
bot.exe X Y imageURL [notificationMode] [defendMode] [placementOrder] [proxyAddress] [logFileName]
```  
- **X, Y** - top left coordinates of image, both in range -32768..32767.
- **imageURL** - URL or path to image file that is built. Transparent parts are ignored. Don't forget to check that image fits into map.  
- **notificationMode** - defines bot behaviour when captcha appears: "B" - opens default browser in place of last attempt, doesn't work in proxy mode, "S" - produces beep sounds, "BS" - combined; if parameter doesn't contain this two letters, bot waits silently. Non-required, makes sound by default.
- **defendMode** - if enabled, bot wouldn't finish its work after first iteration and will provide the integrity of image. Pass "Y" to enable, "N" (or anything else) to disable. Non-required, disabled by default.
- **placementOrder** - indicates how bot will place pixels: L - from left, R - from right, T - from top, B - from bottom, O - outline first and then random, RND (or anything else) - random order. Non-required, random order by default.
- **proxyAddress** - proxy that's used by bot; now are supported only proxies without credentials.
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
