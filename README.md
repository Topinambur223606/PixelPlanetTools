# PixelPlanetBot
Bot for [pixelplanet.fun](https://pixelplanet.fun).

Original idea was based on [woyken/pixelplanet.fun-bot](https://github.com/Woyken/pixelplanet.fun-bot/).

Now only main (Earth) canvas is supported in all applications, moon canvas will be supported in Watcher/Visualizer, 3D canvas bot support theoretically can be implemented in future.

You can download executable files [here](https://github.com/Topinambur223606/PixelPlanetTools/releases/latest).

### Some important stuff
- PixelPlanet recognizes user by IP, so launching multiple bots on same computer \/ from computers in same LAN would not work.
- Admins does not allow building botted arts in active map segment, especially photos and large images. It's not strictly prohibited, but image can be removed any time. Only safe areas for botting are ocean and south.
- Dithering or something similar is not provided in bot, color is picked as closest available; use [PixelPlanet converter](https://pixelplanet.fun/convert) or Photoshop "to web" export to get image for building.
- After mass attack of russian griefers with proxies in 2019, May captcha was introduced at site, so you should deal with it for bot. When bot asks to pass captcha, you should just open site as usual, place pixel anywhere and then press any key in bot shell window.
- Moon canvas support will not be implemented in bot. That canvas is created for veteran users who build with their hands a lot, so it is supposed to be a bot-free area.
- To build EXE by yourself, look [here](./Build.md).

# Usage:
## For all apps:
- To get help, launch app with `help` command;
- To get app version, launch it with `version` command;
- To get updates without running, launch app with `checkUpdates` command;
- To start app, launch it with `run` command:
    - Parameter order is not important;
    - By default logs are saved to `%appdata%/PixelPlanetTools/logs/<app name>`;
    - Log files are deleted from default log folder if older than one week;
    - To specify path to your own log file, use `--logFilePath` (if log file exists, new lines are appended);
    - To display debug logs in console (a lot of useless info), specify `--showDebug` parameter;
    - To disable automatic updates before running, specify `--disableUpdates` parameter.

## PixelPlanetBot
Program that builds picture (surprisingly).

### Parameters:
- `-x, --leftX` - **required**, X coordinate of left picture pixel;
- `-y, --topY` - **required**, Y coordinate of top picture pixel;
- `-i, --imagePath` - **required**, URI (URL or path) of image that is built;
- `-d, --defenseMode` - makes bot stay opened when picture is finished to provide picture integrity, disabled by default;
- `--notificationMode` - defines bot behaviour when captcha appears, possible values: `none`, `sound` (default value), `browser`, `both`;
- `--placingOrder` - determines pixels priority for bot, possible values: `random` (default value), `left`, `right`, `top`, `bottom`, `outline`, `color`, `colorDesc`, `colorRnd`, combined directional (e.g. `leftTop`, `bottomRight`), color-directional (e.g. `colorTop`, `colorDescBottom`, `colorRndRight`), `mask`, mask modes with color or direction criteria (e.g. `maskTop`, `maskColorDesc`).  
All possible modes are listed [here](./ModeList.md);
- `--proxyAddress` - proxy that is used by bot, address includes port, empty by default;
- `--proxyUsername` - username for connecting to proxy, empty by default, no proxy authorization if not specified;
- `--proxyPassword` - password for connecting to proxy, empty by default;
- `--useMirror` - if specified, changes base address to [fuckyouarkeros.fun](https://fuckyouarkeros.fun) (site mirror);
- `--serverUrl` - if specified, changes base address to your custom one - for those who deployed their own PixelPlanet copy;
- `--brightnessMaskPath` - brightness mask for mask placing modes, should be of same size as image that is being built; the brighter is pixel at mask, the earlier corresponding pixel is placed. 16-bit color is supported for this option;
- `--captchaTimeout` - if specified and greater than zero, bot will wait corresponding amount of time (in seconds) for user to solve captcha instead of waiting for key press.

### Examples
- `bot.exe run -x 123 -y 456 -i image.png` - basic usage, `image.png` should be located in same folder with bot executable.
- `bot.exe run --useMirror --imagePath http://imagehosting.example/image.png --leftX -123 -d --topY -456 --logFilePath D:\myLogs\bot.log --captchaTimeout 30`
- `bot.exe run --notificationMode both --proxyAddress 1.2.3.4:5678 -i "relative path\with spaces\in double\quotes.png" --defenseMode --placingOrder left -x 123 -y 456 --disableUpdates`

### Notes:
- `--useMirror` and `--serverUrl` options are not compatible;
- Fully transparent parts of image are ignored.

## PixelPlanetWatcher
Program that logs updates in given rectangle to the binary file.

### Parameters:
- `-l, --leftX` - **required**, X coordinate of left rectangle boundary;
- `-r, --rightX` - **required**, X coordinate of right rectangle boundary;
- `-t, --topY` - **required**, Y coordinate of top rectangle boundary;
- `-b, --bottomY` - **required**, Y coordinate of bottom rectangle boundary;
- `--useMirror` - if specified, changes base address to [fuckyouarkeros.fun](https://fuckyouarkeros.fun) (site mirror);
- `--serverUrl` - if specified, changes base address to your custom one - for those who deployed their own PixelPlanet copy;

### Notes:
- Changes are written to the file every minute.
- When closing with Ctrl+C, changes are also written to the file.
- DO NOT close app with `X` button if you do not want to lose pixels placed after last save.

## RecordVisualizer
Program that creates image sequence from files created by **Watcher**.

### Parameters:
- `-f, --fileName` - **required**, path (relative/absolute) to file that is visualized;
- `--oldRecordFile` - enables old format mode (if file was recorded with version older than 2.0);
