## Self-sufficient modes
Ready to use. Cannot be combined with other modes.
- `Random`;
- `Outline` - draws voxels near to air and color borders first, then inner ones.

## "First, sort by this" modes
Kinds of sort by some criteria, can be combined with second criteria. If no second criteria is used, then it is being considered random.
- By direction:
  - `AscX` - ascending by X (and so on);
  - `DescX` - descending by X (and so on);
  - `AscY`;
  - `DescY`;
  - `AscZ` or `Bottom`;
  - `DescZ` or `Top`;
- By color:
  - `Color` - ordered by color, same way as palette at site;
  - `ColorDesc` - opposite order;
  - `ColorRnd` - palette are mixed randomly before sort.

## "Then, sort by this" modes
Defines order of pixels that have same value by first criteria; do not use these alone - bot will refuse to launch.
- By direction:
  - `ThenAscX`;
  - `ThenDescX`;
  - `ThenAscY`;
  - `ThenDescY`;
  - `ThenAscZ` or `ThenBottom`;
  - `ThenDescZ` or `ThenTop`;
- By color:
  - `ThenColor`;
  - `ThenColorDesc`;
  - `ThenColorRnd`.

## "First"-"then" modes combined
Use these if you want two criteria sort.
- By direction:
  - `AscXTop`;
  - `AscXBottom`;
  - `DescXTop`;
  - `DescXBottom`;
  - `AscYTop`;
  - `AscYBottom`;
  - `DescYTop`;
  - `DescYBottom`;
  - `TopAscX`;
  - `TopDescX`;
  - `TopAscY`;
  - `TopDescY`;
  - `BottomAscX`;
  - `BottomDescX`;
  - `BottomAscY`;
  - `BottomDescY`;
- By color and direction:
  - `ColorAscX`;
  - `ColorDescX`;
  - `ColorAscY`;
  - `ColorDescY`;
  - `ColorTop`;
  - `ColorBottom`;
  - `ColorDescAscX`;
  - `ColorDescDescX`;
  - `ColorDescAscY`;
  - `ColorDescDescY`;
  - `ColorDescTop`;
  - `ColorDescBottom`;
  - `ColorRndAscX`;
  - `ColorRndDescX`;
  - `ColorRndAscY`;
  - `ColorRndDescY`;
  - `ColorRndTop`;
  - `ColorRndBottom`;

## Miscellaneous
- Mode names are case insensitive;
- If you lack some weird modes (for example, "top to bottom then ordered by color"), you can pass it like this: `Top,ThenColor`.  
If you use spaces after commas, parenthesis are required: `"Top, ThenColor"`.
