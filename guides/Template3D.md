## General
- User guide for Sproxel is located [here](https://code.google.com/archive/p/sproxel/wikis/UserManual.wiki) ([webarchive backup](https://web.archive.org/web/20210118191307/https://code.google.com/archive/p/sproxel/wikis/UserManual.wiki));
- Sproxel can be found:
  - in original Google Code repo - [here](https://code.google.com/archive/p/sproxel/downloads) (`sproxel-0.6-win32.zip`);
  - built (by Wirtos) from [fork with some improvements](https://github.com/emilk/sproxel) - [here](https://mega.nz/file/XQ9g0DbJ#59vreUWWwLN7oLGnWYaN3XTYqGS8v-MjIcot3YmGGow);

### Template orientation:
![X horizontal, Y vertical -> XY intersection in top left corner](https://i.imgur.com/rICbUBq.png)

### Export as following:
`File` - `Export` - `File type`:`Sproxel CSV files`

### Explaining the format

Example:  
![example of voxel template](https://i.imgur.com/WzgSM1L.png)

Corresponding CSV file (it's just text document) looks like this:
```
3,4,5
#000000FF,#00000000,#00000000
#FFFFFFFF,#00000000,#00000000
#FFFFFFFF,#00000000,#000000FF
#FFFFFFFF,#00000000,#00000000
#FFFFFFFF,#00000000,#00000000

#00000000,#00000000,#00000000
#00000000,#00000000,#00000000
#00000000,#00000000,#FFFFFFFF
#00000000,#00000000,#00000000
#00000000,#00000000,#00000000

#00000000,#00000000,#00000000
#00000000,#00000000,#00000000
#000000FF,#00000000,#00000000
#00000000,#00000000,#00000000
#00000000,#00000000,#00000000

#00000000,#00000000,#FFFFFFFF
#00000000,#00000000,#000000FF
#FFFFFFFF,#00000000,#000000FF
#00000000,#00000000,#000000FF
#00000000,#00000000,#000000FF
```
Numbers in the first line are size by X (`sizeX`), size by Z (`height`) and size by Y (`sizeY`).  
Every block of lines (there are `height` of them) is a slice by Z - `sizeX * sizeY` rectangle: it has `sizeY` lines, each contains `sizeX` voxels.  
If color ends with `FF`, it is interpreted as colored voxel, otherwise as empty one (So don't change "alpha" value of color in editor, bot will ignore pixel if alpha is not 255, which is two last `FF` letters).
  
There are 4 slices in example; each is 3x5. `#000000FF` is black, `#FFFFFFFF` is white and `#00000000` is transparent.
