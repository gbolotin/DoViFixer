# Streaming boxes and Profile 7

Streaming boxes cannot decode the enhancement layer of a Profile 7 movie. What you see depends on the box and the player app.

<img class="illustration" src="../assets/playback.svg" alt="Animation with simulated pictures: a Profile 7 movie on Apple TV 4K, NVIDIA Shield TV and Amazon Fire TV, then the same movie after conversion to Profile 8.1">

!!! note
    The pictures are simulated, to show the kind of difference. Results depend on the player app, its settings and the firmware. Check your own setup.

## Apple TV 4K

Apple TV cannot decode Profile 7. Most player apps play only the **HDR10 base layer**. You lose the scene-by-scene Dolby Vision metadata, so the picture is mapped one way for the whole movie. It often looks flatter, with dimmer highlights or darker shadows.

**After conversion:** the box plays the movie in Dolby Vision, from the same base layer and metadata.

## NVIDIA Shield TV

The Shield plays a Profile 7 movie in Dolby Vision from the base layer and the RPU, and **drops the enhancement layer**.

- For a MEL movie, that is fine. Nothing visible is lost.
- For a complex FEL movie, the metadata describes a brighter picture than the base layer holds. Highlights can look too dim, or the brightness can be wrong from scene to scene.

**After conversion:** MEL and simple FEL movies look the same as before, in a file that every box and app can play. DoViFixer finds complex FEL movies and skips them, so you know which ones need a UHD Blu-ray player.

## Amazon Fire TV

It depends on the model and the player app. Some combinations play only the **HDR10 base layer**, like Apple TV. Others play Dolby Vision and **drop the enhancement layer**, like the Shield.

**After conversion:** Fire TV models with Dolby Vision play the movie in Dolby Vision.

## Summary

| Player | Profile 7 movie | After DoViFixer (Profile 8.1) |
| --- | --- | --- |
| Apple TV 4K | HDR10 only in most apps | Dolby Vision |
| NVIDIA Shield TV | Dolby Vision without the EL | Dolby Vision, same picture |
| Amazon Fire TV | HDR10, or Dolby Vision without the EL | Dolby Vision on models with Dolby Vision |
| UHD Blu-ray player | Full Dolby Vision with the EL | Dolby Vision without the EL |

A converted movie no longer has its enhancement layer. To keep the full Profile 7 movie for a UHD Blu-ray player, keep the original, or turn on the [enhancement-layer backup](guide/restore.md).
