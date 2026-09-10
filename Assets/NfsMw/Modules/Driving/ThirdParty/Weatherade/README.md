# Weatherade rain assets

`Particles/RainDrop.tif` and `Particles/RainDrop_n.tif` are byte-for-byte copies
from the user-supplied **Weatherade Snow and Rain System v1.1.6** Unity package.
Their original Unity metadata and GUIDs are retained.

Weatherade's original GPU renderer targets Unity's Built-in Render Pipeline and
uses a `GrabPass`, which is not supported by this HDRP project. The local
`WeatheradeRainHDRP` shader reads the same drop texture convention: red is the
far opacity profile and green is the softened near-camera profile. A dedicated
HDRP distortion-vector pass uses the original normal texture to preserve the
package's refractive drop appearance without relying on `GrabPass`.

The demo copies the package's authored rain values for its primary layer:
50-by-50-metre emitter footprint, 20-metre follow offset, 0.006-0.011-metre
particle size, 3-5-second lifetime, -10 to -7 m/s vertical velocity, lateral
velocity ranges of -1 to 0 and -2 to 0 m/s, and a stretch multiplier of 14.
