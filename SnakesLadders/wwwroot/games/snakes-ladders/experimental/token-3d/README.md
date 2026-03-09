# Token 3D Experiment

This folder contains an isolated prototype for rendering real 3D character tokens on the board.

## Current trigger
- Prototype is mapped to `avatarId = 9`.
- Only board tokens and transit/move tokens use the 3D model.
- Lobby picker, winner overlays, and other avatar UI still use the existing 2D avatar system.

## Removal
To remove this experiment cleanly:
1. Remove the extra 3D `<link>` and `<script>` tags from `wwwroot/index.html`.
2. Remove the two hooks to `root.experimentalToken3d` from:
   - `wwwroot/games/snakes-ladders/js/board/sn-board-token-layer.js`
   - `wwwroot/games/snakes-ladders/js/board/sn-piece-transit.js`
3. Delete this `experimental/token-3d/` folder.

## Current asset
- `avatarId = 9` currently loads `/games/snakes-ladders/experimental/token-3d/assets/blue_archive_-wakamo_swimsuit-.glb`.
