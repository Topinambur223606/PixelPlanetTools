# PixelPlanetBot
Bot for [pixelplanet.fun](https://pixelplanet.fun).

All canvases except moon are supported in bot, all 2D canvases are supported in watcher/visualizer.

You can download executable files [here](../../releases/latest).

### Some important stuff
- PixelPlanet recognizes user by IP, so launching multiple bots on same computer \/ from computers in same LAN would not work.
- Admins allow building botted arts only in Antarctica (however, deep ocean is usually also fine). They are not allowed to use in active map segment. Such botted art are usually deleted or moved to Antarctica.
- Dithering or something similar is not provided in bot, color is picked as closest available; use PixelPlanet converter (in user section) or Photoshop "to web" export to get image for building.
- After mass attack of russian griefers with proxies in 2019, May captcha was introduced at site, so you should deal with it for bot. When bot asks to pass captcha, you should just open site as usual and place any pixel manually.
- Moon canvas is not planned to be available in bot. That canvas is created for veteran users who build with their hands a lot, so it is supposed to be a bot-free area.
- To build EXE by yourself, look [here](guides/Build.md).

# Usage:
## For all apps:
- To get help, launch app with `help` command;
- To get app version, launch it with `version` command;
- To get updates without running, launch app with `checkUpdates` command;
- To start app, launch it with `run` command:
  - Parameter order is not important;
  - By default logs are saved to `%appdata%/PixelPlanetTools/logs/<app name>`;
  - Log files are deleted from default log folder if older than one week;
  - To specify canvas, use `-c` or `--canvas`, possible values: earth, moon, voxel, covid, oneBit;
  - To specify path to your own log file, use `--logFilePath` (if log file exists, new lines are appended);
  - To display debug logs in console (a lot of useless info), specify `--showDebug` parameter;
  - To disable automatic updates before running, specify `--disableUpdates` parameter.
  - Since all apps connect to PixelPlanet server, network related options are common too:
    - `--proxyAddress` - proxy that is used by bot, address includes port, empty by default;
    - `--proxyUsername` - username for connecting to proxy, empty by default, no proxy authorization if not specified;
    - `--proxyPassword` - password for connecting to proxy, empty by default;
    - `--useMirror` - if specified, changes base address to [fuckyouarkeros.fun](https://fuckyouarkeros.fun) (site mirror);
    - `--serverUrl` - if specified, changes base address to your custom one - for those who deployed their own PixelPlanet copy;
    - note that `--useMirror` and `--serverUrl` options are not compatible;

## PixelPlanetBot
Program that builds picture (surprisingly).
Besides `run` (and `run3d`) command, it also has `sessions` command used for logging in.

### Run parameters (both regular and 3D modes):
- `-i, --imagePath` - **required**, URI (URL or path) of image that is built (or CSV document exported from Sproxel in case of 3D canvas);
- `-d, --defenseMode` - makes bot stay opened when picture is finished to provide picture integrity, disabled by default;
- `--notificationMode` - defines bot behaviour when captcha appears, possible values: `none`, `sound` (default value), `browser`, `both`;
- `--captchaTimeout` - if specified and greater than zero, bot will wait corresponding amount of time (in seconds) for user to solve captcha instead of waiting for key press.
- `-s, --session` - name of already created session to be loaded;

### Run parameters (only regular - `run`):
- `-x, --leftX` - **required**, X coordinate of left picture pixel;
- `-y, --topY` - **required**, Y coordinate of top picture pixel;
- `--placingOrder` - determines pixels priority for bot;
- `--brightnessMaskPath` - brightness mask for mask placing modes, should be of same size as image that is being built; the brighter is pixel at mask, the earlier corresponding pixel is placed. 16-bit color is supported for this option;

### Run parameters (only 3D - `run3d`):
- `-x, --minX` - **required**, X coordinate of template min-X border;
- `-y, --minY` - **required**, Y coordinate of template min-Y border;
- `-z, --bottomZ` - Z coordinate (height) of bottom voxel, 0 by default;
- `--placingOrder` - determines voxels priority for bot;

### Session parameters:
- `-a, --add` - if specified, new session is created, username and password are required, session name is optional (default - PixelPlanet user name);
- `-r, --remove` - if specified, existing session is logged out and deleted, session name is required;
- `-u, --username` - username or email for logging in;
- `-p, --password` - password for logging in;
- `-s, --session` - custom name for new session or name of session to be deleted, depending on context;
- `-l, --list` - if specified and other operations do not cause error (or are absent), all existing session names are printed before exit;

### Examples
- `bot.exe run -x 123 -y 456 -i image.png` - basic usage, `image.png` should be located in same folder with bot executable.
- `bot.exe run --useMirror --imagePath http://imagehosting.example/image.png --leftX -123 -d --topY -456 --logFilePath D:\myLogs\bot.log --captchaTimeout 30`
- `bot.exe run --notificationMode both --proxyAddress 1.2.3.4:5678 -i "relative path\with spaces\in double\quotes.png" --defenseMode --placingOrder left -x 123 -y 456 --disableUpdates`
- `bot.exe sessions --list -a -u "email@sample.text" -p "password" -s mySession`
- `bot.exe run3d -s mySession -i template.csv -x 123 -y 456 -z 10 --placingOrder topAscX`

### Notes:
- Fully transparent parts of image are ignored at regular (2D) canvases and are cleaned in 3D;
- Short guidelines how to create 3D templates: [here](./guides/Template3D.md);
- All possible 2D placing modes are listed [here](guides/ModeList.md);
- All possible 3D placing modes are listed [here](guides/ModeList3D.md);
- If account is required to build on canvas, session is required to build there using bot;
- Default canvas is `voxel` for 3D mode, so you don't have to specify it (unless PixelPlanet dev adds new 3D canvas);
- PixelPlanet shows height at 3D canvas as second coordinate (Y), but it is third (Z) for bot.

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
  
## Used things (software, libraries, etc)
- Original idea based on [woyken/pixelplanet.fun-bot](https://github.com/Woyken/pixelplanet.fun-bot)
- Direct references - [Command Line Parser Library](https://github.com/commandlineparser/commandline), [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp), [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json)
- Slightly modified [websocket-sharp](https://github.com/sta/websocket-sharp)
- [Sproxel](https://code.google.com/archive/p/sproxel/) and [its fork](https://github.com/emilk/sproxel) as external 3D template editor
