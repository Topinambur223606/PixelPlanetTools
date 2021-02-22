## Self-sufficient modes
Ready to use. Cannot be combined with other modes.
- `Random`;
- `Outline` - draws image border first, then pixels that have more different neighbours, then pixels inside uniformly filled areas.

## "First, sort by this" modes
Kinds of sort by some criteria, can be combined with second criteria. If no second criteria is used, then it is being considered random.
- `Mask` - uses brightness mask image to determine the order, the brighter mask pixel is, the earlier corresponding pixel is placed;
- By direction:
  - `Left` - from left to right;
  - `Right`- from right to left;
  - `Top` - from top to bottom;
  - `Bottom` - from bottom to top;
- By color:
  - `Color` - ordered by color, same way as palette at site;
  - `ColorDesc` - opposite order;
  - `ColorRnd` - palette are mixed randomly before sort.

## "Then, sort by this" modes
Defines order of pixels that have same value by first criteria; do not use these alone - bot will refuse to launch.
- By direction:
  - `ThenLeft`;
  - `ThenRight`;
  - `ThenTop`;
  - `ThenBottom`;
- By color:
  - `ThenColor`;
  - `ThenColorDesc`;
  - `ThenColorRnd`.

## "First"-"then" modes combined
Use these if you want two criteria sort.
- By direction:
  - `LeftTop`;
  - `LeftBottom`;
  - `RightTop`;
  - `RightBottom`;
  - `TopLeft`;
  - `TopRight`;
  - `BottomLeft`;
  - `BottomRight`;
- By color and direction:
  - `ColorTop`;
  - `ColorBottom`;
  - `ColorLeft`;
  - `ColorRight`;
  - `ColorDescTop`;
  - `ColorDescBottom`;
  - `ColorDescLeft`;
  - `ColorDescRight`;
  - `ColorRndTop`;
  - `ColorRndBottom`;
  - `ColorRndLeft`;
  - `ColorRndRight`;
- By mask and color/direction:
  - `MaskTop`;
  - `MaskBottom`;
  - `MaskLeft`;
  - `MaskRight`;
  - `MaskColor`;
  - `MaskColorDesc`;
  - `MaskColorRnd`;

## Miscellaneous
- Mode names are case insensitive;
- If you lack some weird modes (for example, "left to right then ordered by color"), you can pass it like this: `Left,ThenColor`.  
If you use spaces after commas, parenthesis are required: `"Left, ThenColor"`.
