# Stillwater Relay

![Stillwater Relay](./stillwater-relay.png)

- **Created:** 2026-10-06 (Asia/Manila)
- **Windows Computer Use:** `0.4.0` (live host assembly `0.4.0.0`)
- **WCU gesture implementation:** `c4c1057` — bounded drag gestures
- **HostPlane adapter:** OpenCode Connect `0.7.0`
- **Deployed HostPlane build:** `434a7f64ab3d3018`
- **Adapter build commit:** `1bf2186c5ded931f577c6c404164d97fa3731d41`
- **Artist/operator:** ChatGPT / 0xOperator, commissioned by Jes
- **Application:** Microsoft Paint `11.2605.81.0`
- **Canvas:** 1152 × 648
- **SHA-256:** `410e178ea74461503261d22bcb72f9386ada838f78a6b82cc8893d5350633a0d`

## The piece

A warm moon hangs over three layers of blue mountains. Faceted snowcaps, quiet stars, pine silhouettes and broken reflections frame a still lake. The piece continues the signal theme of earlier mementos, with a calmer, more composed landscape.

## How it was made

The artwork was drawn in a fresh, visible Paint window through the deployed computer-use implementation:

`ChatGPT → computer.* → OpenCode Connect HostPlane → persistent Windows bridge → WCU 0.4.0 → Microsoft Paint`

Custom colors were entered through the native color dialog using semantic value setting and exact element waits. Continuous closed pencil strokes and Paint's fill tool formed the mountains, snowcaps, lake, trees and shore. Oval gestures formed the moon and halo; smaller strokes supplied stars and reflections. Slider focus plus arrow keys selected verified 4-pixel and 2-pixel pencil widths.

Paint itself exported the PNG through its Save as dialog directly into this repository. The saved PNG was read back, visually inspected and checked for its dimensions and SHA-256. No external image generator or programmatic raster renderer created the artwork.

A retained subset of **13 construction sequences** contains **199 steps**, including **54 drag gestures**, and totals **46.84 seconds** of HostPlane-reported sequence execution. This is partial action telemetry, **not** an end-to-end benchmark: it excludes discovery, separate setup and recovery calls, screenshot inspection, save operations, and operator/tool round-trip time.

## Observations

- A semantic invocation of Edit colors opened the dialog but timed out and left the initial blank Paint instance unresponsive. That empty instance was replaced before drawing. Pointer opening plus exact dialog waits worked for the subsequent palette edits; the underlying invoke issue was not fixed in this session.
- Direct slider value setting and semantic traversal of the Save as dialog returned errors. Verified slider focus with arrow keys, and pointer/keyboard input for the filename, completed those operations.

These observations are preserved as session evidence, not claims of a diagnosed root cause or repaired implementation.
