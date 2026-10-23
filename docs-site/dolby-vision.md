# How Dolby Vision works

## HDR10 and Dolby Vision

An HDR movie stores brighter highlights and more colors than a normal movie. Your TV cannot show all of it, so it must *map* the movie to what the screen can do.

- **HDR10** gives the TV one set of numbers for the whole movie, such as the brightest pixel. The TV maps every scene the same way.
- **Dolby Vision** adds metadata for every scene, even every frame. The TV maps each scene for its own brightness. Dark scenes keep their detail, and bright scenes keep their highlights.

## The three parts of a Profile 7 movie

UHD Blu-ray movies use Dolby Vision **Profile 7**. A Profile 7 video track carries three parts.

<img class="illustration" src="../assets/dv-layers.svg" alt="Animation: the base layer, the RPU metadata and the enhancement layer stack up and combine into the full Dolby Vision picture">

**Base layer (BL)**
:   A normal 4K HDR10 picture with 10-bit color. Any HDR10 TV or player can show it alone.

**RPU (Reference Processing Unit)**
:   The Dolby Vision metadata: how bright each scene is and how to map it. This is what makes the picture "Dolby Vision".

**Enhancement layer (EL)**
:   Extra video data on top of the base layer, at 1080p. There are two kinds:

    - **MEL, minimal enhancement layer:** almost empty. The base layer already holds the whole picture.
    - **FEL, full enhancement layer:** real picture data. It can add detail, 12-bit color and, in some movies, brightness above the base layer.

A UHD Blu-ray player decodes all three parts and combines them into the final picture.

## Profile 8.1

Streaming services use Dolby Vision **Profile 8.1**. A Profile 8.1 video has a base layer and an RPU, but no enhancement layer. Streaming boxes such as Apple TV 4K, NVIDIA Shield TV and many Amazon Fire TV models play Profile 8.1 in Dolby Vision.

| | Profile 7 | Profile 8.1 |
| --- | --- | --- |
| Base layer | 4K HDR10 | 4K HDR10, the same picture |
| RPU metadata | Yes | Yes, converted from Profile 7 |
| Enhancement layer | MEL or FEL | None |
| Plays in Dolby Vision on | UHD Blu-ray players | Streaming boxes and most smart TV apps |

## Why conversion works

For a MEL movie, the enhancement layer adds nothing you can see. Converting it to Profile 8.1 keeps the base layer and the scene-by-scene metadata, so a streaming box shows the same Dolby Vision picture as a Blu-ray player.

For a FEL movie, it depends on what the enhancement layer does. See [Before you convert](before-you-convert.md).
